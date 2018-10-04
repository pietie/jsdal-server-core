if (not exists (select 1/0 from sys.schemas where name = 'ormv2')) select @err = IsNull(@err + char(10), '') + 'Schema ''ormv2'' does not exist.'
if (not exists (select 1/0 from sys.tables where name ='RoutineMeta' and SCHEMA_NAME(schema_id) = 'ormv2')) select @err = IsNull(@err + char(10), '') + 'Table ''ormv2.RoutineMeta'' does not exist.'
if (not exists (select 1/0 from sys.triggers where name = 'DB_Trigger_DALMonitor' and parent_class_desc = 'DATABASE')) select @err = IsNull(@err + char(10), '') + 'Database trigger ''DB_Trigger_DALMonitor'' does not exist.'
if (not exists (select 1/0 from sys.procedures where name ='Init' and SCHEMA_NAME(schema_Id) = 'ormv2')) select @err = IsNull(@err + char(10), '') + 'Sproc ''Init'' does not exist.'
if (not exists (select 1/0 from sys.procedures where name ='GetRoutineList' and SCHEMA_NAME(schema_Id) = 'ormv2')) select @err = IsNull(@err + char(10), '') + 'Sproc ''GetRoutineList'' does not exist.'
if (not exists (select 1/0 from sys.procedures where name ='GetRoutineListCnt' and SCHEMA_NAME(schema_Id) = 'ormv2')) select @err = IsNull(@err + char(10), '') + 'Sproc ''GetRoutineListCnt'' does not exist.'
if (not exists (select 1/0 from sys.procedures where name ='JsonMetadata' and SCHEMA_NAME(schema_Id) = 'ormv2')) select @err = IsNull(@err + char(10), '') + 'Sproc ''JsonMetadata'' does not exist.'
if (not exists (select 1/0 from sys.objects where name ='RoutineParameterDefaults' and SCHEMA_NAME(schema_id) = 'ormv2' and type='TF')) select @err = IsNull(@err + char(10), '') + 'TVF ''RoutineParameterDefaults'' does not exist.'

