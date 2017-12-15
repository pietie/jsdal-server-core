if (not exists (select top 1 1 from sys.schemas where name = 'orm'))  EXEC ('CREATE SCHEMA orm')

if (not exists (select top 1 1 from sys.tables where name ='SprocDalMonitor' and SCHEMA_NAME(schema_id) = 'orm')) 
BEGIN
	CREATE TABLE [orm].[SprocDalMonitor](
		[Id] [int] IDENTITY(1,1) NOT NULL,
		[CreateDateUtc] [datetime] NOT NULL,
		[LastUpdateDateUtc] [datetime] NULL,
		[CatalogName] [varchar](500) NOT NULL,
		[SchemaName] [varchar](500) NOT NULL,
		[RoutineName] [varchar](500) NOT NULL,
		[RoutineType] [varchar](20) NOT NULL,
		[ReturnType] [varchar](20) NULL,
		[rowver] [timestamp] NOT NULL,
		[IsDeleted] [bit] NOT NULL,
		[LastUpdateByHostName] [varchar](100) NULL,
		[ParametersXml] [varchar](max) NULL,
		[ParameterCount] [int] NULL,
		[JsonMetadata] [varchar](max) NULL
	 CONSTRAINT [PK_SprocDalMonitor] PRIMARY KEY CLUSTERED 
	(
		[Id] ASC
	)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON, FILLFACTOR = 90) ON [PRIMARY]
	) ON [PRIMARY]


	ALTER TABLE [orm].[SprocDalMonitor] ADD  CONSTRAINT [DF_SprocDalMonitor_CreateDateUtc]  DEFAULT (getutcdate()) FOR [CreateDateUtc]
	ALTER TABLE [orm].[SprocDalMonitor] ADD  CONSTRAINT [DF_SprocDalMonitor_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
	CREATE NONCLUSTERED INDEX [IX_SprocDalMonitor_Rowver] ON [orm].[SprocDalMonitor] ([rowver] ASC)
	CREATE NONCLUSTERED INDEX [IX_SprocDalMonitor_CatSchName] ON [orm].[SprocDalMonitor] ([CatalogName] ASC, [SchemaName] ASC, [RoutineName] ASC) 
END

if (not exists (select top 1 1 from sys.procedures where name ='ParamsGetDetail' and SCHEMA_NAME(schema_Id) = 'orm'))
BEGIN
	EXEC('CREATE FUNCTION orm.ParamsGetDetail
(
	@catalog varchar(500)
	,@schema varchar(500)
	,@routine varchar(500)
)
RETURNS @ret TABLE (ParmName varchar(max), HasDefault bit)
AS
BEGIN

	DECLARE @routineSource varchar(max)

	select @routineSource = object_definition(object_id(QUOTENAME(@catalog) + ''.'' + QUOTENAME(@schema) + ''.'' + QUOTENAME(@routine)))

	DECLARE @commentLookup TABLE (ID INT IDENTITY(1,1), StartIx INT, EndIx INT)

	-- start by removing all comments

	declare @i int = 1
			,@ch char(1)
			,@nextCh char(1)

			,@inMultiLineComment bit = 0
			,@inSingleLineComment bit = 0

			,@curCommentLookupId INT
			,@commentEndIx INT
			,@foundEndOfComment bit = 0

			,@len INT = len(@routineSource) + 1
			,@startIx INT

	while (@i < @len)
	begin
		set @ch = substring(@routineSource,@i,1)
		set @nextCh = substring(@routineSource,@i+1,1)

		if (@inMultiLineComment = 0 AND @inSingleLineComment = 0)
		begin
			if (@ch = ''/'' and @nextCh = ''*'')
			begin
				set @inMultiLineComment = 1
				set @commentEndIx = null

				insert into @commentLookup (StartIx) values (@i)
				select @curCommentLookupId = SCOPE_IDENTITY()

				set @startIx = @i

				set @i += 1
			end
			else if (@ch = ''-'' and @nextCh = ''-'')
			begin
				set @inSingleLineComment = 1
				set @commentEndIx = null

				insert into @commentLookup (StartIx) values (@i)
				select @curCommentLookupId = SCOPE_IDENTITY()

				set @startIx = @i

				set @i += 1
			end
		end
		else if (@inMultiLineComment = 1 and @inSingleLineComment = 0)
		begin

			if ((@ch = ''*'' and @nextCh = ''/'') OR (@i = len(@routineSource)/*auto close multiline comment if this is the last character*/))
			begin
				set @foundEndOfComment = 1;
				set @i += 1;
				set @commentEndIx = @i + 1;
			end
		end
		else if (@inMultiLineComment = 0 AND @inSingleLineComment = 1 AND (@ch = char(10) OR @i = len(@routineSource))) -- single line comments end at a newline (or as last line in source)
		begin
			set @foundEndOfComment = 1;
			set @commentEndIx = @i;

			if (@i = len(@routineSource)) set @commentEndIx += 1;
		end

		if (@foundEndOfComment = 1)
		begin
			set @inSingleLineComment = 0
			set @inMultiLineComment = 0

			update @commentLookup
				set EndIx = @commentEndIx
			where id= @curCommentLookupId

			set @routineSource = LEFT(@routineSource, @startIx-1) + SUBSTRING(@routineSource, @startIx + (@commentEndIx - @startIx), 99999)
			
			set @len -= @commentEndIx - @startIx;
			set @i -= @commentEndIx - @startIx;
			

			set @curCommentLookupId = null	
			set @foundEndOfComment = 0
		end


		set @i += 1;
	end
	

	-- find the position of the ''AS'' keyword and catalog the strings

	DECLARE @asKeywordIx INT
	DECLARE @stringLookup TABLE (ID INT IDENTITY(1,1), StartIx INT, EndIx INT)
	DECLARE @curStringLookupId INT

	DECLARE @isInString bit
			,@token varchar(3)

	set @isInString = 0
	
	set @i = 1;
	while (@i <= len(@routineSource))
	begin
		set @ch = substring(@routineSource,@i,1)
		set @nextCh = substring(@routineSource,@i+1,1)	

		set @token = REPLACE(REPLACE(REPLACE(LOWER(substring(@routineSource,@i,3)), char(10), ''''), char(13), ''''), '' '','''')

		if (@isInString = 0)
		begin
			if (@ch = '''''''') 
			begin
				set @isInString = 1
				insert into @stringLookup (StartIx) values (@i)
				select @curStringLookupId = SCOPE_IDENTITY()
			end
		end
		else
		begin
			if (@ch = '''''''' AND @nextCh != '''''''') begin
				set @isInString = 0
				update @stringLookup set EndIx = @i where id = @curStringLookupId
			end
			else if (@ch = '''''''' AND @nextCh = '''''''') set @i += 1 -- skip escaped quote
		end

		set @i += 1
	end

	-- replace ALL string contents with something else so that we can be sure there are no conflicting keywords or ''parm declarations'' in the strings themselves
	select @routineSource = STUFF(@routineSource, StartIx+1, EndIx-StartIx-1, REPLICATE(''x'', EndIx-StartIx-1)) from @stringLookup
	
	/*
		1. Run through each expected parameter, in the order they are defined
		2. For each, look for first occurences of [Name]+[Whitespace]
		3. Move on to next declaration
		4. After finding starting positions of all go back and fill in end positions
		5. Look for equal sings
	*/
		
	
	DECLARE @parmDeclarations varchar(max) 
	
	set @parmDeclarations = @routineSource
	
	-- find starting point of each parm declaration
	DECLARE @parmLookup TABLE (ID INT IDENTITY(1,1), Name nvarchar(500), Ix INT, HasDefault bit default 0, DefaultValue varchar(max))

	DECLARE @parmName varchar(600)
			,@charIndexOffset INT 

	DECLARE cur CURSOR LOCAL FOR   
	select p.PARAMETER_NAME from INFORMATION_SCHEMA.PARAMETERS p with(nolock)
	where p.SPECIFIC_CATALOG = @catalog and p.SPECIFIC_SCHEMA = @schema and p.SPECIFIC_NAME = @routine and p.IS_RESULT != ''YES''
	order by p.ORDINAL_POSITION

	OPEN cur  
	FETCH NEXT FROM cur INTO @parmName 
	WHILE @@FETCH_STATUS = 0  
	BEGIN  

		/*
			1. Find appropriate starting place for parm declaration
			2. Walk forward to find = sign if available
			3. Determine start and end of default value and extract
			4. Stop when we hit next parm declaration or the end
		*/
		
		set @charIndexOffset = 0
	TryNext:
	
		-- the declaration as to be followed by a whitespace(space,newline, tab)
		select top 1 @i = x.Ix from
			(select charindex(@parmName+char(32), @parmDeclarations, @charIndexOffset) Ix
			union select  charindex(@parmName+char(9), @parmDeclarations, @charIndexOffset)
			union select  charindex(@parmName+char(10), @parmDeclarations, @charIndexOffset)
			union select  charindex(@parmName+char(13), @parmDeclarations, @charIndexOffset)) x
		where x.Ix > 0
		order by 1
		
		set @charIndexOffset = @i+1
	
		insert into @parmLookup (Name, Ix) values (@parmName, @i)

	FETCH NEXT FROM cur INTO @parmName
	END   
	CLOSE cur;  
	DEALLOCATE cur;

	
		DECLARE @parmDataType varchar(100)

		select top 1 @i = Ix, @parmName = Name from @parmLookup order by id desc

		select @parmDataType = p.DATA_TYPE from INFORMATION_SCHEMA.PARAMETERS p
		where p.SPECIFIC_CATALOG = @catalog
		  AND p.SPECIFIC_SCHEMA = @schema
		  AND p.SPECIFIC_NAME = @routine
		  and P.PARAMETER_NAME = @parmName

		-- move past the data type declaration to account for definitions like ''@parm AS varchar'' where the AS keyword in the definition might trip us up
		select @i = CHARINDEX(@parmDataType, @parmDeclarations, @i) + len(@parmDataType)

		;WITH CTE AS (
			select REPLACE(REPLACE(REPLACE(@parmDeclarations, char(10), '' ''), char(13),'' ''), char(9) ,'' '') Src
		)
		select @i = Ix from 
		(select CHARINDEX('' as '', Src, @i) Ix from CTE
		union select CHARINDEX('')as '', Src, @i) from CTE) x
		where x.Ix > 0
		order by 1

	
	DECLARE @nextIx INT
			,@decl varchar(max)
			,@declLen int
			,@ix INT
			,@id INT

	DECLARE cur CURSOR LOCAL FOR   
		select p.Id, p.Name ,p.Ix , IsNull(n.Ix, @i) NextIx
			from @parmLookup p
				 left join @parmLookup n on n.Id = p.Id +1
		order by p.Id

		
	OPEN cur  
	FETCH NEXT FROM cur INTO @id,@parmName, @ix, @nextIx
	WHILE @@FETCH_STATUS = 0  
	BEGIN  
	
		
		set @declLen = @nextIx-@ix-1
		set @decl = substring(@parmDeclarations, @ix, @declLen)


		set @i = charindex(''='', @decl)

		if (@i != 0) -- if we have a default
		begin
			update @parmLookup set HasDefault = 1 where ID = @id
		end

		

	FETCH NEXT FROM cur INTO @id, @parmName, @ix, @nextIx
	END   
	CLOSE cur;  
	DEALLOCATE cur;

	insert into  @ret (ParmName, HasDefault)
	select Name, HasDefault from @parmLookup order by Id	
	
	RETURN 
END')
END

if (not exists (select top 1 1 from sys.procedures where name ='SprocGenGetParameterList' and SCHEMA_NAME(schema_Id) = 'orm'))
BEGIN
	EXEC('
	CREATE procedure [orm].[SprocGenGetParameterList]
	(
		@routineName varchar(500)
		,@schemaName varchar(500)
	)
	as
	begin

		select r.SPECIFIC_CATALOG [Catalog]
				,r.SPECIFIC_SCHEMA [Schema]
				,r.SPECIFIC_NAME RoutineName
				,parms.PARAMETER_MODE ParameterMode
				,parms.IS_RESULT IsResult
				,parms.PARAMETER_NAME ParameterName
				,parms.DATA_TYPE DataType
				,parms.CHARACTER_OCTET_LENGTH Length
			from INFORMATION_SCHEMA.ROUTINES r
				 inner join INFORMATION_SCHEMA.PARAMETERS parms on parms.SPECIFIC_NAME = r.SPECIFIC_NAME
																	and parms.SPECIFIC_CATALOG = r.SPECIFIC_CATALOG
																	and parms.SPECIFIC_SCHEMA = r.SPECIFIC_SCHEMA
		where r.SPECIFIC_NAME = @routineName
		  and r.SPECIFIC_SCHEMA = @schemaName
		--order by parms.ORDINAL_POSITION
	end')
END


if (not exists (select top 1 1 from sys.procedures where name ='SprocGenExtractAndStoreJsonMetadata' and SCHEMA_NAME(schema_Id) = 'orm'))
BEGIN
	EXEC(
	'create proc [orm].[SprocGenExtractAndStoreJsonMetadata]
(
	@catalog varchar(500)
	,@schema varchar(500)
	,@routine varchar(500)
	
)
as
begin

	/*
		1. Extract all multi and single line comments
		2. Within each comment, extract what looks like json fragments
		3. Within each of those fragments look for the first one that looks like a valid jsDAL metadata definition.
	*/
	SET NOCOUNT ON;
 
	DECLARE @routineSource varchar(max)

	select @routineSource = object_definition(object_id(QUOTENAME(@catalog) + ''.'' + QUOTENAME(@schema) + ''.'' + QUOTENAME(@routine)))



	DECLARE @commentLookup TABLE (ID INT IDENTITY(1,1), StartIx INT, EndIx INT)


	declare @i int = 1
			,@ch char(1)
			,@nextCh char(1)

			,@inMultiLineComment bit = 0
			,@inSingleLineComment bit = 0

			,@curCommentLookupId INT
			,@commentEndIx INT
			,@foundEndOfComment bit = 0

	while (@i < len(@routineSource) + 1)
	begin
		set @ch = substring(@routineSource,@i,1)
		set @nextCh = substring(@routineSource,@i+1,1)

		if (@inMultiLineComment = 0 AND @inSingleLineComment = 0)
		begin
			if (@ch = ''/'' and @nextCh = ''*'')
			begin
				set @inMultiLineComment = 1
				set @commentEndIx = null

				insert into @commentLookup (StartIx) values (@i)
				select @curCommentLookupId = SCOPE_IDENTITY()

				set @i += 1
			end
			else if (@ch = ''-'' and @nextCh = ''-'')
			begin
				set @inSingleLineComment = 1
				set @commentEndIx = null

				insert into @commentLookup (StartIx) values (@i)
				select @curCommentLookupId = SCOPE_IDENTITY()

				set @i += 1
			end
		end
		else if (@inMultiLineComment = 1 and @inSingleLineComment = 0)
		begin

			if ((@ch = ''*'' and @nextCh = ''/'') OR (@i = len(@routineSource)/*auto close multiline comment if this is the last character*/))
			begin
				set @foundEndOfComment = 1;
				set @i += 1;
				set @commentEndIx = @i + 1;
			end
		end
		else if (@inMultiLineComment = 0 AND @inSingleLineComment = 1 AND @ch = char(10)) -- single line comments end at a newline
		begin
			set @foundEndOfComment = 1;
			set @commentEndIx = @i;
		end

		if (@foundEndOfComment = 1)
		begin
			set @inSingleLineComment = 0
			set @inMultiLineComment = 0

			update @commentLookup
				set EndIx = @commentEndIx
			where id= @curCommentLookupId

			set @curCommentLookupId = null

			
			set @foundEndOfComment = 0
		end


		set @i += 1;
	end

	DECLARE @comment varchar(max)
			,@isObjectOpen bit = 0
			,@curOpenCnt int = 0
			,@curFragmentId INT

	DECLARE @jsonFragments TABLE(ID INT IDENTITY(1,1), StartIx INT, EndIx INT, Fragment varchar(max))

	DECLARE cur CURSOR LOCAL FOR
		select SUBSTRING(@routineSource, StartIx, EndIx-StartIx+1)  from @commentLookup
 
	OPEN cur
	FETCH NEXT FROM cur INTO @comment
	WHILE @@FETCH_STATUS = 0
	BEGIN
		set @i = 0
		set @curFragmentId = null
		set @isObjectOpen = 0
		set @curOpenCnt = 0


		while (@i < len(@comment) + 1)
		begin
			set @ch = substring(@comment,@i,1)

			if (@ch = ''{'')
			begin

				if (@isObjectOpen = 0)
				begin
					set @isObjectOpen = 1
					set @curOpenCnt = 1;

					insert into @jsonFragments (StartIx) values (@i)
					select @curFragmentId = SCOPE_IDENTITY()
				end
				else
				begin
					set @curOpenCnt += 1;
				end
			end
			else if (@ch = ''}'' or @i = len(@comment))
			begin
				if (@isObjectOpen = 1)
				begin

					set @curOpenCnt -= 1;
					
					if (@curOpenCnt <= 0)
                    begin

                        set @isObjectOpen = 0; 
                        
						update @jsonFragments 
							set EndIx = @i,
								Fragment = SUBSTRING(@comment, StartIx, @i-StartIx+1)
						where id = @curFragmentId
						
						set @curFragmentId = null
                    end
				
				end				
			end


			set @i += 1;
		end
		
	FETCH NEXT FROM cur INTO @comment
	END
	CLOSE cur
	DEALLOCATE cur

	DECLARE @jsonMetadata varchar(max)
	-- grab the first fragment that *looks* like jsDAL metadata
	select top 1 @jsonMetadata = Fragment from @jsonFragments where Fragment like ''%jsDAL%:%{%''

	update orm.SprocDalMonitor
		set JsonMetadata = @jsonMetadata
	where CatalogName = @catalog and SchemaName = @schema and RoutineName = @routine

end')
END


if (not exists (select top 1 1 from sys.procedures where name ='SprocGenInitialise' and SCHEMA_NAME(schema_Id) = 'orm'))
BEGIN
	EXEC('
	CREATE PROCEDURE [orm].[SprocGenInitialise]
AS
begin


	-- INSERT NEW ROUTINES
	INSERT INTO orm.SprocDalMonitor (CatalogName, SchemaName, RoutineName, RoutineType,ReturnType, IsDeleted, LastUpdateByHostName, ParametersXml, ParameterCount)
	select r.SPECIFIC_CATALOG [Catalog]
			,r.SPECIFIC_SCHEMA [Schema]
			,r.SPECIFIC_NAME RoutineName
			,r.ROUTINE_TYPE RoutineType
			,r.DATA_TYPE ReturnType
			,0
			,HOST_NAME()
			,(
				select routine.SPECIFIC_CATALOG [Catalog]
						,routine.SPECIFIC_SCHEMA [Schema]
						,routine.SPECIFIC_NAME RoutineName
						,Parameter.PARAMETER_MODE ParameterMode
						,Parameter.IS_RESULT IsResult
						,Parameter.PARAMETER_NAME ParameterName
						,Parameter.DATA_TYPE DataType
						,Parameter.CHARACTER_OCTET_LENGTH Length
						,ISNULL(x.HasDefault,0) HasDefault
					from INFORMATION_SCHEMA.ROUTINES Routine
							inner join INFORMATION_SCHEMA.PARAMETERS Parameter on Parameter.SPECIFIC_NAME = routine.SPECIFIC_NAME
																			and Parameter.SPECIFIC_CATALOG = routine.SPECIFIC_CATALOG
																			and Parameter.SPECIFIC_SCHEMA = routine.SPECIFIC_SCHEMA
						left join orm.ParamsGetDetail(r.SPECIFIC_CATALOG, r.SPECIFIC_SCHEMA, r.SPECIFIC_NAME) x on x.ParmName= Parameter.PARAMETER_NAME
				where routine.SPECIFIC_NAME = r.SPECIFIC_NAME
					and routine.SPECIFIC_CATALOG = r.SPECIFIC_CATALOG
					and routine.SPECIFIC_SCHEMA = r.SPECIFIC_SCHEMA
				for xml auto, elements
			) ParametersXml
			,(
			select count(1)
					from INFORMATION_SCHEMA.ROUTINES Routine
							inner join INFORMATION_SCHEMA.PARAMETERS Parameter on Parameter.SPECIFIC_NAME = routine.SPECIFIC_NAME
																			and Parameter.SPECIFIC_CATALOG = routine.SPECIFIC_CATALOG
																			and Parameter.SPECIFIC_SCHEMA = routine.SPECIFIC_SCHEMA
				where routine.SPECIFIC_NAME = r.SPECIFIC_NAME
					and routine.SPECIFIC_CATALOG = r.SPECIFIC_CATALOG
					and routine.SPECIFIC_SCHEMA = r.SPECIFIC_SCHEMA
			) ParameterCount
		from INFORMATION_SCHEMA.ROUTINES r with (nolock)
		where not exists (select 1 from orm.SprocDalMonitor dst with(nolock) where dst.RoutineName = r.SPECIFIC_NAME and dst.SchemaName = r.SPECIFIC_SCHEMA and dst.CatalogName = r.SPECIFIC_CATALOG)


	-- UPDATE
		update 	dst
			Set LastUpdateDateUtc = getutcdate()
					,LastUpdateByHostName = HOST_NAME()
					,RoutineType = src.RoutineType
					,ReturnType = src.ReturnType
					,IsDeleted = 0
					,ParametersXml = src.ParametersXml
					,ParameterCount = src.ParameterCount
		from orm.SprocDalMonitor dst
				inner join (select r.SPECIFIC_CATALOG 
							,r.SPECIFIC_SCHEMA 
							,r.SPECIFIC_NAME
							,r.ROUTINE_TYPE RoutineType
							,r.DATA_TYPE ReturnType
							,(
								select routine.SPECIFIC_CATALOG 
										,routine.SPECIFIC_SCHEMA 
										,routine.SPECIFIC_NAME
										,Parameter.PARAMETER_MODE ParameterMode
										,Parameter.IS_RESULT IsResult
										,Parameter.PARAMETER_NAME ParameterName
										,Parameter.DATA_TYPE DataType
										,Parameter.CHARACTER_OCTET_LENGTH Length
										,ISNULL(x.HasDefault,0) HasDefault
									from INFORMATION_SCHEMA.ROUTINES Routine
											inner join INFORMATION_SCHEMA.PARAMETERS Parameter on Parameter.SPECIFIC_NAME = routine.SPECIFIC_NAME
																							and Parameter.SPECIFIC_CATALOG = routine.SPECIFIC_CATALOG
																							and Parameter.SPECIFIC_SCHEMA = routine.SPECIFIC_SCHEMA
											left join orm.ParamsGetDetail(r.SPECIFIC_CATALOG, r.SPECIFIC_SCHEMA, r.SPECIFIC_NAME) x on x.ParmName= Parameter.PARAMETER_NAME
								where routine.SPECIFIC_NAME = r.SPECIFIC_NAME
									and routine.SPECIFIC_CATALOG = r.SPECIFIC_CATALOG
									and routine.SPECIFIC_SCHEMA = r.SPECIFIC_SCHEMA
								for xml auto, elements
							) ParametersXml
							,(select count(1)
									from INFORMATION_SCHEMA.ROUTINES Routine
											inner join INFORMATION_SCHEMA.PARAMETERS Parameter on Parameter.SPECIFIC_NAME = routine.SPECIFIC_NAME
																							and Parameter.SPECIFIC_CATALOG = routine.SPECIFIC_CATALOG
																							and Parameter.SPECIFIC_SCHEMA = routine.SPECIFIC_SCHEMA
								where routine.SPECIFIC_NAME = r.SPECIFIC_NAME
									and routine.SPECIFIC_CATALOG = r.SPECIFIC_CATALOG
									and routine.SPECIFIC_SCHEMA = r.SPECIFIC_SCHEMA
							) ParameterCount
						from INFORMATION_SCHEMA.ROUTINES r) src on  dst.RoutineName = src.SPECIFIC_NAME and dst.SchemaName = src.SPECIFIC_SCHEMA and dst.CatalogName = src.SPECIFIC_CATALOG


	
	DECLARE @catalog varchar(500)
			,@schema varchar(500)
			,@routine varchar(500)

	DECLARE cur CURSOR FOR
		select CatalogName, SchemaName, RoutineName from orm.SprocDalMonitor where JsonMetadata is null
 
	OPEN cur
	FETCH NEXT FROM cur INTO @catalog, @schema, @routine
	WHILE @@FETCH_STATUS = 0
	BEGIN
		 exec [orm].[SprocGenExtractAndStoreJsonMetadata] @catalog = @catalog, @schema = @schema, @routine = @routine
	FETCH NEXT FROM cur INTO @catalog, @schema, @routine
	END
	CLOSE cur
	DEALLOCATE cur



			
end
')
END

if (not exists (select top 1 1 from sys.procedures where name ='SprocGenGetRoutineList' and SCHEMA_NAME(schema_Id) = 'orm'))
BEGIN
	EXEC('
	CREATE procedure [orm].[SprocGenGetRoutineList]
	(
		@maxRowver		bigint = 0
	)
	as
	begin

		select mon.Id
				,mon.CatalogName
				,mon.SchemaName
				,mon.RoutineName
				,case 
					when UPPER(mon.RoutineType) = ''FUNCTION'' AND UPPER(mon.ReturnType) = ''TABLE'' then ''TVF''
					else mon.RoutineType 
			 	end  RoutineType
				,cast(mon.rowver as bigint) rowver
				,mon.IsDeleted
				,mon.ParametersXml
				,mon.ParameterCount	
				,object_id(QUOTENAME(mon.CatalogName) + ''.'' + QUOTENAME(mon.SchemaName) + ''.'' + QUOTENAME(mon.RoutineName)) ObjectId
				,mon.JsonMetadata
			from orm.[SprocDalMonitor] mon with (nolock)
		where mon.rowver > @maxRowver
		order by rowver
	end')
END

if (not exists (select top 1 1 from sys.procedures where name ='SprocGenGetRoutineListCnt' and SCHEMA_NAME(schema_Id) = 'orm'))
BEGIN
	EXEC('
	CREATE procedure [orm].[SprocGenGetRoutineListCnt]
	(
		@maxRowver		bigint = 0
	)
	as
	begin

		select count(1) CNT
			from orm.[SprocDalMonitor] mon with (nolock)
		where mon.rowver > @maxRowver

	end')
END


if (not exists (select top 1 1 from sys.triggers where name = 'SprocDalMonitor' and parent_class_desc = 'DATABASE')) 
BEGIN

	DECLARE @trig nvarchar(max) 
	
	set @trig ='

	CREATE TRIGGER [SprocDalMonitor] ON DATABASE 
		FOR CREATE_PROCEDURE, ALTER_PROCEDURE, CREATE_FUNCTION, ALTER_FUNCTION, DROP_PROCEDURE, DROP_FUNCTION,  ALTER_SCHEMA, RENAME
	AS 
	BEGIN
		SET NOCOUNT ON
	
		DECLARE @eventType NVARCHAR(MAX)
				,@dbName NVARCHAR(MAX)
				,@schema NVARCHAR(MAX)
				,@objectName NVARCHAR(MAX)
				,@objectType NVARCHAR(MAX)
	
		SELECT @eventType = EVENTDATA().value(''(/EVENT_INSTANCE/EventType)[1]'',''nvarchar(max)'')
				,@dbName = EVENTDATA().value(''(/EVENT_INSTANCE/DatabaseName)[1]'',''nvarchar(max)'')
				,@schema = EVENTDATA().value(''(/EVENT_INSTANCE/SchemaName)[1]'',''nvarchar(max)'')
				,@objectName = EVENTDATA().value(''(/EVENT_INSTANCE/ObjectName)[1]'',''nvarchar(max)'')
				,@objectType = EVENTDATA().value(''(/EVENT_INSTANCE/ObjectType)[1]'',''nvarchar(max)'')

		if (@eventType = ''RENAME'')
		begin
			set @objectName = EVENTDATA().value(''(/EVENT_INSTANCE/NewObjectName)[1]'',''nvarchar(max)'')
		end
	 
		DECLARE @existingId INT
				,@isDeleted BIT 
			
		set @isDeleted = case LEFT(@eventType,5) when ''DROP_'' then 1 else 0 end

		-- look for an existing Monitor entry
		select @existingId = m.Id 
			from orm.[SprocDalMonitor] m (nolock)
		where m.CatalogName = @dbName
		  and m.SchemaName = @schema
		  and m.RoutineName = @objectName
	 
		DECLARE @parameterXml varchar(max)
			,@parameterCount int
	
		set @parameterXml = (select routine.SPECIFIC_CATALOG [Catalog]
									,routine.SPECIFIC_SCHEMA [Schema]
									,routine.SPECIFIC_NAME RoutineName
									,Parameter.PARAMETER_MODE ParameterMode
									,Parameter.IS_RESULT IsResult
									,Parameter.PARAMETER_NAME ParameterName
									,Parameter.DATA_TYPE DataType
									,Parameter.CHARACTER_OCTET_LENGTH Length
									,ISNULL(x.HasDefault,0) HasDefault
								from INFORMATION_SCHEMA.ROUTINES Routine
									 inner join INFORMATION_SCHEMA.PARAMETERS Parameter on Parameter.SPECIFIC_NAME = routine.SPECIFIC_NAME
																						and Parameter.SPECIFIC_CATALOG = routine.SPECIFIC_CATALOG
																						and Parameter.SPECIFIC_SCHEMA = routine.SPECIFIC_SCHEMA
									 left join orm.ParamsGetDetail(@dbName, @schema, @objectName) x on x.ParmName= Parameter.PARAMETER_NAME																						
							where routine.SPECIFIC_NAME = @objectName
							  and routine.SPECIFIC_CATALOG = @dbName
							  and routine.SPECIFIC_SCHEMA = @schema
							for xml auto, elements)
						

		select @parameterCount = COUNT(1)
								from INFORMATION_SCHEMA.ROUTINES Routine
									 inner join INFORMATION_SCHEMA.PARAMETERS Parameter on Parameter.SPECIFIC_NAME = routine.SPECIFIC_NAME
																						and Parameter.SPECIFIC_CATALOG = routine.SPECIFIC_CATALOG
																						and Parameter.SPECIFIC_SCHEMA = routine.SPECIFIC_SCHEMA
							where routine.SPECIFIC_NAME = @objectName
							  and routine.SPECIFIC_CATALOG = @dbName
							  and routine.SPECIFIC_SCHEMA = @schema
	
		if (@existingId is null)
		begin
	
			INSERT INTO orm.[SprocDalMonitor] (CatalogName, schemaname, routinename, RoutineType,ReturnType, IsDeleted, LastUpdateByHostName, ParametersXml, ParameterCount)
			SELECT SPECIFIC_CATALOG, SPECIFIC_SCHEMA, SPECIFIC_NAME, ROUTINE_TYPE, DATA_TYPE, @isDeleted, HOST_NAME(), @parameterXml, @parameterCount
			FROM INFORMATION_SCHEMA.ROUTINES r
			WHERE r.SPECIFIC_CATALOG = @dbName
			  AND r.SPECIFIC_SCHEMA = @schema
			  AND r.SPECIFIC_NAME = @objectName
	
		end
		else
		begin
	
			update orm.[SprocDalMonitor]
				set  LastUpdateDateUtc = getutcdate()
					,LastUpdateByHostName = HOST_NAME()
					,RoutineType = @objectType
					,ReturnType = IsNull(r.DATA_TYPE, ReturnType)
					,IsDeleted = @isDeleted
					,ParametersXml = @parameterXml
					,ParameterCount = @parameterCount
			from orm.[SprocDalMonitor] m (nolock)
				left join INFORMATION_SCHEMA.ROUTINES r on r.SPECIFIC_CATALOG = @dbName
										AND r.SPECIFIC_SCHEMA = @schema
										AND r.SPECIFIC_NAME = @objectName
			where m.ID = @existingId
		
		end

		-- handle schema transfers
		--if (@eventType = ''ALTER_SCHEMA'')
		begin
			-- ''delete'' any entries that no longer exist
			update s
				set s.IsDeleted = 1
			from orm.SprocDalMonitor s 
				left join INFORMATION_SCHEMA.ROUTINES r on r.SPECIFIC_SCHEMA = s.SchemaName and r.SPECIFIC_NAME = s.RoutineName
			where r.SPECIFIC_NAME is null
			  and s.IsDeleted = 0
		end

		exec [orm].[SprocGenExtractAndStoreJsonMetadata] @catalog = @dbName, @schema = @schema, @routine = @objectName
	
	END

	';
	exec sp_executesql @trig

END

if (not exists (select top 1 1 from sys.procedures where name ='Uninstall' and SCHEMA_NAME(schema_Id) = 'orm'))
begin
	EXEC('
	create proc orm.Uninstall
	as
	begin
	
		if (exists (select top 1 1 from sys.triggers where name = ''SprocDalMonitor'' and parent_class_desc = ''DATABASE'')) drop trigger SprocDalMonitor on database
	
		if (exists (select top 1 1 from sys.procedures where name =''SprocGenExtractAndStoreJsonMetadata'' and SCHEMA_NAME(schema_Id) = ''orm'')) drop proc orm.SprocGenExtractAndStoreJsonMetadata
		
		if (exists (select top 1 1 from sys.procedures where name =''SprocGenGetParameterList'' and SCHEMA_NAME(schema_Id) = ''orm'')) drop proc orm.SprocGenGetParameterList
		if (exists (select top 1 1 from sys.procedures where name =''SprocGenInitialise'' and SCHEMA_NAME(schema_Id) = ''orm'')) drop proc orm.SprocGenInitialise
		if (exists (select top 1 1 from sys.procedures where name =''SprocGenGetRoutineList'' and SCHEMA_NAME(schema_Id) = ''orm'')) drop proc orm.SprocGenGetRoutineList
		if (exists (select top 1 1 from sys.procedures where name =''SprocGenGetRoutineListCnt'' and SCHEMA_NAME(schema_Id) = ''orm'')) drop proc orm.SprocGenGetRoutineListCnt

		if (exists (select top 1 1 from sys.tables where name =''SprocDalMonitorAdditional'' and SCHEMA_NAME(schema_id) = ''orm'')) drop table orm.SprocDalMonitorAdditional

		if (exists (select 1 from sys.indexes where name = ''IX_SprocDalMonitor_Rowver'' and OBJECT_NAME(object_id) = ''SprocDalMonitor'')) DROP INDEX [IX_SprocDalMonitor_Rowver] ON [orm].[SprocDalMonitor]
		if (exists (select 1 from sys.indexes where name = ''IX_SprocDalMonitor_CatSchName'' and OBJECT_NAME(object_id) = ''SprocDalMonitor'')) DROP INDEX [IX_SprocDalMonitor_CatSchName] ON [orm].[SprocDalMonitor]

		if (exists (select top 1 1 from sys.tables where name =''SprocDalMonitor'' and SCHEMA_NAME(schema_id) = ''orm'')) drop table orm.SprocDalMonitor
	

		drop proc orm.Uninstall
		if (exists (select top 1 1 from sys.schemas where name = ''orm'')) DROP SCHEMA orm
	end
	')
end

exec orm.SprocGenInitialise