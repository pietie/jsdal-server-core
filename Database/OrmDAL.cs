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
using Microsoft.SqlServer.Types;
using System.Threading.Tasks;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;


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
                cmd.CommandText = "ormv2.GetRoutineListCnt";
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

        public static DataSet RoutineGetFmtOnlyResults(string connectionString, string schema, string routine, List<RoutineParameterV2> parameterList, out string error)
        {
            error = null;
            // we have to open a new connection as there is an open Reader across the main connection
            using (var con = new SqlConnection(connectionString))
            {
                con.Open();

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
                    using (var reader = dbCmd.ExecuteReader(CommandBehavior.SchemaOnly))
                    {
                        var schemaTable = reader.GetSchemaTable();

                        while (schemaTable != null)
                        {
                            schemaTable.TableName = "Table" + ds.Tables.Count;
                            ds.Tables.Add(schemaTable);
                            bool b = reader.NextResult();
                            schemaTable = reader.GetSchemaTable();
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

                return ds;
            }
        }

        public class ExecutionResult
        {
            public DataSet DataSet { get; set; }
            public object ScalarValue { get; set; }
            public string userError { get; set; }

            // optional stats
            public long? BytesReceived { get; set; }
            public long? NetworkServerTimeInMS { get; set; }
        }

        public static ExecutionResult ExecRoutineQuery(
               Controllers.ExecController.ExecType type,
               string schemaName,
               string routineName,
               Endpoint endpoint,
               Dictionary<string, string> inputParameters,
               Dictionary<string, string> requestHeaders,
               string remoteIpAddress,
               List<ExecutionPlugin> plugins,
               int commandTimeOutInSeconds,
               out Dictionary<string, dynamic> outputParameterDictionary,
               ExecutionBase execRoutineQueryMetric,
               ref Dictionary<string, string> responseHeaders,
               out int rowsAffected
           )
        {
            SqlConnection con = null;
            SqlCommand cmd = null;

            rowsAffected = 0;


            try
            {
                var s1 = execRoutineQueryMetric.BeginChildStage("Lookup cached routine");

                var routineCache = endpoint.CachedRoutines;
                var cachedRoutine = routineCache.FirstOrDefault(r => r.Equals(schemaName, routineName));

                outputParameterDictionary = new Dictionary<string, dynamic>();

                if (cachedRoutine == null)
                {
                    // TODO: Return 404 rather?
                    throw new Exception($"The routine [{ schemaName }].[{routineName}] was not found.");
                }

                s1.End();

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

                var s3 = execRoutineQueryMetric.BeginChildStage("Open connection");

                var cs = endpoint.GetSqlConnection();

                if (cs == null)
                {
                    throw new Exception($"Execution connection not found on endpoint '{endpoint.Pedigree}'({ endpoint.Id }).");
                }

                con = new SqlConnection(cs.ConnectionStringDecrypted);

                if (endpoint.CaptureConnectionStats)
                {
                    con.StatisticsEnabled = true;
                    con.ResetStatistics();
                }
                else
                {
                    con.StatisticsEnabled = false;
                }

                con.Open();

                s3.End();

                var s4 = execRoutineQueryMetric.BeginChildStage("Process plugins");

                // PLUGINS
                ProcessPlugins(plugins, con);

                s4.End();

                var prepareCmdMetric = execRoutineQueryMetric.BeginChildStage("Prepare command");

                cmd = new SqlCommand();
                cmd.Connection = con;

                var isTVF = cachedRoutine.Type == "TVF";

                //   if (cachedRoutine.Type == "PROCEDURE" || isTVF)
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = string.Format("[{0}].[{1}]", schemaName, routineName);

                    if (isTVF)
                    {
                        string parmCsvList = string.Join(",", cachedRoutine.Parameters.Where(p => !p.IsResult).Select(p => p.Name).ToArray());

                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = string.Format("select * from [{0}].[{1}]({2})", schemaName, routineName, parmCsvList);
                    }
                    else if (type == Controllers.ExecController.ExecType.Scalar)
                    {
                        string parmCsvList = string.Join(",", cachedRoutine.Parameters.Where(p => !p.IsResult).Select(p => p.Name).ToArray());

                        if (cachedRoutine.Type.Equals("PROCEDURE", StringComparison.CurrentCultureIgnoreCase))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.CommandText = string.Format("[{0}].[{1}]", schemaName, routineName);
                        }
                        else if (cachedRoutine.Type.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
                        {
                            cmd.CommandType = CommandType.Text;
                            cmd.CommandText = string.Format("select [{0}].[{1}]({2})", schemaName, routineName, parmCsvList);
                        }
                    }

                    if (cachedRoutine.Parameters != null)
                    {
                        /*
                            Parameters
                            ¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯¯
                                (Parm not specified)    ->  (Has default in sproc def)  -> Add to command with C# null to inherit null value
                                                            (No default)                -> Don't add to command, should result in SQL Exception
                                
                                Parm specified as null  ->  DBNull.Value (regardless of Sproc default value)
                                $jsDAL$DBNull           ->  Add to command with value as DBNull.Value

                        
                         */
                        cachedRoutine.Parameters.ForEach(expectedParm =>
                        {
                            if (expectedParm.IsResult) return;

                            (var sqlType, var udtType) = Controllers.ExecController.GetSqlDbTypeFromParameterType(expectedParm.SqlDataType);
                            object parmValue = null;

                            //??var newSqlParm = cmd.Parameters.Add(p.Name, sqlType, p.MaxLength);
                            var newSqlParm = new SqlParameter(expectedParm.Name, sqlType, expectedParm.MaxLength);

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


                            // if the expected parameter was defined in the request or if a plugin provided an override
                            if (inputParameters.ContainsKey(expectedParmName) || pluginParamVal != PluginSetParameterValue.DontSet)
                            {
                                object val = null;

                                // TODO: Should input parameter if specified be able to override any plugin value?
                                // TODO: For now a plugin value take precendence over an input value - we can perhaps make this a property of the plugin return value (e.g. AllowInputOverride)
                                if (pluginParamVal != PluginSetParameterValue.DontSet)
                                {
                                    val = pluginParamVal.Value;
                                }
                                else if (inputParameters.ContainsKey(expectedParmName))
                                {
                                    val = inputParameters[expectedParmName];
                                }

                                // look for special jsDAL Server variables
                                val = jsDALServerVariables.Parse(remoteIpAddress, val);

                                if (val == null || val == DBNull.Value)
                                {
                                    parmValue = DBNull.Value;
                                }
                                // TODO: Consider making this 'null' mapping configurable.This is just a nice to have for when the client does not call the API correctly
                                // convert the string value of 'null' to actual C# null
                                else if (val.ToString().Trim().ToLower().Equals("null"))
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

                    prepareCmdMetric.End();

                    DataSet ds = null;
                    object scalarVal = null;

                    if (type == Controllers.ExecController.ExecType.Query)
                    {
                        var execStage = execRoutineQueryMetric.BeginChildStage("Execute Query");

                        var da = new SqlDataAdapter(cmd);
                        ds = new DataSet();

                        cmd.CommandTimeout = commandTimeOutInSeconds;

                        var firstTableRowsAffected = da.Fill(ds); // Fill only returns rows affected on first Table

                        if (ds.Tables.Count > 0)
                        {
                            foreach (DataTable t in ds.Tables)
                            {
                                rowsAffected += t.Rows.Count;
                            }
                        }

                        if (inputParameters.ContainsKey("$select") && ds.Tables.Count > 0)
                        {
                            string limitToFieldsCsv = inputParameters["$select"];

                            if (!string.IsNullOrEmpty(limitToFieldsCsv))
                            {
                                var listPerTable = limitToFieldsCsv.Split(new char[] { ';' }/*, StringSplitOptions.RemoveEmptyEntries*/);

                                for (int tableIx = 0; tableIx < listPerTable.Length; tableIx++)
                                {
                                    var fieldsToKeep = listPerTable[tableIx].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToLookup(s => s.Trim());

                                    if (fieldsToKeep.Count > 0)
                                    {
                                        var table = ds.Tables[tableIx];

                                        for (int i = 0; i < table.Columns.Count; i++)
                                        {
                                            var match = fieldsToKeep.FirstOrDefault((k) => k.Key.Equals(table.Columns[i].ColumnName, StringComparison.OrdinalIgnoreCase));

                                            if (match == null)
                                            {
                                                table.Columns.Remove(table.Columns[i]);
                                                i--;
                                            }
                                        }

                                    }
                                }


                            }

                        } // $select

                        execStage.End();
                    }
                    else if (type == Controllers.ExecController.ExecType.NonQuery)
                    {
                        var execStage = execRoutineQueryMetric.BeginChildStage("Execute NonQuery");

                        //TODO: Implement Async versions cmd.ExecuteNonQueryAsync()

                        rowsAffected = cmd.ExecuteNonQuery();
                        execStage.End();
                    }
                    else if (type == Controllers.ExecController.ExecType.Scalar)
                    {
                        var execStage = execRoutineQueryMetric.BeginChildStage("Execute Scalar");
                        scalarVal = cmd.ExecuteScalar();
                        execStage.End();
                    }
                    else
                    {
                        throw new NotSupportedException($"ExecType \"{type.ToString()}\" not supported");
                    }

                    long? bytesReceived = null;
                    long? networkServerTimeMS = null;

                    if (endpoint.CaptureConnectionStats)
                    {
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

                    //!executionTrackingEndFunction();

                    return new ExecutionResult() { DataSet = ds, ScalarValue = scalarVal, NetworkServerTimeInMS = networkServerTimeMS, BytesReceived = bytesReceived };
                }
            }
            finally
            {
                if (cmd != null)
                {
                    cmd.Dispose();
                }
                if (con != null)
                {
                    con.Close();
                    con.Dispose();
                }

            }
        } // execRoutine

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

                            return SqlGeography.Point((double)obj.lat, (double)obj.lng, srid);
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
            var blobRefPrefix = "blobRef:";

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
                    var capResp = ValidateGoogleRecaptcha(captchaHeaderVal);

                    if (responseHeaders == null) responseHeaders = new Dictionary<string, string>();

                    responseHeaders["captcha-ret"] = capResp ? "1" : "0";

                    if (capResp) return null;
                    else return "Captcha failed.";
                }
            }

            return null;
        }

        private static bool ValidateGoogleRecaptcha(string captcha) /*Promise<{ success: boolean, "error-codes"?: string[]}>*/
        {
            try
            {
                var postData = $"secret={Settings.SettingsInstance.Instance.Settings.GoogleRecaptchaSecret}&response={captcha}";
                var webClient = new WebClient();

                webClient.Headers["Content-Type"] = "application/x-www-form-urlencoded";
                //var response = webClient.UploadString("https://www.google.com/recaptcha/api/siteverify", postData);
                var responseBytes = webClient.UploadData("https://www.google.com/recaptcha/api/siteverify", "POST", System.Text.Encoding.UTF8.GetBytes(postData));

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


