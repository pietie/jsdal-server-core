using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using jsdal_plugin;
using jsdal_server_core.Performance;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace jsdal_server_core.Controllers
{
    public class ExecController : Controller
    {
        private struct ExecOptions
        {
            public string dbSourceGuid;
            public string dbConnectionGuid;
            public string schema;
            public string routine;
            public ExecType Type;
            public Dictionary<string, string> OverridingInputParameters { get; set; }
        }

        public enum ExecType
        {
            Query,
            NonQuery,
            Scalar
        }

        [AllowAnonymous]
        [HttpGet("/api/execnq/{dbSourceGuid}/{dbConnectionGuid}/{schema}/{routine}")]
        [HttpPost("/api/execnq/{dbSourceGuid}/{dbConnectionGuid}/{schema}/{routine}")]
        public IActionResult execNonQuery([FromRoute] string dbSourceGuid, [FromRoute] string dbConnectionGuid, [FromRoute] string schema, [FromRoute] string routine)
        {
            return exec(new ExecOptions() { dbSourceGuid = dbSourceGuid, dbConnectionGuid = dbConnectionGuid, schema = schema, routine = routine, Type = ExecType.NonQuery });
        }

        [AllowAnonymous]
        [HttpGet("/api/exec/{dbSourceGuid}/{dbConnectionGuid}/{schema}/{routine}")]
        [HttpPost("/api/exec/{dbSourceGuid}/{dbConnectionGuid}/{schema}/{routine}")]
        public IActionResult execQuery([FromRoute] string dbSourceGuid, [FromRoute] string dbConnectionGuid, [FromRoute] string schema, [FromRoute] string routine)
        {
            return exec(new ExecOptions() { dbSourceGuid = dbSourceGuid, dbConnectionGuid = dbConnectionGuid, schema = schema, routine = routine, Type = ExecType.Query });
        }


        [AllowAnonymous]
        [HttpGet("/api/execScalar/{dbSourceGuid}/{dbConnectionGuid}/{schema}/{routine}")]
        [HttpPost("/api/execScalar/{dbSourceGuid}/{dbConnectionGuid}/{schema}/{routine}")]
        public IActionResult Scalar([FromRoute] string dbSourceGuid, [FromRoute] string dbConnectionGuid, [FromRoute] string schema, [FromRoute] string routine)
        {
            return exec(new ExecOptions() { dbSourceGuid = dbSourceGuid, dbConnectionGuid = dbConnectionGuid, schema = schema, routine = routine, Type = ExecType.Scalar });
        }

        private class BatchData
        {
            public int Ix { get; set; }
            public BatchDataRoutine Routine { get; set; }
        }

        private class BatchDataRoutine
        {
            public string dbSource { get; set; }
            public string dbConnection { get; set; }
            public string schema { get; set; }
            public string routine { get; set; }

            [JsonProperty("params")]
            public Dictionary<string, string> parameters { get; set; }


        }

        [AllowAnonymous]
        [HttpPost("/api/batch/{dbConnectionGuid}")]
        public IActionResult Batch([FromRoute] string dbConnectionGuid)
        {
            var res = this.Response;
            var req = this.Request;

            try
            {
                // TODO: Add batch metrics? Or just note on exec that it was part of a batch?

                string body = null;
                Dictionary<string, dynamic> bodyParams = null;

                int commandTimeOutInSeconds = 60;

                using (var sr = new StreamReader(req.Body))
                {
                    body = sr.ReadToEnd();

                    bodyParams = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(body);

                    // look for any other parameters sent through
                    var allOtherKeys = bodyParams.Keys.Where(k=>!k.Equals("batch-data")).ToList();

                    var baseInputParams = bodyParams.Where((kv)=> !kv.Key.Equals("batch-data"));

                    BatchData[] batchDataCollection = JsonConvert.DeserializeObject<BatchData[]>(bodyParams["batch-data"].ToString());

                    var leftTodo = batchDataCollection.Length;

                    var responses = new Dictionary<int, ApiResponse>();

                    using (ManualResetEvent waitToCompleteEvent = new ManualResetEvent(false))
                    {
                        foreach (BatchData batchItem in batchDataCollection)
                        {
                            ThreadPool.QueueUserWorkItem((state) =>
                            {
                                try
                                {
                                    var inputParameters = new Dictionary<string, string>();

                                    foreach(var kv in baseInputParams)
                                    {
                                        inputParameters.Add(kv.Key, kv.Value);
                                    }

                                    if (batchItem.Routine.parameters != null)
                                    {
                                        foreach(var kv in batchItem.Routine.parameters)
                                        {
                                            if (inputParameters.ContainsKey(kv.Key))
                                            {
                                                inputParameters[kv.Key] = kv.Value;
                                            }
                                            else
                                            {
                                                inputParameters.Add(kv.Key, kv.Value);
                                            }
                                        }
                                    }

                                    var ret = exec(new ExecOptions()
                                    {
                                        dbSourceGuid = batchItem.Routine.dbSource,
                                        dbConnectionGuid = dbConnectionGuid,
                                        schema = batchItem.Routine.schema,
                                        routine = batchItem.Routine.routine,
                                        OverridingInputParameters = inputParameters,
                                        Type = ExecType.Query

                                    }) as Microsoft.AspNetCore.Mvc.ObjectResult;

                                    if (ret != null)
                                    {
                                        var apiResponse = ret.Value as ApiResponse;

                                        lock (responses)
                                        {
                                            responses.Add(batchItem.Ix, apiResponse);
                                        }
                                    }
                                }
                                catch (Exception execEx)
                                {
                                    // TODO: handle by pushing this as the respone?
                                    responses.Add(batchItem.Ix, ApiResponse.Exception(execEx));
                                }
                                finally
                                {
                                    if (Interlocked.Decrement(ref leftTodo) == 0)
                                    {
                                        waitToCompleteEvent.Set();
                                    }
                                }

                            });
                        } // foreach

                        // TODO: Make timeout configurable?
                        if (!waitToCompleteEvent.WaitOne(TimeSpan.FromSeconds(60*6)))
                        {
                            // TODO: Report timeout error?
                            return BadRequest("Response(s) was not received in time.");
                        }

                        return Ok(ApiResponse.Payload(responses));


                    } // using ManualResetEvent                
                } // using StreamReader

            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Exception(ex));
            }
        }

        private IActionResult exec(ExecOptions execOptions)
        {
            var debugInfo = "";
            var res = this.Response;
            var req = this.Request;

            string appTitle = null;

            DatabaseSource dbSource = null;
// record client info? IP etc? Record other interestsing info like Connection and DbSource used -- maybe only for the realtime connections? ... or metrics should be against connection at least?
            var routineExecutionMetric = ExecTracker.Begin(execOptions.dbSourceGuid, execOptions.schema, execOptions.routine);

            List<jsDALPlugin> pluginList = null;

            try
            {

                appTitle = req.Headers["App-Title"];

                // always start off not caching whatever we send back
                res.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                res.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
                res.Headers["Content-Type"] = "application/json";

                var isPOST = req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

                debugInfo += $"[{execOptions.schema}].[{execOptions.routine}]";

                var dbSources = SettingsInstance.Instance.ProjectList.SelectMany(p => p.DatabaseSources);
                //var dbSourcesFlat = [].concat.apply([], dbSources); // flatten the array of arrays

                dbSource = dbSources.FirstOrDefault(dbs => dbs.CacheKey == execOptions.dbSourceGuid);

                if (dbSource == null) throw new Exception($"The specified DB source \"{execOptions.dbSourceGuid}\" was not found.");

                // make sure the source domain/IP is allowed access
                var mayAccess = dbSource.mayAccessDbSource(this.Request);

                if (!mayAccess.isSuccess)
                {
                    res.ContentType = "text/plain";
                    res.StatusCode = 403;
                    return this.Content(mayAccess.userErrorVal);
                }

                string body = null;
                Dictionary<string, string> inputParameters = null;
                Dictionary<string, dynamic> outputParameters;
                int commandTimeOutInSeconds = 60;

                if (execOptions.OverridingInputParameters != null)
                {
                    inputParameters = execOptions.OverridingInputParameters;
                }
                else
                {
                    if (isPOST)
                    {
                        using (var sr = new StreamReader(req.Body))
                        {
                            body = sr.ReadToEnd();

                            inputParameters = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                        }
                    }
                    else
                    {
                        inputParameters = req.Query.ToDictionary(t => t.Key, t => t.Value.ToString());
                    }
                }

                if (inputParameters == null) inputParameters = new Dictionary<string, string>();

                // PLUGINS
                var pluginsInitMetric = routineExecutionMetric.BeginChildStage("Init plugins");

                pluginList = InitPlugins(dbSource, inputParameters);

                pluginsInitMetric.End();

                var execRoutineQueryMetric = routineExecutionMetric.BeginChildStage("execRoutineQuery");

                int rowsAffected;

                // DB call
                var executionResult = OrmDAL.execRoutineQuery(req, res,
                    execOptions.Type,
                    execOptions.schema,
                    execOptions.routine,
                    dbSource,
                    execOptions.dbConnectionGuid,
                    inputParameters,
                    pluginList,
                    commandTimeOutInSeconds,
                    out outputParameters,
                    execRoutineQueryMetric,
                    out rowsAffected
                );

                execRoutineQueryMetric.End();
                
                routineExecutionMetric.RowsAffected = rowsAffected;
                

                var prepareResultsMetric = routineExecutionMetric.BeginChildStage("Prepare results");

                if (!string.IsNullOrEmpty(executionResult.userError))
                {
                    return Ok(ApiResponse.ExclamationModal(executionResult.userError));
                }

                var retVal = (IDictionary<string, object>)new System.Dynamic.ExpandoObject();
                var ret = ApiResponse.Payload(retVal);

                retVal.Add("OutputParms", outputParameters);

                if (outputParameters != null)
                {// TODO: Consider making this a plugin
                    var possibleUEParmNames = (new string[] { "usererrormsg", "usrerrmsg", "usererrormessage", "usererror", "usererrmsg" }).ToList();

                    var ueKey = outputParameters.Keys.FirstOrDefault(k => possibleUEParmNames.Contains(k.ToLower()));

                    // if a user error msg is defined.
                    if (!string.IsNullOrWhiteSpace(ueKey) && !string.IsNullOrWhiteSpace(outputParameters[ueKey]))
                    {
                        ret.Message = outputParameters[ueKey];
                        ret.Title = "Action failed";
                        ret.Type = ApiResponseType.ExclamationModal;
                    }
                }

                if (execOptions.Type == ExecType.Query)
                {
                    var dataSet = executionResult.DataSet;
                    var dataContainers = dataSet.ToJsonDS();

                    var keys = dataContainers.Keys.ToList();

                    for (var i = 0; i < keys.Count; i++)
                    {
                        retVal.Add(keys[i], dataContainers[keys[i]]);
                    }

                    retVal.Add("HasResultSets", keys.Count > 0);
                    retVal.Add("ResultSetKeys", keys.ToArray());
                }
                else if (execOptions.Type == ExecType.NonQuery)
                {

                }
                else if (execOptions.Type == ExecType.Scalar)
                {

                    if (executionResult.ScalarValue is DateTime)
                    {
                        var dt = (DateTime)executionResult.ScalarValue;

                        // convert to Javascript Date ticks
                        var ticks = dt.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

                        ret = ApiResponseScalar.Payload(ticks, true);
                    }
                    else
                    {
                        ret = ApiResponse.Payload(executionResult.ScalarValue);
                    }
                }

                prepareResultsMetric.End();
                routineExecutionMetric.End();

                return Ok(ret);
            }
            catch (Exception ex)
            {
                routineExecutionMetric.Exception(ex);

                Connection dbConn = null;

                if (!string.IsNullOrWhiteSpace(execOptions.dbConnectionGuid) && dbSource != null)
                {
                    dbConn = dbSource.getSqlConnection(execOptions.dbConnectionGuid);

                    if (debugInfo == null) debugInfo = "";

                    if (dbConn != null)
                    {
                        debugInfo = $"{ dbSource.Name } - { dbConn.initialCatalog } - { debugInfo }";
                    }
                    else
                    {
                        debugInfo = $"{ dbSource.Name } - (no connection) - { debugInfo }";
                    }

                }

                var exceptionResponse = ApiResponse.Exception(ex, debugInfo, appTitle);

                if (pluginList != null)
                {
                    string externalRef;

                    if (dbConn != null)
                    {
                        using (var con = new SqlConnection(dbConn.ConnectionStringDecrypted))
                        {
                            try
                            {
                                con.Open();
                                ProcessPluginExectionExceptionHandlers(pluginList, con, ex, out externalRef);
                                ((dynamic)exceptionResponse.Data).ExternalRef = externalRef;
                            }
                            catch (Exception e)
                            {
                                SessionLog.Exception(e);
                            }

                        }
                    } // else: TODO: Log fact that we dont have a proper connection string.. or should plugins handle that?
                }



                // return it as "200 (Ok)" because the exception has been handled
                return Ok(exceptionResponse);
                //return BadRequest(exceptionResponse);
            }
        }

        private static void ProcessPluginExectionExceptionHandlers(List<jsDALPlugin> pluginList, SqlConnection con, Exception ex, out string externalRef)
        {
            externalRef = null;
            if (pluginList == null) return;
            foreach (var plugin in pluginList)
            {
                try
                {
                    string msg = null;
                    string externalRefTmp = null;

                    plugin.OnExecutionException(con, ex, out externalRefTmp, out msg);

                    if (!string.IsNullOrWhiteSpace(externalRefTmp))
                    {
                        externalRef = externalRefTmp;
                    }
                }
                catch (Exception e)
                {
                    SessionLog.Error("Plugin {0} OnExecutionException failed", plugin.Name);
                    SessionLog.Exception(e);
                }
            }
        }

        private static MethodInfo initPluginMethod = typeof(jsDALPlugin).GetMethod("InitPlugin", BindingFlags.NonPublic | BindingFlags.Instance);
        private static List<jsDALPlugin> InitPlugins(DatabaseSource dbSource, Dictionary<string, string> queryString)
        {
            var plugins = new List<jsDALPlugin>();

            if (Program.PluginAssemblies != null && dbSource.Plugins != null)
            {
                foreach (string pluginGuid in dbSource.Plugins)
                {
                    var plugin = Program.PluginAssemblies.SelectMany(kv => kv.Value).FirstOrDefault(p => p.Guid.ToString().Equals(pluginGuid, StringComparison.OrdinalIgnoreCase));

                    if (plugin != null)
                    {
                        try
                        {
                            var concrete = (jsDALPlugin)plugin.Assembly.CreateInstance(plugin.TypeInfo.FullName);

                            initPluginMethod.Invoke(concrete, new object[] { queryString });

                            plugins.Add(concrete);
                        }
                        catch (Exception ex)
                        {
                            SessionLog.Error("Failed to instantiate '{0}' ({1}) on assembly '{2}'", plugin.TypeInfo.FullName, pluginGuid, plugin.Assembly.FullName);
                            SessionLog.Exception(ex);
                        }
                    }
                    else
                    {
                        SessionLog.Warning("The specified plugin GUID '{0}' was not found in the list of loaded plugins.", pluginGuid);
                    }
                }
            }

            return plugins;
        }


        public static SqlDbType GetSqlDbTypeFromParameterType(string parameterDataType)
        {
            switch (parameterDataType.ToLower())
            {
                case "date":
                    return SqlDbType.Date;
                case "datetime":
                    return SqlDbType.DateTime;
                case "time":
                    return SqlDbType.VarChar; // send as a simple string and let SQL take care of it
                                              //return SqlDbType.Time;
                case "smalldatetime":
                    return SqlDbType.SmallDateTime;
                case "int":
                    return SqlDbType.Int;
                case "smallint":
                    return SqlDbType.SmallInt;
                case "bigint":
                    return SqlDbType.BigInt;
                case "bit":
                    return SqlDbType.Bit;
                case "nvarchar":
                    return SqlDbType.NVarChar;
                case "varchar":
                    return SqlDbType.VarChar;
                case "text":
                    return SqlDbType.Text;
                case "ntext":
                    return SqlDbType.NText;
                case "varbinary":
                    return SqlDbType.VarBinary;
                case "decimal":
                    return SqlDbType.Decimal;
                case "uniqueidentifier":
                    return SqlDbType.UniqueIdentifier;
                case "money":
                    return SqlDbType.Money;
                case "char":
                    return SqlDbType.Char;
                case "nchar":
                    return SqlDbType.NChar;
                case "xml":
                    return SqlDbType.Xml;
                case "float":
                    return SqlDbType.Float;
                case "image":
                    return SqlDbType.Image;
                case "tinyint":
                    return SqlDbType.TinyInt;
                default:
                    throw new NotSupportedException("GetSqlDbTypeFromParameterType::Unsupported data type: " + parameterDataType);
            }
        }
    }
}