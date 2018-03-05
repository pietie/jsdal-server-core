
using System.Data.SqlClient;
using System.Collections.Generic;
using jsdal_server_core.Settings.ObjectModel;
using System.Data;
using System;
using System.Data.Common;
using System.Reflection;
using System.Text;
using System.Linq;
using Microsoft.AspNetCore.Http;
using jsdal_plugin;
using jsdal_server_core.Performance;

namespace jsdal_server_core
{
    public static class OrmDAL
    {
        public static int SprocGenGetRoutineListCnt(SqlConnection con, long? maxRowDate)
        {
            using (var cmd = new SqlCommand())
            {

                cmd.Connection = con;
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.CommandText = "orm.SprocGenGetRoutineListCnt";
                cmd.Parameters.Add("maxRowver", System.Data.SqlDbType.BigInt).Value = maxRowDate ?? 0;

                var scalar = cmd.ExecuteScalar();

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

        public static DataSet RoutineGetFmtOnlyResults(string connectionString, string schema, string routine, List<RoutineParameter> parameterList, out string error)
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
                        if (p.IsResult.Equals("YES", StringComparison.OrdinalIgnoreCase)) continue;
                        var parm = CreateParameter(dbCmd, p.ParameterName, Controllers.ExecController.GetSqlDbTypeFromParameterType(p.DataType));
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
        }

        public static ExecutionResult execRoutineQuery(HttpRequest req, HttpResponse res,
               Controllers.ExecController.ExecType type,
               string schemaName,
               string routineName,
               DatabaseSource dbSource,
               string dbConnectionGuid,
               Dictionary<string, string> inputParameters,
               List<jsDALPlugin> plugins,
               int commandTimeOutInSeconds,
               out Dictionary<string, dynamic> outputParameterDictionary,
               ExecutionBase execRoutineQueryMetric,
               out int rowsAffected
           )
        {
            SqlConnection con = null;
            SqlCommand cmd = null;

            rowsAffected = 0;

            try
            {
                var s1 = execRoutineQueryMetric.BeginChildStage("Lookup cached routine");

                var routineCache = dbSource.cache;
                var cachedRoutine = routineCache.FirstOrDefault(r => r.equals(schemaName, routineName));


                outputParameterDictionary = new Dictionary<string, dynamic>();

                if (cachedRoutine == null)
                {
                    // TODO: Return 404 rather?
                    throw new Exception($"The routine[{ schemaName }].[{routineName}] was not found.");
                }

                s1.End();

                var s2 = execRoutineQueryMetric.BeginChildStage("Process metadata");

                string metaResp = null;

                try
                {
                    metaResp = processMetadata(req, res, cachedRoutine);
                }
                catch (Exception) { /*ignore metadata failures*/ }

                if (metaResp != null)
                {
                    return new ExecutionResult() { userError = metaResp };
                }

                s2.End();

                var s3 = execRoutineQueryMetric.BeginChildStage("Open connection");

                var cs = dbSource.getSqlConnection(dbConnectionGuid);
                con = new SqlConnection(cs.ConnectionStringDecrypted);

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
                        string parmCsvList = string.Join(",", cachedRoutine.Parameters.Where(p => !p.IsResult.Equals("YES", StringComparison.OrdinalIgnoreCase)).Select(p => p.ParameterName).ToArray());

                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = string.Format("select * from [{0}].[{1}]({2})", schemaName, routineName, parmCsvList);
                    }
                    else if (type == Controllers.ExecController.ExecType.Scalar)
                    {
                        string parmCsvList = string.Join(",", cachedRoutine.Parameters.Where(p => !p.IsResult.Equals("YES", StringComparison.OrdinalIgnoreCase)).Select(p => p.ParameterName).ToArray());

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
                        cachedRoutine.Parameters.ForEach(p =>
                        {
                            if (p.IsResult.Equals("yes", StringComparison.OrdinalIgnoreCase)) return;

                            var sqlType = Controllers.ExecController.GetSqlDbTypeFromParameterType(p.DataType);
                            object parmValue = null;

                            var newSqlParm = cmd.Parameters.Add(p.ParameterName, sqlType, p.Length ?? 32);

                            if (p.ParameterMode == "IN")
                            {
                                newSqlParm.Direction = ParameterDirection.Input;
                            }
                            else if (p.ParameterMode == "INOUT")
                            {
                                newSqlParm.Direction = ParameterDirection.InputOutput;
                            }

                            // trim leading '@'
                            var parmName = p.ParameterName.Substring(1);

                            if (inputParameters.ContainsKey(parmName))
                            {
                                string val = inputParameters[parmName];

                                // look for special jsDAL Server variables
                                val = jsDALServerVariables.parse(req, val);

                                if (val == null)
                                {
                                    parmValue = null;
                                }
                                // TODO: Consider making this 'null' mapping configurable.This is just a nice to have for when the client does not call the API correctly
                                // convert the string value of 'null' to actual C# null
                                else if (val == "null")
                                {
                                    parmValue = null;
                                }
                                else
                                {
                                    parmValue = convertParameterValue(sqlType, val);
                                }

                                newSqlParm.Value = parmValue;
                            }
                            else
                            {
                                // do not skip on INOUT parameters as SQL does not apply default values to OUT parameters
                                if (p.HasDefault && (p.ParameterMode == "IN"))
                                {
                                    // If no explicit value was specified and the parameter has it's own default...
                                    // Then DO NOT set newSqlParm.Value so that the DB Engine applies the default defined in SQL
                                    newSqlParm.Value = null;
                                }
                                else
                                {
                                    newSqlParm.Value = DBNull.Value;
                                }

                                //     if (p.HasDefault) // fall back to default parameter value if one exists
                                //     {
                                //         // If no explicit value was specified and the parameter has it's own default...
                                //         // Then DO NOT set newSqlParm.Value so that the DB Engine applies the default defined in SQL
                                //         newSqlParm.Value = null;
                                //     }
                                //     else {
                                //         newSqlParm.Value = DBNull.Value;
                                //     }
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


                    if (cachedRoutine?.Parameters != null)
                    {
                        var outputParmList = (from p in cachedRoutine.Parameters
                                              where p.ParameterMode.Equals("INOUT", StringComparison.OrdinalIgnoreCase)
                                              select p).ToList();


                        // Retrieve OUT-parameters and their values
                        foreach (var outParm in outputParmList)
                        {
                            object val = null;

                            val = cmd.Parameters[outParm.ParameterName].Value;

                            if (val == DBNull.Value) val = null;

                            outputParameterDictionary.Add(outParm.ParameterName.TrimStart('@'), val);
                        }
                    }



                    //!executionTrackingEndFunction();

                    return new ExecutionResult() { DataSet = ds, ScalarValue = scalarVal };

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


        private static void ProcessPlugins(List<jsDALPlugin> pluginList, SqlConnection con)
        {
            foreach (var plugin in pluginList)
            {
                try
                {
                    plugin.OnConnectionOpened(con);
                }
                catch (Exception ex)
                {
                    SessionLog.Error("Plugin {0} OnConnectionOpened failed", plugin.Name);
                    SessionLog.Exception(ex);
                }

            }

        }

        private static object convertParameterValue(SqlDbType sqlType, string value)
        {
            // if the expected value is a string return as is
            if ((new SqlDbType[] { SqlDbType.Char, SqlDbType.NChar, SqlDbType.NText, SqlDbType.NVarChar, SqlDbType.Text, SqlDbType.VarChar }).Contains(sqlType))
            {
                return value;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return DBNull.Value;
            }
            //            var valueType = value.GetType();


            // if (valueType == typeof(string) && ((string)value).Equals("null", StringComparison.OrdinalIgnoreCase))
            // {
            //     return DBNull.Value;
            // }

            switch (sqlType)
            {
                case SqlDbType.UniqueIdentifier:
                    return new Guid((string)value);
                case SqlDbType.DateTime:




                    //string text = "2013-07-03T02:16:03.000+01:00";
                    // we expect ISO 8601 format (with time offset included)
                    if (DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.FFFK", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                    {
                        return dt;
                    }
                    return null; // TODO: consider just returning the original value and hope for the best?
                                 //return value; 
                case SqlDbType.Bit:
                    return ConvertToSqlBit(value);
                case SqlDbType.VarBinary:
                    return ConvertToSqlVarbinary(value);
                case SqlDbType.Time:
                    return value;

                default:
                    return value;
                    //default:
                    //return Convert.ChangeType(value, sqlType.);
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
                var key = Guid.Parse(str.Substring(blobRefPrefix.Length));

                //!if (!BlobStore.Exists(key)) throw new Exception($"Invalid, non-existent or expired blob reference specified: '{str}'");
                //!return BlobStore.Get(key);
                throw new NotImplementedException("TODO: implement BlobStorage?");
            }
            else
            {
                // assume a base64 string was posted for the binary data
                return Convert.FromBase64String(str);
            }
        }


        private static string processMetadata(HttpRequest req, HttpResponse res, CachedRoutine cachedRoutine)
        {
            if (cachedRoutine?.jsDALMetadata?.jsDAL?.security?.requiresCaptcha ?? false)
            {
                if (!req.Headers.ContainsKey("captcha-val"))
                {
                    System.Threading.Thread.Sleep(1000 * 5);  // TODO: Make this configurable? We intentionally slow down requests that do not conform 

                    return "captcha-val header not specified";
                }

                if (jsdal_server_core.Settings.SettingsInstance.Instance.Settings.GoogleRecaptchaSecret != null)
                {
                    return "The setting GoogleRecaptchaSecret is not configured on this jsDAL server.";
                }

                var capResp = validateGoogleRecaptcha(req.Headers["captcha-val"]);

                res.Headers["captcha-ret"] = capResp.success ? "1" : "0";

                if (capResp.success) return null;
                else return "Captcha failed.";
            }

            return null;
        }

        private static dynamic validateGoogleRecaptcha(string captcha) /*Promise<{ success: boolean, "error-codes"?: string[]}>*/
        {

            return null;
            // //             var postData = {
            // //                 secret: SettingsInstance.Instance.Settings.GoogleRecaptchaSecret,
            // //                 response: captcha

            // //             };

            // //         request.post('https://www.google.com/recaptcha/api/siteverify',
            // //                 {
            // //                     headers: { 'content-type': 'application/json' },
            // //                     form: postData
            // //     }, (error: any, response: request.RequestResponse, body: any) => {
            // //                     if (!error) {
            // //                         try {
            // //                             resolve(JSON.parse(body));
            // // }
            // //                         catch (e) {
            // //                             ExceptionLogger.logException(e);
            // //                             reject(e);
            // //                         }
            // //                     }
            // //                     else {
            // //                         reject(error);
            // //                     }

            // //                 });

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