using System.Collections.Generic;
using jsdal_server_core.Settings.ObjectModel;
using System.Data;
using System;
using System.Data.Common;
using System.Reflection;
using System.Text;
using System.Linq;
using jsdal_plugin;
using jsdal_server_core.Performance;
using Endpoint = jsdal_server_core.Settings.ObjectModel.Endpoint;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Types;
using System.Threading;

namespace jsdal_server_core
{
    public static class OrmDAL
    {
        public static async Task<int> GetRoutineListCntAsync(SqlConnection con, long? maxRowDate)
        {
            using (var cmd = new SqlCommand())
            {

                cmd.Connection = con;
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.CommandTimeout = 30;
                cmd.CommandText = string.Intern("ormv2.GetRoutineListCnt");
                cmd.Parameters.Add("maxRowver", System.Data.SqlDbType.BigInt).Value = maxRowDate ?? 0;

                var scalar = await cmd.ExecuteScalarAsync();

                return (int)scalar;
            }
        }

        // ripped from .NET Framework SqlCommandBuilder
        private static string BuildQuotedString(string unQuotedString)
        {
            string quotePrefix = "[";
            string quoteSuffix = "]";
            StringBuilder stringBuilder = new StringBuilder();

            stringBuilder.Append(quotePrefix);

            stringBuilder.Append(unQuotedString.Replace(quoteSuffix, quoteSuffix + quoteSuffix));
            stringBuilder.Append(quoteSuffix);

            return stringBuilder.ToString();
        }

        private static string GetBrackettedName(string schema, string name)
        {
            if (string.IsNullOrEmpty(schema)) return BuildQuotedString(name);
            else return BuildQuotedString(schema) + "." + BuildQuotedString(name);
        }

        // from sqlmetal
        private static DbParameter CreateParameter(DbCommand command, string name, SqlDbType dbType)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            PropertyInfo property = parameter.GetType().GetProperty("SqlDbType");
            if (property != (PropertyInfo)null)
                property.SetValue((object)parameter, (object)dbType, (object[])null);
            return parameter;
        }

