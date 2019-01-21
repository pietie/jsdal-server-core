IF (SCHEMA_ID('ormv2') IS NULL)
	EXEC ('CREATE SCHEMA ormv2')
GO

IF (OBJECT_ID('ormv2.RoutineMeta') IS NULL)
BEGIN
	CREATE TABLE [ormv2].[RoutineMeta](
		[Id] [int] IDENTITY(1,1) NOT NULL,
		[CreateDateUtc] [datetime] NOT NULL,
		[LastUpdateDateUtc] [datetime] NULL,
		[CatalogName] [nvarchar](128) NOT NULL,
		[SchemaName] [nvarchar](128) NOT NULL,
		[RoutineName] [nvarchar](128) NOT NULL,
		[RoutineType] [nvarchar](20) NOT NULL,
		[ReturnType] [nvarchar](20) NULL,
		[rowver] [timestamp] NOT NULL,
		[IsDeleted] [bit] NOT NULL,
		[LastUpdateByHostName] [nvarchar](128) NULL,
		[ParametersXml] [varchar](max) NULL,
		[JsonMetadata] [varchar](max) NULL
	 CONSTRAINT [PK_RoutineMeta] PRIMARY KEY CLUSTERED 
	(
		[Id] ASC
	)WITH (PAD_INDEX  = OFF, STATISTICS_NORECOMPUTE  = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS  = ON, ALLOW_PAGE_LOCKS  = ON, FILLFACTOR = 100) ON [PRIMARY]
	) ON [PRIMARY]


	ALTER TABLE [ormv2].[RoutineMeta] ADD  CONSTRAINT [DF_RoutineMeta_CreateDateUtc]  DEFAULT (getutcdate()) FOR [CreateDateUtc]
	ALTER TABLE [ormv2].[RoutineMeta] ADD  CONSTRAINT [DF_RoutineMeta_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
	
	CREATE NONCLUSTERED INDEX [IX_RoutineMeta_Rowver] ON [ormv2].[RoutineMeta] ([rowver] ASC)
	CREATE NONCLUSTERED INDEX [IX_RoutineMeta_CatSchName] ON [ormv2].[RoutineMeta] ([CatalogName] ASC, [SchemaName] ASC, [RoutineName] ASC) 
END
GO

IF (OBJECT_ID('ormv2.RoutineParameterDefaults') IS NULL) 
	EXEC ('CREATE FUNCTION ormv2.RoutineParameterDefaults() RETURNS @ret TABLE (ParmId smallint, Name varchar(128), DefVal varchar(8000)) as BEGIN RETURN END')
GO

ALTER FUNCTION [ormv2].[RoutineParameterDefaults]
(
	@procId INT
)
RETURNS @ret TABLE (ParmId smallint, Name varchar(128), DefVal varchar(8000))
AS
BEGIN

	DECLARE @routineSource varchar(max)
	
	DECLARE @parmLookup TABLE (ParameterId SMALLINT, Name varchar(128), DataType varchar(128)) 
	
	insert into @parmLookup
	select parameter_id, name, CONCAT(type_name(user_type_id), case
												when type_name(user_type_id) in ('varchar','nvarchar', 'char', 'nchar', 'varbinary') then
														case max_length
															when -1 then '(max)'
															else concat('(',max_length,')')
														end
												when type_name(user_type_id) in ('decimal') then concat('(',precision,',',scale,')')
											 end) DataType
				from sys.parameters where object_id = @procId 
		order by parameter_id
		 
	select @routineSource = object_definition(@procId)


	DECLARE @i int	-- current ix in buffer
			,@i2 int
			,@len int
			,@ch char(1)
			,@nextCh char(1)

			,@unknownTokenStartIx INT
			,@unknownTokenComplete varchar(max)
			,@unknownToken varchar(max)
			,@unknownBank varchar(max) =''

			,@curParameterId SMALLINT = 0
			,@maxParameterId SMALLINT 
			,@curParameterName varchar(128) 
			,@curParameterDataType varchar(128)
			,@parmState SMALLINT = 0 -- 0: not started, 1: found @, 2: found 'as', 3: found datatype, 4: found equals sign

			,@defBuf varchar(max) -- current default value buffer


	select @maxParameterId = max(ParameterId) from @parmLookup

	set @i = 1
	set @len = len(@routineSource) + 1
 
	while (@i < @len)
	begin
		set @ch = substring(@routineSource,@i,1)
		set @nextCh = substring(@routineSource,@i+1,1)


		-- SINGLE LINE comment
			if (@ch = '-' and @nextCh = '-')
			begin

				set @i2 = @i
				set @i += 2 -- skip over second dash
				
				-- look for end of SL comment
				while (@i < @len)
				begin
					if (SUBSTRING(@routineSource, @i-2, 1) = char(10)) begin set @i -= 2; break; end
					if (SUBSTRING(@routineSource, @i-1, 1) = char(10)) begin set @i -= 1; break; end
					if (SUBSTRING(@routineSource, @i, 1) = char(10)) begin break; end
					set @i += 3
				end
			
				set @unknownBank = IsNull(@unknownBank,'') + IsNull(@unknownToken, '')
				set @unknownTokenStartIx  = null

				--set @i += 2 
				set @i += 1
				CONTINUE
			end

		--  MULTI LINE comment
			else if (@ch = '/' and @nextCh = '*')
			begin

				set @i2 = @i
				set @i += 2 -- skip over *
				
				-- look for end of ML comment
				while (@i < @len)
				begin
					if (SUBSTRING(@routineSource, @i, 2) = '*/') BREAK; 
					
					set @i += 1
				end					
			
				set @i += 1 -- move over * to /

				set @unknownBank = IsNull(@unknownBank,'') + IsNull(@unknownToken, '')
				set @unknownTokenStartIx  = null
				set @i += 1
				CONTINUE
			end
		-- STRING
			else if (@ch = '''')
			begin
				set @i2 = @i
				set @i += 1
				
				-- look for end of STRING
				while (@i < @len)
				begin
					-- look for escaped string
					if (SUBSTRING(@routineSource, @i, 2) = '''''') 
					BEGIN 
						set @i += 2
						CONTINUE
					END
					else if (SUBSTRING(@routineSource, @i, 1) = '''') 
					begin
						break
					end
					
					set @i += 1
				end							
				
				if (@parmState = 4/*EQ*/)
				begin
					set @parmState = 100 -- found what we are looking for
					set @defBuf = SUBSTRING(@routineSource, @i2, @i - @i2 + 1)
				end
			
				set @unknownTokenStartIx  = null

				set @i += 1
				CONTINUE
			end
			else if (@ch = '@' OR @parmState = 200)
			begin
				if (@parmState > 0)
				begin
					-- finding another @ means we are done with the previous one
					if (@parmState >= 4/*FOUND EQ*/)
					begin
						set @defBuf = RTRIM(@defBuf)
						
						-- remove comma at end
						if (RIGHT(@defBuf,1) = ',') set @defBuf = SUBSTRING(@defBuf,1, LEN(@defBuf)-1)
						-- remove OUT at end
						if (RIGHT(@defBuf,3) = 'out') set @defBuf = SUBSTRING(@defBuf,1, LEN(@defBuf)-3)

						update @ret
							set DefVal = @defBuf
						where ParmId = @curParameterId
					end
				 
				end
				
				set @curParameterId += 1

				if (@curParameterId > @maxParameterId)
				begin
					-- we've covered all parameters!
					break
				end

				select @curParameterName = Name, @curParameterDataType = DataType from @parmLookup  where ParameterId = @curParameterId

				insert @ret (ParmId, Name) values (@curParameterId, @curParameterName)
			 				
				set @i += len(@curParameterName)
			
				set @parmState = 1/*Found @*/
				set @defBuf = null
				set @unknownTokenStartIx = null
				set @unknownToken = null
				set @unknownBank = null
			end
			else 
			begin
				-- skip over some whitespace chars
				if (@parmState > 0 and @ch in (char(9), char(10), char(13)))
				begin
					set @i += 1
					CONTINUE
				end

				if (@unknownTokenStartIx is null) set @unknownTokenStartIx = @i

				set @unknownToken = SUBSTRING(@routineSource, @unknownTokenStartIx, @i - @unknownTokenStartIx + 1)
				set @unknownTokenComplete = CONCAT(@unknownBank, @unknownToken)

				if (@parmState = 1/*Found @*/ AND @unknownTokenComplete = 'AS')
				begin		
					set @unknownTokenStartIx = null
					set @unknownToken = null
					set @unknownBank = null
					set @parmState = 2/*found AS*/
					set @i += 1
					CONTINUE

				end
				else if (@parmState in (1/*@*/, 2/*AS*/) AND @unknownTokenComplete = @curParameterDataType)
				begin
					set @unknownTokenStartIx = null
					set @unknownToken = null
					set @unknownBank = null
					set @parmState = 3/*found DATATYPE*/
					set @i += 1
					CONTINUE
				end
				else if (@parmState = 3/*DT*/ AND LTRIM(@unknownTokenComplete) = '=')
				begin
					set @unknownTokenStartIx = null
					set @unknownToken = null
					set @unknownBank = null
					set @parmState = 4/*found EQ*/
					set @i += 1
					set @defBuf = ''
					CONTINUE
				end
				else if (@parmState = 4/*EQ*/)
				begin
					set @defBuf += @ch

					if (@curParameterId = @maxParameterId)
					begin
						-- look for breaks/end of declaration on last parameter
						if (RIGHT(@defBuf, 1) = ')')
						begin
							set @defBuf = LEFT(@defBuf, LEN(@defBuf)-1)
							-- we've reached the end
							set @parmState = 200
						end
						else if (RIGHT(@defBuf, 2) = 'as')
						begin
							set @defBuf = LEFT(@defBuf, LEN(@defBuf)-2)
							-- we've reached the end
							set @parmState = 200
						end
						else if (RIGHT(@defBuf, 3) = 'OUT')
						begin
							-- we've reached the end
							set @parmState = 200
						end
					end

				end
		
		end -- main else after '@' check
		
		set @i += 1
	end
	
	RETURN
END
GO

IF OBJECT_ID('ormv2.JsonMetadata') is null
	EXEC ('CREATE FUNCTION ormv2.JsonMetadata(@procID INT) RETURNS VARCHAR(MAX) AS BEGIN RETURN null END')
GO

ALTER function ormv2.JsonMetadata
(
	@procId INT
)
RETURNS varchar(max)
as
BEGIN

	DECLARE @routineSource varchar(max)
	 
	select @routineSource = object_definition(@procId)

	-- quick like check upfront to bail early speeds up the whole function greatly
	if (@routineSource not like '%jsDAL%:%{%') return null	

	DECLARE @i INT	-- current ix in buffer
			,@startIx INT
			,@i2 INT
			,@ch CHAR(1)
			,@nextCh CHAR(1)
			,@state INT = 0-- 0: Blank, 1: In SL_COMMENT, 2: In ML_COMMENT
			,@openBraceCnt INT = -1
			,@braceStartIx INT
			,@fragment varchar(max)
			
	set @i = 1
 
	while (@i < len(@routineSource) + 1)
	begin
		set @ch = substring(@routineSource,@i,1)
		set @nextCh = substring(@routineSource,@i+1,1)

		-- SINGLE LINE comment START
			if (@state = 0  AND @ch = '-' AND @nextCh = '-')
			begin
				set @startIx = @i
				set @state = 1 -- IN SL_COMMENT
			end
			else if (@state = 1/*IN SL_COMMENT*/ AND @ch = char(10))
			begin
				set @startIx = null
				set @state = 0
			end
		--  MULTI LINE comment START
			else if (@state = 0 AND @ch = '/' AND @nextCh = '*')
			begin
				set @startIx = @i
				set @state = 2 -- IN ML_COMMENT
			end
			else if (@state = 2/*IN ML_COMMENT*/ and @ch = '*' and @nextCh = '/')
			begin
				set @startIx = null
				set @state = 0
			end
		-- STRING -- we have to handle strings so that we don't pickup something like '/*jsDAL: {...}*/' from inside a string
			else if (@state = 0 AND @ch = '''')
			begin
				set @i2 = @i
				set @i += 1
				
				-- look for end of STRING
				while (@i < len(@routineSource) + 1)
				begin
					-- look for escaped string
					if (SUBSTRING(@routineSource, @i, 2) = '''''') 
					BEGIN 
						set @i += 2
						CONTINUE
					END
					else if (SUBSTRING(@routineSource, @i, 1) = '''') 
					begin
						break
					end
					
					set @i += 1
				end							

				set @i += 1
				CONTINUE
			end
			else if (@state in (1,2))
			begin

				if (@ch = '{')
				begin
					IF (@openBraceCnt = -1) 
					BEGIN
						set @openBraceCnt = 1
						set @braceStartIx = @i
					END
					ELSE 
					BEGIN
						set @openBraceCnt += 1
					END
				end
				else if (@ch = '}')
				begin

					if (@openBraceCnt > 0)
					begin
						set @openBraceCnt -= 1
						if (@openBraceCnt = 0)
						begin
							set @fragment  = SUBSTRING(@routineSource, @braceStartIx, @i - @braceStartIx + 1)

							-- if it looks 'good enough'
							if (@fragment like '%jsDAL%:%{%')
							begin
								return @fragment
								break
							end
							
							set @openBraceCnt = -1
						end
					end
				end
			end
					  

		set @i += 1
	end -- main while

	return null
END
GO


IF OBJECT_ID('ormv2.Init') is null
	EXEC ('CREATE PROCEDURE ormv2.Init AS RETURN 0')
GO

ALTER PROCEDURE [ormv2].[Init]
AS
BEGIN

-----------------------------------------------------------
-- Create lookup table for each parameter's default value
-----------------------------------------------------------
	CREATE TABLE #Lookup(object_id INT, ParmId SMALLINT, DefVal varchar(8000))

	insert into #Lookup
	select o.object_id, p.ParmId, p.DefVal from sys.objects o 
				cross apply ormv2.RoutineParameterDefaults(o.object_id) p
	where o.type IN ('P'/*SPROC*/, 'FN'/*UDF*/, 'TF'/*Table-valued function*/, 'IF'/*inline table function*/ /*, 'AF'aggregate function CLR*/ /*, 'FT'CLR table valued*//*, 'PC' CLR SPROC*//*, 'IS' DUNNO!*//*, 'FS' CLR SCALAR*/)
		and not exists (select 1/0 
							from ormv2.RoutineMeta dst with(nolock) 
						where dst.RoutineName = o.name and dst.SchemaName = SCHEMA_NAME(o.schema_id) and dst.CatalogName = DB_NAME())

	CREATE NONCLUSTERED INDEX [tmplookup_ix_object_id] ON #Lookup ([object_id] ASC)
-------------------------

	TRUNCATE TABLE ormv2.RoutineMeta

	INSERT INTO ormv2.RoutineMeta (CatalogName, SchemaName, RoutineName, RoutineType,ReturnType, IsDeleted, LastUpdateByHostName, ParametersXml, JsonMetadata)
	SELECT DB_NAME() [Catalog]
			,SCHEMA_NAME(o.schema_id) [Schema]
			,o.name RoutineName
			,convert(nvarchar(20), CASE
				WHEN o.type IN ('P','PC')
				THEN 'PROCEDURE'
				ELSE 'FUNCTION' END) RoutineType
			,convert(sysname, CASE
								WHEN o.type IN ('TF', 'IF', 'FT') THEN N'TABLE'
								ELSE ISNULL(TYPE_NAME(c.system_type_id),TYPE_NAME(c.user_type_id)) 
							END) ReturnType
			,0 IsDeleted
			,HOST_NAME()
			,(
				select p.Name [@Name]
						,p.is_output [@IsOutput]
						,p.max_length [@Max]
						,p.precision [@Precision]
						,p.scale [@Scale]
						,TYPE_NAME(p.user_type_id) [@Type]
						,l.DefVal [@DefVal]
						,CASE WHEN p.parameter_id = 0 THEN 1 ELSE null END [@Result]
					from sys.parameters p 
						left join #Lookup l on l.object_id = o.object_id and l.ParmId = p.parameter_id
					where p.object_id = o.object_id
				for xml path('Parm'), elements
		)  ParametersXml

		,ormv2.JsonMetadata(o.object_id)
		from sys.objects o 
			LEFT JOIN sys.parameters c ON (c.object_id = o.object_id AND c.parameter_id = 0)
	where o.type IN ('P'/*SPROC*/, 'FN'/*UDF*/, 'TF'/*Table-valued function*/, 'IF'/*inline table function*/ /*, 'AF'aggregate function CLR*/ /*, 'FT'CLR table valued*//*, 'PC' CLR SPROC*//*, 'IS' DUNNO!*//*, 'FS' CLR SCALAR*/)
		and not exists (select 1/0 
							from ormv2.RoutineMeta dst with(nolock) 
						where dst.RoutineName = o.name and dst.SchemaName = SCHEMA_NAME(o.schema_id) and dst.CatalogName = DB_NAME())

 

		IF (exists(select 1/0 from sys.triggers where Name = 'DB_Trigger_DALMonitor' and parent_class_desc = 'DATABASE')) EXEC ('DROP TRIGGER DB_Trigger_DALMonitor ON DATABASE')

		EXEC ('CREATE TRIGGER [DB_Trigger_DALMonitor] ON DATABASE 
			FOR CREATE_PROCEDURE, ALTER_PROCEDURE, CREATE_FUNCTION, ALTER_FUNCTION, DROP_PROCEDURE, DROP_FUNCTION,  ALTER_SCHEMA, RENAME
		AS 
		BEGIN
			SET NOCOUNT ON
			
			DECLARE @eventType NVARCHAR(128)
					,@dbName NVARCHAR(128)
					,@schema NVARCHAR(128)
					,@objectName NVARCHAR(128)
					,@objectType NVARCHAR(128)
			
			SELECT @eventType = EVENTDATA().value(''(/EVENT_INSTANCE/EventType)[1]'',''nvarchar(128)'')
					,@dbName = EVENTDATA().value(''(/EVENT_INSTANCE/DatabaseName)[1]'',''nvarchar(128)'')
					,@schema = EVENTDATA().value(''(/EVENT_INSTANCE/SchemaName)[1]'',''nvarchar(128)'')
					,@objectName = EVENTDATA().value(''(/EVENT_INSTANCE/ObjectName)[1]'',''nvarchar(128)'')
					,@objectType = EVENTDATA().value(''(/EVENT_INSTANCE/ObjectType)[1]'',''nvarchar(128)'')

			if (@eventType = ''RENAME'')
			begin
				set @objectName = EVENTDATA().value(''(/EVENT_INSTANCE/NewObjectName)[1]'',''nvarchar(128)'')
			end
			
			DECLARE @existingId INT
					,@isDeleted BIT 
					
			set @isDeleted = case LEFT(@eventType,5) when ''DROP_'' then 1 else 0 end

			-- look for an existing entry
			select @existingId = m.Id 
				from ormv2.[RoutineMeta] m 
			where m.CatalogName = @dbName
				and m.SchemaName = @schema
				and m.RoutineName = @objectName
			
			if (@existingId is null)
			begin
				INSERT INTO ormv2.RoutineMeta (CatalogName, SchemaName, RoutineName, RoutineType,ReturnType, IsDeleted, LastUpdateByHostName, ParametersXml, JsonMetadata)
					select DB_NAME() [Catalog]
							,SCHEMA_NAME(o.schema_id) [Schema]
							,o.name RoutineName
							,convert(nvarchar(20), CASE
								WHEN o.type IN (''P'',''PC'')
								THEN ''PROCEDURE''
								ELSE ''FUNCTION'' END) RoutineType
							,convert(sysname, CASE WHEN o.type IN (''TF'', ''IF'', ''FT'') THEN N''TABLE'' ELSE ISNULL(TYPE_NAME(c.system_type_id),TYPE_NAME(c.user_type_id))  END) ReturnType
							,0 IsDeleted
							,HOST_NAME()
							,(
								select x.Name [@Name]
									,p.is_output [@IsOutput]
									,p.max_length [@Max]
									,p.precision [@Precision]
									,p.scale [@Scale]
									,TYPE_NAME(p.user_type_id) [@Type]
									,x.DefVal [@DefVal]
									,CASE WHEN p.parameter_id = 0 THEN 1 ELSE null END [@Result]
								from sys.parameters p 
									left join ormv2.RoutineParameterDefaults(o.object_id) x  on x.ParmId = p.parameter_id 
								where p.object_id = o.object_id
							for xml path(''Parm''), elements
						)  ParametersXml
						,ormv2.JsonMetadata(o.object_id)
						from sys.objects o 
							LEFT JOIN sys.parameters c ON (c.object_id = o.object_id AND c.parameter_id = 0)
					where o.type IN (''P''/*SPROC*/, ''FN''/*UDF*/, ''TF''/*Table-valued function*/, ''IF''/*inline table function*/ /*, ''AF''aggregate function CLR*/ /*, ''FT''CLR table valued*//*, ''PC'' CLR SPROC*//*, ''IS'' DUNNO!*//*, ''FS'' CLR SCALAR*/)
					and DB_NAME() = @dbName
					and o.schema_id = SCHEMA_ID(@schema)
					and o.name = @objectName
					
			
			end
			else
			begin

				update ormv2.[RoutineMeta]
					set  LastUpdateDateUtc = getutcdate()
						,LastUpdateByHostName = HOST_NAME()
						,RoutineType = convert(nvarchar(20), CASE WHEN o.type IN (''P'',''PC'') THEN ''PROCEDURE'' ELSE ''FUNCTION'' END) 
						,ReturnType = convert(sysname, CASE WHEN o.type IN (''TF'', ''IF'', ''FT'') THEN N''TABLE'' ELSE ISNULL(TYPE_NAME(c.system_type_id),TYPE_NAME(c.user_type_id))  END)
						,IsDeleted = @isDeleted
						,ParametersXml = (
							select x.Name [@Name]
									,p.is_output [@IsOutput]
									,p.max_length [@Max]
									,p.precision [@Precision]
									,p.scale [@Scale]
									,TYPE_NAME(p.user_type_id) [@Type]
									,x.DefVal [@DefVal]
									,CASE WHEN p.parameter_id = 0 THEN 1 ELSE null END [@Result]
								from sys.parameters p 
									left join ormv2.RoutineParameterDefaults(o.object_id) x  on x.ParmId = p.parameter_id 
								where p.object_id = o.object_id
							for xml path(''Parm''), elements
						)
						,JsonMetadata = ormv2.JsonMetadata(o.object_id)
				from ormv2.[RoutineMeta] m (nolock)
					left join sys.objects o  on DB_NAME() = m.CatalogName and o.schema_id = SCHEMA_ID(m.SchemaName) and o.name = m.RoutineName
					LEFT JOIN sys.parameters c ON (c.object_id = o.object_id AND c.parameter_id = 0)
				where m.ID = @existingId

			end
			
			begin
				-- ''delete'' any entries that no longer exist
				update s
					set s.IsDeleted = 1
				from ormv2.RoutineMeta s 
					left join sys.objects o on DB_NAME() = s.CatalogName and o.schema_id = SCHEMA_ID(s.SchemaName) and o.name = s.RoutineName
				where o.object_id is null
					and s.IsDeleted = 0
			end

		END') 
	
END

GO

IF OBJECT_ID('ormv2.GetRoutineList') is null
	EXEC ('CREATE PROCEDURE ormv2.GetRoutineList AS RETURN 0')
GO


ALTER PROCEDURE [ormv2].[GetRoutineList]
(
	@maxRowver		timestamp = 0
)
as
begin

	select mon.Id
			,mon.CatalogName
			,mon.SchemaName
			,mon.RoutineName
			,case 
				when UPPER(mon.RoutineType) = 'FUNCTION' AND UPPER(mon.ReturnType) = 'TABLE' then 'TVF'
				else mon.RoutineType 
			end  RoutineType
			,cast(mon.rowver as bigint) rowver
			,mon.IsDeleted
			,mon.ParametersXml
			,object_id(QUOTENAME(mon.CatalogName) + '.' + QUOTENAME(mon.SchemaName) + '.' + QUOTENAME(mon.RoutineName)) ObjectId
			,mon.JsonMetadata
			,mon.LastUpdateByHostName
		from ormv2.[RoutineMeta] mon with (nolock)
	where mon.rowver > @maxRowver
	order by rowver
END
GO

IF OBJECT_ID('ormv2.GetRoutineListCnt') is null
	EXEC ('CREATE PROCEDURE ormv2.GetRoutineListCnt AS RETURN 0')
GO

ALTER PROCEDURE [ormv2].[GetRoutineListCnt]
(
	@maxRowver		timestamp = 0
)
AS
BEGIN
	select count(1) CNT from ormv2.[RoutineMeta] mon where mon.rowver > @maxRowver
END
GO
	
IF OBJECT_ID('ormv2.Uninstall') IS NULL
	EXEC ('CREATE PROCEDURE ormv2.Uninstall AS RETURN 0')
GO

ALTER PROCEDURE  ormv2.Uninstall
AS
BEGIN
	
	IF (exists(select 1/0 from sys.triggers where Name = 'DB_Trigger_DALMonitor' and parent_class_desc = 'DATABASE')) DROP TRIGGER DB_Trigger_DALMonitor on database

	IF (OBJECT_ID('ormv2.JsonMetadata') IS NOT NULL) DROP FUNCTION ormv2.JsonMetadata
			
	IF (OBJECT_ID('ormv2.Init') IS NOT NULL) DROP PROCEDURE ormv2.Init
	IF (OBJECT_ID('ormv2.GetRoutineList') IS NOT NULL) DROP PROCEDURE ormv2.GetRoutineList
	IF (OBJECT_ID('ormv2.GetRoutineListCnt') IS NOT NULL) DROP PROCEDURE ormv2.GetRoutineListCnt

	if (exists (select 1/0 from sys.indexes where name = 'IX_RoutineMeta_Rowver' and object_id = OBJECT_ID('ormv2.RoutineMeta'))) DROP INDEX [IX_RoutineMeta_Rowver] ON [ormv2].[RoutineMeta]
	if (exists (select 1/0 from sys.indexes where name = 'IX_RoutineMeta_CatSchName' and object_id = OBJECT_ID('ormv2.RoutineMeta'))) DROP INDEX [IX_RoutineMeta_CatSchName] ON [ormv2].[RoutineMeta]

	IF (OBJECT_ID('ormv2.RoutineParameterDefaults') IS NOT NULL) DROP FUNCTION ormv2.RoutineParameterDefaults
	IF (OBJECT_ID('ormv2.RoutineMeta') IS NOT NULL) DROP TABLE ormv2.RoutineMeta
	
	
	DROP PROCEDURE ormv2.Uninstall
	IF (SCHEMA_ID('ormv2') IS NOT NULL) DROP SCHEMA ormv2
END
GO

--EXEC ormv2.Init