        public static async Task<(DataSet, string/*error*/)> RoutineGetFmtOnlyResultsAsync(string connectionString, string schema, string routine, List<RoutineParameterV2> parameterList)
        {
            string error = null;
            // we have to open a new connection as there is an open Reader across the main connection
            using (var con = new SqlConnection(connectionString))
            {
                await con.OpenAsync();

                var ds = new DataSet();

                var dbCmd = con.CreateCommand();

                dbCmd.CommandType = CommandType.StoredProcedure;
                dbCmd.CommandText = GetBrackettedName(schema, routine);

                if (parameterList != null)
                {
                    foreach (var p in parameterList)
                    {
                        if (p.IsResult) continue;

                        (var dbType, _) = Controllers.ExecController.GetSqlDbTypeFromParameterType(p.SqlDataType);
                        var parm = CreateParameter(dbCmd, p.Name, dbType);
                        dbCmd.Parameters.Add(parm);
                    }
                }

                try
                {
                    using (var reader = await dbCmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly))
                    {
                        var schemaTable = await reader.GetSchemaTableAsync();

                        while (schemaTable != null)
                        {
                            schemaTable.TableName = "Table" + ds.Tables.Count;
                            ds.Tables.Add(schemaTable);

                            bool b = await reader.NextResultAsync();
                            schemaTable = await reader.GetSchemaTableAsync();
                        }

                    }
                }
                catch (SqlException se)
                {
                    error = se.ToString();
                }
                catch (Exception ex)
                { // soft handle exception as to allow as many results sets to be worked through as possible
                    error = ex.ToString();
                }

                return (ds, error);
            }
        }

        public class ExecutionResult
        {
            public Dictionary<string/*Table0..N*/, ReaderResult> ReaderResults { get; set; }
            public DataSet DataSet { get; set; }
            public object ScalarValue { get; set; }
            public string userError { get; set; }

            // optional stats
            public long? BytesReceived { get; set; }
            public long? NetworkServerTimeInMS { get; set; }

            public int? RowsAffected { get; set; }

            public Dictionary<string, dynamic> OutputParameterDictionary { get; set; }
            public Dictionary<string, string> ResponseHeaders { get; set; }
        }

        // public static ExecutionResult ExecRoutineQuery(
        //        Controllers.ExecController.ExecType type,
        //        string schemaName,
        //        string routineName,
        //        Endpoint endpoint,
        //        Dictionary<string, string> inputParameters,
        //        Dictionary<string, string> requestHeaders,
        //        string remoteIpAddress,
        //        List<ExecutionPlugin> plugins,
        //        int commandTimeOutInSeconds,
        //        out Dictionary<string, dynamic> outputParameterDictionary,
        //        ExecutionBase execRoutineQueryMetric,
        //        ref Dictionary<string, string> responseHeaders,
        //        out int rowsAffected
        //    )

        public static async Task<ExecutionResult> ExecRoutineQueryAsync(
                CancellationToken cancellationToken,
                   Controllers.ExecController.ExecType type,
                   string schemaName,
                   string routineName,
                   Endpoint endpoint,
                   Dictionary<string, string> inputParameters,
                   Dictionary<string, string> requestHeaders,
                   string remoteIpAddress,
                   List<ExecutionPlugin> plugins,
                   int commandTimeOutInSeconds,
                   ExecutionBase execRoutineQueryMetric,
                   Dictionary<string, string> responseHeaders,
                   ExecutionPolicy executionPolicy = null
               )
        {
            SqlConnection con = null;
            SqlCommand cmd = null;
            ExecutionBase execStage = null;

            int rowsAffected = 0;

            try
            {
                var s1 = execRoutineQueryMetric.BeginChildStage(string.Intern("Lookup cached routine"));

                var cachedRoutine = endpoint.CachedRoutines.FirstOrDefault(r => r.Equals(schemaName, routineName));

                if (cachedRoutine == null)
                {
                    // TODO: Return 404 rather?
                    throw new Exception($"The routine [{schemaName}].[{routineName}] was not found.");
                }

                s1.End();

                //
                // jsDAL METADATA
                //
                {
                    var s2 = execRoutineQueryMetric.BeginChildStage("Process metadata");

                    string metaResp = null;

                    try
                    {
                        metaResp = ProcessMetadata(requestHeaders, ref responseHeaders, cachedRoutine);
                    }
                    catch (Exception) { /*ignore metadata failures*/ }

                    if (metaResp != null)
                    {
                        return new ExecutionResult() { userError = metaResp };
                    }

                    s2.End();
                }

                var s3 = execRoutineQueryMetric.BeginChildStage("Open connection");

                var cs = endpoint.GetSqlConnection();

                if (cs == null)
                {
                    throw new Exception($"Execution connection not found on endpoint '{endpoint.Pedigree}'({endpoint.Id}).");
                }

                var csb = new SqlConnectionStringBuilder(cs.ConnectionStringDecrypted);


                // {schemaName}.{routineName} -- including schema.routine will create too many unique connection pools
                csb.ApplicationName = $"jsdal-server EXEC {endpoint.Pedigree}".Left(128);

                con = new SqlConnection(csb.ToString());

                if (endpoint.CaptureConnectionStats)
                {
                    con.StatisticsEnabled = true;
                    con.ResetStatistics();
                }
                else
                {
                    con.StatisticsEnabled = false;
                }

                await con.OpenAsync();

                s3.End();

                //
                // PLUGINS
                //
                var s4 = execRoutineQueryMetric.BeginChildStage("Process plugins");
                ProcessPlugins(plugins, con);
                s4.End();

                var prepareCmdMetric = execRoutineQueryMetric.BeginChildStage("Prepare command");

                //
                // CREATE SQL COMMAND
                //
                cmd = CreateSqlCommand(con, commandTimeOutInSeconds, cachedRoutine, type);

                if (executionPolicy != null)
                {
                    cmd.CommandTimeout = (int)executionPolicy.CommandTimeoutInSeconds;
                }

                //
                // PARAMETERS
                //
                SetupSqlCommandParameters(cmd, cachedRoutine, inputParameters, plugins, remoteIpAddress);

                prepareCmdMetric.End();

                Dictionary<string/*Table0..N*/, ReaderResult> readerResults = null;

                object scalarVal = null;

                int deadlockRetryNum = 0;

            RetryDbCall:

                try
                {
                    if (type == Controllers.ExecController.ExecType.Query)
                    {
                        execStage = execRoutineQueryMetric.BeginChildStage("Execute Query");

                        readerResults = await ProcessExecQueryAsync(cancellationToken, cmd, inputParameters);

                        execStage.End();
                    }
                    else if (type == Controllers.ExecController.ExecType.NonQuery)
                    {
                        execStage = execRoutineQueryMetric.BeginChildStage("Execute NonQuery");

                        rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
                        execStage.End();
                    }
                    else if (type == Controllers.ExecController.ExecType.Scalar)
                    {
                        execStage = execRoutineQueryMetric.BeginChildStage("Execute Scalar");
                        scalarVal = await cmd.ExecuteScalarAsync(cancellationToken);
                        execStage.End();
                    }
                    else
                    {
                        throw new NotSupportedException($"ExecType \"{type.ToString()}\" not supported");
                    }
                }
                catch (SqlException dl) when (dl.Number == 1205/*Deadlock*/ && (executionPolicy?.DeadlockRetry?.Enabled ?? false))
                {
                    deadlockRetryNum++;

                    if (deadlockRetryNum > executionPolicy.DeadlockRetry.MaxRetries)
                    {
                        throw;
                    }
                    else
                    {
                        int delayInSeconds = 3;

                        if (executionPolicy.DeadlockRetry.Type.Equals("Linear", StringComparison.OrdinalIgnoreCase))
                        {
                            delayInSeconds = (int)((executionPolicy.DeadlockRetry.Value * deadlockRetryNum) + 0.5M);
                        }
                        else if (executionPolicy.DeadlockRetry.Type.Equals("Exponential", StringComparison.OrdinalIgnoreCase))
                        {
                            delayInSeconds = (int)((Math.Pow((double)executionPolicy.DeadlockRetry.Value, deadlockRetryNum)) + 0.5);
                        }

                        await Task.Delay(delayInSeconds * 1000);
                        // don't tell anyone
                        goto RetryDbCall;
                    }
                }

                long? bytesReceived = null;
                long? networkServerTimeMS = null;
                // TODO: Consider moving CaptureConnectionStats to Execution Policy
                if (endpoint.CaptureConnectionStats)
                {
                    RetrieveConnectionStatistics(con, out bytesReceived, out networkServerTimeMS);
                }

                var outputParameterDictionary = RetrieveOutputParameterValues(cachedRoutine, cmd);

                return new ExecutionResult()
                {
                    ReaderResults = readerResults,
                    DataSet = null, // deprecated
                    ScalarValue = scalarVal,
                    NetworkServerTimeInMS = networkServerTimeMS,
                    BytesReceived = bytesReceived,
                    RowsAffected = rowsAffected,
                    OutputParameterDictionary = outputParameterDictionary,
                    ResponseHeaders = responseHeaders
                };

            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (SqlException se)
            {
                // TODO: Implement a retry for transactions that fail because of deadlocks
                if (se.Number == 1205/*Deadlock*/)
                {

                }

                string after = "";
                if (execStage != null)
                {
                    execStage.End();

                    if (execStage.DurationInMS.HasValue)
                    {
                        after = $" after {(decimal)execStage.DurationInMS.Value / 1000.0M:0.00} seconds";
                    }
                }

                throw new JsDALExecutionException($"[{schemaName}].[{routineName}] failed on {endpoint?.Pedigree}{after}", se, schemaName, routineName, endpoint.Pedigree, executionPolicy);
            }
            catch (Exception ex)
            {
                string after = "";
                if (execStage != null)
                {
                    execStage.End();

                    if (execStage.DurationInMS.HasValue)
                    {
                        after = $" after {(decimal)execStage.DurationInMS.Value / 1000.0M:0.00} seconds";
                    }
                }
                throw new JsDALExecutionException($"[{schemaName}].[{routineName}] failed on {endpoint?.Pedigree}{after}", ex, schemaName, routineName, endpoint.Pedigree, executionPolicy);
            }
            finally
            {
                cmd?.Dispose();
                con?.Close();
                con?.Dispose();
            }
        } // execRoutine

        private static SqlCommand CreateSqlCommand(SqlConnection con, int commandTimeOutInSeconds, CachedRoutine cachedRoutine, Controllers.ExecController.ExecType type)
        {
            var cmd = new SqlCommand();
            cmd.Connection = con;
            cmd.CommandTimeout = commandTimeOutInSeconds;

            var isTVF = cachedRoutine.Type == "TVF";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = string.Format("[{0}].[{1}]", cachedRoutine.Schema, cachedRoutine.Routine);

            if (isTVF)
            {
                string parmCsvList = string.Join(",", cachedRoutine.Parameters.Where(p => !p.IsResult).Select(p => p.Name).ToArray());

                cmd.CommandType = CommandType.Text;
                cmd.CommandText = string.Format("select * from [{0}].[{1}]({2})", cachedRoutine.Schema, cachedRoutine.Routine, parmCsvList);
            }
            else if (type == Controllers.ExecController.ExecType.Scalar)
            {
                string parmCsvList = string.Join(",", cachedRoutine.Parameters.Where(p => !p.IsResult).Select(p => p.Name).ToArray());

                if (cachedRoutine.Type.Equals(string.Intern("PROCEDURE"), StringComparison.CurrentCultureIgnoreCase))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = string.Format("[{0}].[{1}]", cachedRoutine.Schema, cachedRoutine.Routine);
                }
                else if (cachedRoutine.Type.Equals(string.Intern("FUNCTION"), StringComparison.OrdinalIgnoreCase))
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandText = string.Format("select [{0}].[{1}]({2})", cachedRoutine.Schema, cachedRoutine.Routine, parmCsvList);
                }
            }

            return cmd;
        }

        private static void SetupSqlCommandParameters(SqlCommand cmd, CachedRoutine cachedRoutine, Dictionary<string, string> inputParameters, List<ExecutionPlugin> plugins, string remoteIpAddress)
        {
            /*
                Parameters
                ¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯
                    (Parm not specified)    ->  (Has default in sproc def)  -> Add to command with C# null to inherit default value
                                                (No default)                -> Don't add to command, should result in SQL Exception

                    Parm specified as null  ->  DBNull.Value (regardless of Sproc default value)
                    $jsDAL$DBNull           ->  Add to command with value as DBNull.Value


             */
            // TODO: Possible Parallel.Foreach here?
            cachedRoutine?.Parameters?.ForEach(expectedParm =>
            {
                if (expectedParm.IsResult) return;

                (var sqlType, var udtType) = Controllers.ExecController.GetSqlDbTypeFromParameterType(expectedParm.SqlDataType);
                object parmValue = null;

                var newSqlParm = new SqlParameter(expectedParm.Name, sqlType, expectedParm.MaxLength);

                if (expectedParm.Scale > 0)
                {
                    newSqlParm.Scale = (byte)expectedParm.Scale;
                }

                if (expectedParm.Precision > 0)
                {
                    newSqlParm.Precision = (byte)expectedParm.Precision;
                }
                
                newSqlParm.UdtTypeName = udtType;

                if (!expectedParm.IsOutput)
                {
                    newSqlParm.Direction = ParameterDirection.Input;
                }
                else
                {
                    newSqlParm.Direction = ParameterDirection.InputOutput;
                }

                // trim leading '@'
                var expectedParmName = expectedParm.Name.Substring(1);

                var pluginParamVal = GetParameterValueFromPlugins(cachedRoutine, expectedParmName, plugins);

                var matchingKey = inputParameters.Keys.FirstOrDefault(k=>k.Equals(expectedParmName, StringComparison.OrdinalIgnoreCase));

                // if the expected parameter was defined in the request or if a plugin provided an override
               // if (inputParameters.ContainsKey(expectedParmName) || pluginParamVal != PluginSetParameterValue.DontSet)
               if (matchingKey != null || pluginParamVal != PluginSetParameterValue.DontSet)
                {
                    object val = null;

                    // TODO: Should input parameter if specified be able to override any plugin value?
                    // TODO: For now a plugin value take precendence over an input value - we can perhaps make this a property of the plugin return value (e.g. AllowInputOverride)
                    if (pluginParamVal != PluginSetParameterValue.DontSet)
                    {
                        val = pluginParamVal.Value;
                    }
                    else if (inputParameters.ContainsKey(matchingKey))
                    {
                        val = inputParameters[matchingKey];
                    }

                    // look for special jsDAL Server variables
                    val = jsDALServerVariables.Parse(remoteIpAddress, val);

                    if (val == null || val == DBNull.Value)
                    {
                        parmValue = DBNull.Value;
                    }
                    // TODO: Consider making this 'null' mapping configurable. This is just a nice to have for when the client does not call the API correctly
                    // convert the string value of 'null' to actual DBNull null
                    else if (val.ToString().Trim().ToLower().Equals(Strings.@null))
                    {
                        parmValue = DBNull.Value;
                    }
                    else
                    {
                        parmValue = ConvertParameterValue(expectedParmName, sqlType, val.ToString(), udtType);
                    }

                    newSqlParm.Value = parmValue;

                    cmd.Parameters.Add(newSqlParm);
                }
                else
                {
                    // if (expectedParm.HasDefault && !expectedParm.IsOutput/*SQL does not apply default values to OUT parameters so OUT parameters will always be mandatory to define*/)
                    if (expectedParm.HasDefault || expectedParm.IsOutput)
                    {
                        // TODO: If expectedParm.IsOutput and the expectedParm not specified, refer to Endpoint config on strategy ... either auto specify and make null or let SQL throw

                        // If no explicit value was specified but the parameter has it's own default...
                        // Then DO NOT set newSqlParm.Value so that the DB engine applies the default defined in SQL
                        newSqlParm.Value = null;
                        cmd.Parameters.Add(newSqlParm);
                    }
                    else
                    {
                        // SQL Parameter does not get added to cmd.Parameters and SQL will throw
                    }
                }

            }); // foreach Parameter 

        }

        private static Dictionary<string, dynamic> RetrieveOutputParameterValues(CachedRoutine cachedRoutine, SqlCommand cmd)
        {
            var outputParameterDictionary = new Dictionary<string, dynamic>();

            if (cachedRoutine?.Parameters != null)
            {
                var outputParmList = (from p in cachedRoutine.Parameters
                                      where p.IsOutput && !p.IsResult/*IsResult parameters do not have a name so cannot be accessed in loop below*/
                                      select p).ToList();


                // Retrieve OUT-parameters and their values
                foreach (var outParm in outputParmList)
                {
                    object val = null;

                    val = cmd.Parameters[outParm.Name].Value;

                    if (val == DBNull.Value) val = null;

                    outputParameterDictionary.Add(outParm.Name.TrimStart('@'), val);
                }
            }

            return outputParameterDictionary;
        }

        private static void RetrieveConnectionStatistics(SqlConnection con, out long? bytesReceived, out long? networkServerTimeMS)
        {
            bytesReceived = networkServerTimeMS = null;
            var conStats = con.RetrieveStatistics();

            if (conStats != null)
            {
                if (conStats.Contains("BytesReceived"))
                {
                    bytesReceived = (long)conStats["BytesReceived"];
                }

                if (conStats.Contains("NetworkServerTime"))
                {
                    networkServerTimeMS = (long)conStats["NetworkServerTime"];
                }
            }
        }


        private static Dictionary<int, string[]> ProcssSelectInstruction(Dictionary<string, string> inputParameters)
        {
            if (!inputParameters.ContainsKey("$select")) return null;

            string limitToFieldsCsv = inputParameters["$select"];

            if (string.IsNullOrEmpty(limitToFieldsCsv)) return null;

            var ret = new Dictionary<int, string[]>();

            var listPerTable = limitToFieldsCsv.Split(new char[] { ';' }/*, StringSplitOptions.RemoveEmptyEntries*/);

            for (int tableIx = 0; tableIx < listPerTable.Length; tableIx++)
            {
                var fieldsToKeep = listPerTable[tableIx].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().ToUpper()).ToArray();

                if (fieldsToKeep.Length > 0)
                {
                    ret.Add(tableIx, fieldsToKeep);
                }
            }

            return ret;

        }

        public class ReaderResult
        {
            public DataField[] Fields { get; set; }
            public List<object[]> Data { get; set; }
        }

        private static async Task<Dictionary<string/*Table0..N*/, ReaderResult>> ProcessExecQueryAsync(CancellationToken cancellationToken, SqlCommand cmd, Dictionary<string, string> inputParameters)
        {
            var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            int resultIx = 0;
            var allResultSets = new Dictionary<string, ReaderResult>();

            var limitToFields = ProcssSelectInstruction(inputParameters);

            do
            {
                var readerResult = new ReaderResult();

                allResultSets.Add($"Table{resultIx}", readerResult);

                var fieldTypes = Enumerable.Range(0, reader.FieldCount).Select(reader.GetFieldType).ToArray();

                //?var hasGeoType = fieldTypes.Contains(typeof(SqlGeography)) || fieldTypes.Contains(typeof(SqlGeometry));

                var allFieldNamesUpper = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).Select(n => n.ToUpper()).ToList();

                int fieldCount = reader.FieldCount;

                var fieldMappingIx = new List<int>();

                // if there exists a select list
                if (limitToFields?.ContainsKey(resultIx) ?? false)
                {
                    foreach (var limit in limitToFields[resultIx])
                    {
                        var ix = allFieldNamesUpper.IndexOf(limit);

                        if (ix >= 0) fieldMappingIx.Add(ix);
                    }
                }
                else
                {
                    // default to ALL available fields
                    fieldMappingIx = Enumerable.Range(0, reader.FieldCount).ToList();
                }

                fieldCount = fieldMappingIx.Count;

                readerResult.Fields = Enumerable.Range(0, fieldCount)
                    .Select(i => new DataField(reader.GetName(fieldMappingIx[i]), ToJqxType(reader.GetFieldType(fieldMappingIx[i]))))
                    .ToArray();

                readerResult.Data = new List<object[]>();


                while (await reader.ReadAsync(cancellationToken))
                {
                    // TODO: Cannot use GetValues as SqlGeography types currently throw as .NET Core cannot deserialize it properly. If SqlClient adds support review in future for possible perf benefit - fetch all rows at once instead of one by one. Also just consider the a $select instruction comes through (thus only asking for a subset of the columns)
                    // TODO: We can also possible split it like the below snippet - only take perf hit if we know a geo type is coming up
                    // if (!hasGeoType)
                    // {
                    //     var fullDataRow = new object[reader.FieldCount];// has to be all fields

                    //     // TODO: Handle $select
                    //     // TODO: What about Bytes?

                    //     reader.GetValues(fullDataRow);
                    //     readerResult.Data.Add(fullDataRow);
                    //     continue;
                    // }

                    var data = new object[fieldCount];

                    for (var dstIx = 0; dstIx < fieldCount; dstIx++)
                    {
                        var srcIx = fieldMappingIx[dstIx];
                        var isDBNull = await reader.IsDBNullAsync(srcIx, cancellationToken);

                        if (isDBNull) continue;

                        if (fieldTypes[srcIx] == typeof(byte[]))
                        {
                            var bytes = reader.GetSqlBytes(srcIx).Value;
                            data[dstIx] = Convert.ToBase64String(bytes);
                        }
                        else if (fieldTypes[srcIx] == typeof(SqlGeography))
                        {
                            var geometryReader = new NetTopologySuite.IO.SqlServerBytesReader { IsGeography = true };
                            var bytes = reader.GetSqlBytes(srcIx).Value;
                            var geometry = geometryReader.Read(bytes);

                            var point = geometry as NetTopologySuite.Geometries.Point;

                            if (point != null)
                            {
                                data[dstIx] = new { lat = point.Y, lng = point.X };
                            }
                            else
                            {
                                data[dstIx] = geometry.ToString();
                            }
                        }
                        else
                        {
                            //var x = reader.GetProviderSpecificValue(colIx);
                            data[dstIx] = reader.GetValue(srcIx);
                        }

                    } // for each column

                    readerResult.Data.Add(data);
                }

                resultIx++;
            }
            while (await reader.NextResultAsync(cancellationToken));

            return allResultSets;
        }
        private static PluginSetParameterValue GetParameterValueFromPlugins(CachedRoutine routine, string parameterName, List<ExecutionPlugin> plugins)
        {
            foreach (var plugin in plugins)
            {
                try
                {
                    var val = plugin.GetParameterValue(routine.Schema, routine.Routine, parameterName);

                    if (val != PluginSetParameterValue.DontSet)
                    {
                        return val;
                    }
                }
                catch (Exception ex)
                {
                    SessionLog.Error("Plugin {0} GetParameterValue failed", plugin.Name);
                    SessionLog.Exception(ex);
                }
            }

            return PluginSetParameterValue.DontSet;
        }

        private static void ProcessPlugins(List<ExecutionPlugin> pluginList, SqlConnection con)
        {
            foreach (var plugin in pluginList)
            {
                try
                {
                    plugin.OnConnectionOpened(con);
                }
                catch (Exception ex)
                {
                    ExceptionLogger.LogException(ex, $"Plugin {plugin.Name} OnConnectionOpened failed", "jsdal-server");
                }

            }

        }

        private static readonly System.Data.SqlDbType[] SqlStringTypes = new System.Data.SqlDbType[] { SqlDbType.Char, SqlDbType.NChar, SqlDbType.NText,
                                                                                                             SqlDbType.NVarChar, SqlDbType.Text, SqlDbType.VarChar };

        private static object ConvertParameterValue(string paramName, SqlDbType sqlType, string value, string udtType)
        {
            // if the expected value is a string return as is
            if (SqlStringTypes.Contains(sqlType))
            {
                return value;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return DBNull.Value;
            }

            switch (sqlType)
            {
                case SqlDbType.UniqueIdentifier:
                    return new Guid((string)value);
                case SqlDbType.DateTime:
                    var expectedFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFK";

                    // DateTimeOffset expect the Date & Time to be in LOCAL time
                    if (DateTimeOffset.TryParseExact(value, expectedFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dto))
                    {
                        return dto.DateTime;
                    }
                    else
                    {
                        throw new JsdalServerParameterValueException(paramName, $"Invalid DateTime value of {value ?? "(null)"}. Expected format is: {expectedFormat} e.g. {DateTime.Now.ToString(expectedFormat)}");
                    }

                case SqlDbType.Bit:
                    return ConvertToSqlBit(value);
                case SqlDbType.VarBinary:
                    return ConvertToSqlVarbinary(value);
                case SqlDbType.Timestamp:
                    return BitConverter.GetBytes(long.Parse(value)).Reverse().ToArray()/*have to reverse to match the endianness*/;
                case SqlDbType.Time:
                    return value;
                case SqlDbType.Float:
                    return double.Parse(value);
                case SqlDbType.Decimal:
                    return decimal.Parse(value);
                case SqlDbType.Udt:
                    {
                        // TODO: Refractor into separate function
                        // TODO: Add support for geometry
                        // TODO: Throw if unable to convert? (e.g. see DateTime section)
                        if (udtType.Equals("geography", StringComparison.OrdinalIgnoreCase))
                        {
                            // for geography we only support { lat: .., lng: ...} for now - in future we might support WKT strings
                            var obj = JsonConvert.DeserializeObject<dynamic>(value);
                            int srid = 4326;

                            if (obj["srid"] != null)
                            {
                                srid = (int)obj.srid;
                            }

                            // Use NetTopologySuite until MS adds support for Geography/Geometry in dotcore sql client
                            // See https://github.com/dotnet/SqlClient/issues/30


                            var geometry = new NetTopologySuite.Geometries.Point((double)obj.lng, (double)obj.lat) { SRID = srid };

                            var geometryWriter = new NetTopologySuite.IO.SqlServerBytesWriter { IsGeography = true };
                            var bytes = geometryWriter.Write(geometry);

                            return new System.Data.SqlTypes.SqlBytes(bytes);

                            //return SqlGeography.Point((double)obj.lat, (double)obj.lng, srid);
                        }

                        return value;
                    }

                default:
                    {
                        var typeName = RoutineParameterV2.GetCSharpDataTypeFromSqlDbType(sqlType.ToString().ToLower());
                        var type = Type.GetType(typeName);

                        return Convert.ChangeType(value, type);
                    }
            }
        }

        private static object ConvertToSqlBit(string value)
        {
            return int.TryParse(value, out int n) ? Convert.ToBoolean(n) : Convert.ToBoolean(value);
        }

        private static object ConvertToSqlVarbinary(string value)
        {
            var str = value.ToString();
            const string blobRefPrefix = "blobRef:";

            if (str.StartsWith(blobRefPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var key = str.Substring(blobRefPrefix.Length);

                if (!BlobStore.Exists(key)) throw new Exception($"Invalid, non-existent or expired blob reference specified: '{str}'");
                return BlobStore.Get(key).Data;
            }
            else if (str.Equals("dbnull", StringComparison.OrdinalIgnoreCase))
            {
                return DBNull.Value;
            }
            else
            {
                // assume a base64 string was posted for the binary data
                return Convert.FromBase64String(str);
            }
        }


        private static string ProcessMetadata(Dictionary<string, string> requestHeaders, ref Dictionary<string, string> responseHeaders, CachedRoutine cachedRoutine)
        {
            if (cachedRoutine?.jsDALMetadata?.jsDAL?.security?.requiresCaptcha ?? false)
            {
                if (!requestHeaders.ContainsKey("captcha-val"))
                {
                    System.Threading.Thread.Sleep(1000 * 5);  // TODO: Make this configurable? We intentionally slow down requests that do not conform 

                    return "captcha-val header not specified";
                }

                if (jsdal_server_core.Settings.SettingsInstance.Instance.Settings.GoogleRecaptchaSecret == null)
                {
                    return "The setting GoogleRecaptchaSecret is not configured on this jsDAL server.";
                }

                var captchaHeaderVal = requestHeaders.Val("captcha-val");
                if (captchaHeaderVal != null)
                {
                    var t = ValidateGoogleRecaptchaAsync(captchaHeaderVal);

                    t.Wait();
                    var capResp = t.Result;

                    if (responseHeaders == null) responseHeaders = new Dictionary<string, string>();

                    responseHeaders["captcha-ret"] = capResp ? "1" : "0";

                    if (capResp) return null;
                    else return "Captcha failed.";
                }
            }

            return null;
        }

        private static async Task<bool> ValidateGoogleRecaptchaAsync(string captcha) /*Promise<{ success: boolean, "error-codes"?: string[]}>*/
        {
            try
            {
                var postData = $"secret={Settings.SettingsInstance.Instance.Settings.GoogleRecaptchaSecret}&response={captcha}";
                var webClient = new WebClient();

                webClient.Headers["Content-Type"] = "application/x-www-form-urlencoded";

                var responseBytes = await webClient.UploadDataTaskAsync("https://www.google.com/recaptcha/api/siteverify", "POST", System.Text.Encoding.UTF8.GetBytes(postData));

                var response = System.Text.Encoding.UTF8.GetString(responseBytes);

                var deserialized = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(response);

                if (deserialized != null && deserialized["success"] != null)
                {
                    var je = ((JsonElement)deserialized["success"]);

                    return je.GetBoolean();
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex);
                return false;
            }

            /* 



                    request.post('https://www.google.com/recaptcha/api/siteverify',
                                        {
                                            headers: { 'content-type': 'application/json' },
                                            form: postData
                }, (error: any, response: request.RequestResponse, body: any) => {
                                            if (!error) {
                                                try {
                                                    resolve(JSON.parse(body));
            }
                                                catch (e) {
                                                    ExceptionLogger.logException(e);
                                                    reject(e);
                                                }
                                            }
                                            else {
                                                reject(error);
                                            }

                                        });*/

        }

        public static string ToJqxType(this Type type)
        {
            //type(optional) - A string containing the data field's type. Possible values: "string", "date", "number", "bool".
            if (type == null) return "string";

            if (typeof(DateTime) == type || typeof(DateTime?) == type)
            {
                return "date";
            }

            if (typeof(Int16) == type || typeof(Int16?) == type ||
                typeof(Int32) == type || typeof(Int32?) == type ||
                typeof(Int64) == type || typeof(Int64?) == type)
            {
                return "number";
            }

            if (typeof(Boolean) == type || typeof(Boolean?) == type)
            {
                return "bool";
            }

            return "string";
        }

        public static Dictionary<string, DataContainer> ToJsonDS(this DataSet ds, bool includeHeaderInfo = true)
        {
            if (ds == null) return null;

            var ret = new Dictionary<string, DataContainer>();

            for (var i = 0; i < ds.Tables.Count; i++)
            {
                var dt = ds.Tables[i];
                var tableName = "Table" + i;

                List<DataField> tableHeaderList = null;

                if (includeHeaderInfo)
                {
                    tableHeaderList = (
                        dt.Columns.Cast<DataColumn>()
                        .Select(col => new DataField(col.ColumnName, col.DataType.ToJqxType()))
                    ).ToList();
                }

                var tableRowList = new List<List<Object>>();

                foreach (DataRow row in dt.Rows)
                {
                    var rowValueList = new List<Object>();

                    foreach (DataColumn col in dt.Columns)
                    {
                        var val = row[col];

                        if (val is byte[])
                        {
                            rowValueList.Add(Convert.ToBase64String((byte[])val));
                        }
                        else if (val is DBNull)
                        {
                            rowValueList.Add(null);
                        }
                        else if (val is SqlGeography)
                        {
                            var geo = (SqlGeography)val;

                            if (!geo.IsNull)
                            {
                                var wkt = geo.ToString();

                                if (wkt.StartsWith("POINT") && !geo.Lat.IsNull && !geo.Long.IsNull)
                                {
                                    val = new { lat = geo.Lat.Value, lng = geo.Long.Value };
                                }
                                else
                                {
                                    val = wkt;
                                }

                                rowValueList.Add(val);
                            }
                            else
                            {
                                rowValueList.Add(null);
                            }
                        }
                        else
                        {
                            // 10/07/2015, PL: Problematic encoding all output strings - they should have been encoded "going in" ?!
                            //if (row[col] is string)
                            //{
                            //    rowValueList.Add(HttpUtility.HtmlEncode(row[col]));
                            //}
                            //else
                            {
                                rowValueList.Add(val);
                            }

                        }
                    }

                    tableRowList.Add(rowValueList);
                }

                ret.Add(tableName, new DataContainer(tableHeaderList, tableRowList));
            }

            return ret;
        }

        public class DataField
        {
            /*
             * Do Not Change the Property Names to Caps
             * This is used to pass date to the Front End For Consumption by the jqWidgets Pulugin
             * It Requires the name, type to be lower case names.
             */
            // ReSharper disable InconsistentNaming
            public string name { get; set; }
            public string type { get; set; }
            // ReSharper restore InconsistentNaming

            public DataField(string n, string t)
            {
                name = n;
                type = t;
            }
        }

        public class DataContainer
        {
            public List<DataField> Fields { get; set; }
            public List<List<Object>> Data { get; set; }

            public DataContainer(List<DataField> fields, List<List<object>> data)
            {
                Fields = fields;
                Data = data;
            }
        }


    }
}


