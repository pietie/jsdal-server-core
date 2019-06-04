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
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http.Features;

namespace jsdal_server_core.Controllers
{
    public class ExecController : Controller
    {

        private readonly IConfiguration config;

        public ExecController(IConfiguration configuration)
        {
            this.config = configuration;
        }

        public class ExecOptions
        {
            public string project;
            public string application;
            public string endpoint;

            public string schema;
            public string routine;
            public ExecType type;
            [JsonIgnore]
            public Dictionary<string, string> OverridingInputParameters { get; set; }

            public Dictionary<string, string> inputParameters { get; set; }
        }


        public enum ExecType
        {
            Query = 0,
            NonQuery = 1,
            Scalar = 2
        }

        [AllowAnonymous]
        [HttpGet("/api/execnq/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/execnq/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public IActionResult execNonQuery([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            return exec(new ExecOptions() { project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.NonQuery });
        }

        [AllowAnonymous]
        [HttpGet("/api/exec/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/exec/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public IActionResult execQuery([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            return exec(new ExecOptions() { project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.Query });
        }

        [AllowAnonymous]
        [HttpGet("/api/execScalar/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/execScalar/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public IActionResult Scalar([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            return exec(new ExecOptions() { project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.Scalar });
        }

        private class BatchData
        {
            public int Ix { get; set; }
            public BatchDataRoutine Routine { get; set; }
        }

        private class BatchDataRoutine
        {
            public string project { get; set; }
            public string application { get; set; }
            public string endpoint { get; set; }
            public string schema { get; set; }
            public string routine { get; set; }

            [JsonProperty("params")]
            public Dictionary<string, string> parameters { get; set; }

        }

        [AllowAnonymous]
        [HttpGet("/api/util/scanpdf417/{blobRef}")]
        [HttpPost("/api/util/scanpdf417/{blobRef}")]
        public async Task<IActionResult> ScanPdf417(string blobRef, [FromQuery] bool? raw = false, [FromQuery] bool? veh = false, [FromQuery] bool? drv = false)
        {
            var res = this.Response;
            var req = this.Request;

            try
            {
                // always start off not caching whatever we send back
                res.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                res.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
                res.Headers["Content-Type"] = "application/json";

                if (!BlobStore.Exists(blobRef)) return NotFound($"Invalid, non-existent or expired blob reference specified: '{blobRef}'");

                var blob = BlobStore.Get(blobRef);

                var client = new System.Net.Http.HttpClient();

                using (var content = new System.Net.Http.ByteArrayContent(blob))
                {
                    var barcodeServiceUrl = this.config["AppSettings:BarcodeService.URL"].TrimEnd('/');
                    var postUrl = $"{barcodeServiceUrl}/scan/pdf417?raw={raw}&veh={veh}&drv={drv}";

                    var response = await client.PostAsync(postUrl, content);

                    var responseText = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        var json = JsonConvert.DeserializeObject(responseText);

                        return Ok(ApiResponse.Payload(json));
                    }
                    else
                    {
                        SessionLog.Error("Barcode failed. postUrl = {0}; contentLength: {1}; responseText={2}", postUrl ?? "(null)", blob?.Length ?? -1, responseText ?? "(null)");

                        //return StatusCode((int)response.StatusCode, responseText);
                        //return new ContentResult() { Content = responseText, StatusCode = (int)response.StatusCode, ContentType = "text/plain" };
                        return BadRequest(responseText);
                    }

                }
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Exception(ex));
            }
        }

        [AllowAnonymous]
        [HttpPost("/api/blob")]
        public IActionResult PrepareBlob()
        {
            // TODO: Limit allowable size of post
            var res = this.Response;
            var req = this.Request;

            try
            {

                // always start off not caching whatever we send back
                res.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                res.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
                res.Headers["Content-Type"] = "application/json";

                var keyList = new List<string>();

                foreach (var file in req.Form.Files)
                {
                    var id = shortid.ShortId.Generate(useNumbers: true, useSpecial: false, length: 6);
                    var data = new byte[file.Length];

                    using (var stream = file.OpenReadStream())
                    {
                        stream.Read(data, 0, data.Length);
                    }

                    BlobStore.Add(id, data);
                    keyList.Add(id);
                }

                return Ok(ApiResponse.Payload(keyList));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Exception(ex));
            }
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
                    var allOtherKeys = bodyParams.Keys.Where(k => !k.Equals("batch-data")).ToList();

                    var baseInputParams = bodyParams.Where((kv) => !kv.Key.Equals("batch-data"));

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

                                    foreach (var kv in baseInputParams)
                                    {
                                        inputParameters.Add(kv.Key, kv.Value);
                                    }

                                    if (batchItem.Routine.parameters != null)
                                    {
                                        foreach (var kv in batchItem.Routine.parameters)
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
                                        project = batchItem.Routine.project,
                                        application = batchItem.Routine.application,
                                        endpoint = batchItem.Routine.endpoint,
                                        schema = batchItem.Routine.schema,
                                        routine = batchItem.Routine.routine,
                                        OverridingInputParameters = inputParameters,
                                        type = ExecType.Query

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
                        if (!waitToCompleteEvent.WaitOne(TimeSpan.FromSeconds(60 * 6)))
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
            var res = this.Response;
            var req = this.Request;

            Dictionary<string, string> inputParameters = null;
            string body = null;

            // TODO: log remote IP with exception and associate with request itself?
            var remoteIpAddress = this.HttpContext.Features.Get<IHttpConnectionFeature>()?.RemoteIpAddress.ToString();
            var referer = req.Headers["Referer"].FirstOrDefault();
            var appTitle = req.Headers["App-Title"].FirstOrDefault();

            var isPOST = req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

            var syncIOFeature = HttpContext.Features.Get<IHttpBodyControlFeature>();
            if (syncIOFeature != null)
            {
                syncIOFeature.AllowSynchronousIO = true;
            }

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

            (var result, var routineExecutionMetric, var mayAccess) = ExecuteRoutine(execOptions, inputParameters, req.Headers, referer, remoteIpAddress, appTitle, out var responseHeaders);

            if (responseHeaders != null && responseHeaders.Count > 0)
            {
                foreach (var kv in responseHeaders)
                {
                    res.Headers[kv.Key] = kv.Value;
                }
            }

            if (mayAccess != null && !mayAccess.isSuccess)
            {
                res.ContentType = "text/plain";
                res.StatusCode = 403;
                return this.Content(mayAccess.userErrorVal);
            }

            // TODO: Only output this if "debug mode" is enabled on the jsDALServer Config (so will come through as a debug=1 or something parameter)
            if (routineExecutionMetric != null)
            {
                res.Headers.Add("Server-Timing", routineExecutionMetric.GetServerTimeHeader());
            }

            return Ok(result);
        }
        // // private IActionResult execOLD(ExecOptions execOptions)
        // // {
        // //     var debugInfo = "";
        // //     var res = this.Response;
        // //     var req = this.Request;

        // //     string appTitle = null;

        // //     Project project = null;
        // //     Application app = null;
        // //     Endpoint endpoint = null;

        // //     List<jsDALPlugin> pluginList = null;

        // //     // record client info? IP etc? Record other interestsing info like Connection and DbSource used -- maybe only for the realtime connections? ... or metrics should be against connection at least?
        // //     RoutineExecution routineExecutionMetric = null;

        // //     string remoteIpAddress = null;

        // //     try
        // //     {
        // //         if (!ControllerHelper.GetProjectAndAppAndEndpoint(execOptions.project, execOptions.application, execOptions.endpoint, out project, out app, out endpoint, out var resp))
        // //         {
        // //             return Ok(resp);
        // //         }

        // //         // TODO: log remote IP with exception and associate with request itself?
        // //         remoteIpAddress = this.HttpContext.Features.Get<IHttpConnectionFeature>()?.RemoteIpAddress.ToString();

        // //         routineExecutionMetric = ExecTracker.Begin(endpoint.Id, execOptions.schema, execOptions.routine);

        // //         appTitle = req.Headers["App-Title"];

        // //         // always start off not caching whatever we send back
        // //         res.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
        // //         res.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
        // //         res.Headers["Content-Type"] = "application/json";

        // //         var isPOST = req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

        // //         debugInfo += $"[{execOptions.schema}].[{execOptions.routine}]";

        // //         // make sure the source domain/IP is allowed access
        // //         var mayAccess = app.MayAccessDbSource("this.Request");

        // //         if (!mayAccess.isSuccess)
        // //         {
        // //             res.ContentType = "text/plain";
        // //             res.StatusCode = 403;
        // //             return this.Content(mayAccess.userErrorVal);
        // //         }

        // //         string body = null;
        // //         Dictionary<string, string> inputParameters = null;
        // //         Dictionary<string, dynamic> outputParameters;
        // //         int commandTimeOutInSeconds = 60;

        // //         if (execOptions.OverridingInputParameters != null)
        // //         {
        // //             inputParameters = execOptions.OverridingInputParameters;
        // //         }
        // //         else
        // //         {
        // //             if (isPOST)
        // //             {
        // //                 using (var sr = new StreamReader(req.Body))
        // //                 {
        // //                     body = sr.ReadToEnd();

        // //                     inputParameters = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
        // //                 }
        // //             }
        // //             else
        // //             {
        // //                 inputParameters = req.Query.ToDictionary(t => t.Key, t => t.Value.ToString());
        // //             }
        // //         }

        // //         if (inputParameters == null) inputParameters = new Dictionary<string, string>();

        // //         execOptions.inputParameters = inputParameters;

        // //         // PLUGINS
        // //         var pluginsInitMetric = routineExecutionMetric.BeginChildStage("Init plugins");

        // //         pluginList = InitPlugins(app, inputParameters);

        // //         pluginsInitMetric.End();

        // //         var execRoutineQueryMetric = routineExecutionMetric.BeginChildStage("execRoutineQuery");

        // //         int rowsAffected;

        // //         ///////////////////
        // //         // Database call
        // //         ///////////////////

        // //         var executionResult = OrmDAL.ExecRoutineQuery(execOptions.type,
        // //             execOptions.schema,
        // //             execOptions.routine,
        // //             endpoint,
        // //             inputParameters,
        // //             null,
        // //             pluginList,
        // //             commandTimeOutInSeconds,
        // //             out outputParameters,
        // //             execRoutineQueryMetric,
        // //             out rowsAffected
        // //         );

        // //         execRoutineQueryMetric.End();

        // //         var prepareResultsMetric = routineExecutionMetric.BeginChildStage("Prepare results");

        // //         if (!string.IsNullOrEmpty(executionResult.userError))
        // //         {
        // //             return Ok(ApiResponse.ExclamationModal(executionResult.userError));
        // //         }

        // //         var retVal = (IDictionary<string, object>)new System.Dynamic.ExpandoObject();
        // //         var ret = ApiResponse.Payload(retVal);

        // //         retVal.Add("OutputParms", outputParameters);

        // //         if (outputParameters != null)
        // //         { // TODO: Consider making this a plugin
        // //             var possibleUEParmNames = (new string[] { "usererrormsg", "usrerrmsg", "usererrormessage", "usererror", "usererrmsg" }).ToList();

        // //             var ueKey = outputParameters.Keys.FirstOrDefault(k => possibleUEParmNames.Contains(k.ToLower()));

        // //             // if a user error msg is defined.
        // //             if (!string.IsNullOrWhiteSpace(ueKey) && !string.IsNullOrWhiteSpace(outputParameters[ueKey]))
        // //             {
        // //                 ret.Message = outputParameters[ueKey];
        // //                 ret.Title = "Action failed";
        // //                 ret.Type = ApiResponseType.ExclamationModal;
        // //             }
        // //         }

        // //         if (execOptions.type == ExecType.Query)
        // //         {
        // //             var dataSet = executionResult.DataSet;
        // //             var dataContainers = dataSet.ToJsonDS();

        // //             var keys = dataContainers.Keys.ToList();

        // //             for (var i = 0; i < keys.Count; i++)
        // //             {
        // //                 retVal.Add(keys[i], dataContainers[keys[i]]);
        // //             }

        // //             retVal.Add("HasResultSets", keys.Count > 0);
        // //             retVal.Add("ResultSetKeys", keys.ToArray());
        // //         }
        // //         else if (execOptions.type == ExecType.NonQuery)
        // //         {

        // //         }
        // //         else if (execOptions.type == ExecType.Scalar)
        // //         {

        // //             if (executionResult.ScalarValue is DateTime)
        // //             {
        // //                 var dt = (DateTime)executionResult.ScalarValue;

        // //                 // convert to Javascript Date ticks
        // //                 var ticks = dt.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        // //                 ret = ApiResponseScalar.Payload(ticks, true);
        // //             }
        // //             else
        // //             {
        // //                 ret = ApiResponse.Payload(executionResult.ScalarValue);
        // //             }
        // //         }

        // //         prepareResultsMetric.End();
        // //         routineExecutionMetric.End(rowsAffected);

        // //         // TODO: Only output this if "debug mode" is enabled on the jsDALServer Config (so will come through as a debug=1 or something parameter)
        // //         this.Response.Headers.Add("Server-Timing", routineExecutionMetric.GetServerTimeHeader());

        // //         return Ok(ret);
        // //     }
        // //     catch (Exception ex)
        // //     {
        // //         if (routineExecutionMetric != null)
        // //         {
        // //             routineExecutionMetric.Exception(ex);
        // //         }

        // //         Connection dbConn = null;

        // //         if (endpoint != null)
        // //         {
        // //             // TODO: Fix!
        // //             dbConn = endpoint.GetSqlConnection();

        // //             if (debugInfo == null) debugInfo = "";

        // //             if (dbConn != null)
        // //             {
        // //                 debugInfo = $"{ endpoint.Pedigree } - { dbConn.InitialCatalog } - { debugInfo }";
        // //             }
        // //             else
        // //             {
        // //                 debugInfo = $"{ endpoint.Pedigree } - (no connection) - { debugInfo }";
        // //             }

        // //         }

        // //         var exceptionResponse = ApiResponse.ExecException(ex, execOptions, debugInfo, appTitle);

        // //         if (pluginList != null)
        // //         {
        // //             string externalRef;

        // //             if (dbConn != null)
        // //             {
        // //                 using (var con = new SqlConnection(dbConn.ConnectionStringDecrypted))
        // //                 {
        // //                     try
        // //                     {
        // //                         con.Open();
        // //                         ProcessPluginExectionExceptionHandlers(pluginList, con, ex, out externalRef);
        // //                         ((dynamic)exceptionResponse.Data).ExternalRef = externalRef;
        // //                     }
        // //                     catch (Exception e)
        // //                     {
        // //                         SessionLog.Exception(e);
        // //                     }

        // //                 }
        // //             } // else: TODO: Log fact that we dont have a proper connection string.. or should plugins handle that?
        // //         }

        // //         // return it as "200 (Ok)" because the exception has been handled
        // //         return Ok(exceptionResponse);
        // //         //return BadRequest(exceptionResponse);
        // //     }

        // // }

        public static (ApiResponse, RoutineExecution, CommonReturnValue) ExecuteRoutine(ExecOptions execOptions, Dictionary<string, string> inputParameters,
        Microsoft.AspNetCore.Http.IHeaderDictionary requestHeaders, string referer, string remoteIpAddress, string appTitle, out Dictionary<string, string> responseHeaders)
        {
            var debugInfo = "";

            Project project = null;
            Application app = null;
            Endpoint endpoint = null;
            responseHeaders = null;

            List<ExecutionPlugin> pluginList = null;

            // record client info? IP etc? Record other interestsing info like Connection and DbSource used -- maybe only for the realtime connections? ... or metrics should be against connection at least?
            RoutineExecution routineExecutionMetric = null;

            try
            {
                if (!ControllerHelper.GetProjectAndAppAndEndpoint(execOptions.project, execOptions.application, execOptions.endpoint, out project, out app, out endpoint, out var resp))
                {
                    return (resp, null, null);
                }

                routineExecutionMetric = ExecTracker.Begin(endpoint.Id, execOptions.schema, execOptions.routine);

                debugInfo += $"[{execOptions.schema}].[{execOptions.routine}]";

                // make sure the source domain/IP is allowed access
                var mayAccess = app.MayAccessDbSource(referer);

                if (!mayAccess.isSuccess) return (null, null, mayAccess);


                string body = null;
                //Dictionary<string, string> inputParameters = null;
                Dictionary<string, dynamic> outputParameters;
                int commandTimeOutInSeconds = 60;

                if (execOptions.OverridingInputParameters != null)
                {
                    inputParameters = execOptions.OverridingInputParameters;
                }

                if (inputParameters == null) inputParameters = new Dictionary<string, string>();

                execOptions.inputParameters = inputParameters;

                // PLUGINS
                var pluginsInitMetric = routineExecutionMetric.BeginChildStage("Init plugins");

                pluginList = InitPlugins(app, inputParameters);

                pluginsInitMetric.End();

                var execRoutineQueryMetric = routineExecutionMetric.BeginChildStage("execRoutineQuery");

                int rowsAffected;

                ///////////////////
                // Database call
                ///////////////////
                var executionResult = OrmDAL.ExecRoutineQuery(
                    execOptions.type,
                    execOptions.schema,
                    execOptions.routine,
                    endpoint,
                    inputParameters,
                    requestHeaders,
                    remoteIpAddress,
                    pluginList,
                    commandTimeOutInSeconds,
                    out outputParameters,
                    execRoutineQueryMetric,
                    out responseHeaders,
                    out rowsAffected
                );

                execRoutineQueryMetric.End();

                var prepareResultsMetric = routineExecutionMetric.BeginChildStage("Prepare results");

                if (!string.IsNullOrEmpty(executionResult.userError))
                {
                    return (ApiResponse.ExclamationModal(executionResult.userError), routineExecutionMetric, mayAccess);
                }

                var retVal = (IDictionary<string, object>)new System.Dynamic.ExpandoObject();
                var ret = ApiResponse.Payload(retVal);

                retVal.Add("OutputParms", outputParameters);

                if (outputParameters != null)
                { // TODO: Consider making this a plugin
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

                if (execOptions.type == ExecType.Query)
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
                else if (execOptions.type == ExecType.NonQuery)
                {

                }
                else if (execOptions.type == ExecType.Scalar)
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
                routineExecutionMetric.End(rowsAffected);

                return (ret, routineExecutionMetric, mayAccess);
            }
            catch (Exception ex)
            {
                if (routineExecutionMetric != null)
                {
                    routineExecutionMetric.Exception(ex);
                }

                Connection dbConn = null;

                if (endpoint != null)
                {
                    // TODO: Fix!
                    dbConn = endpoint.GetSqlConnection();

                    if (debugInfo == null) debugInfo = "";

                    if (dbConn != null)
                    {
                        debugInfo = $"{ endpoint.Pedigree } - { dbConn.InitialCatalog } - { debugInfo }";
                    }
                    else
                    {
                        debugInfo = $"{ endpoint.Pedigree } - (no connection) - { debugInfo }";
                    }

                }

                var exceptionResponse = ApiResponse.ExecException(ex, execOptions, debugInfo, appTitle);

                // TODO: Get Execution plugin list specifically
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
                                ProcessPluginExecutionExceptionHandlers(pluginList, con, ex, out externalRef);
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
                return (exceptionResponse, routineExecutionMetric, null);
            }
        }

        private static void ProcessPluginExecutionExceptionHandlers(List<ExecutionPlugin> pluginList, SqlConnection con, Exception ex, out string externalRef)
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

        private static MethodInfo initPluginMethod = typeof(ExecutionPlugin).GetMethod("InitPlugin", BindingFlags.NonPublic | BindingFlags.Instance);
        private static List<ExecutionPlugin> InitPlugins(Application app, Dictionary<string, string> queryString)
        {
            var plugins = new List<ExecutionPlugin>();

            if (PluginManager.PluginAssemblies != null && app.Plugins != null)
            {
                foreach (string pluginGuid in app.Plugins)
                {
                    var plugin = PluginManager.PluginAssemblies.SelectMany(kv => kv.Value).FirstOrDefault(p => p.Guid.ToString().Equals(pluginGuid, StringComparison.OrdinalIgnoreCase));

                    if (plugin != null)
                    {
                        try
                        {
                            var concrete = (ExecutionPlugin)plugin.Assembly.CreateInstance(plugin.TypeInfo.FullName);

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

        public static (SqlDbType, string) GetSqlDbTypeFromParameterType(string parameterDataType)
        {
            string defUdtType = null;

            switch (parameterDataType.ToLower())
            {
                case "date":
                    return (SqlDbType.Date, defUdtType);
                case "datetime":
                    return (SqlDbType.DateTime, defUdtType);
                case "time":
                    return (SqlDbType.VarChar, defUdtType); // send as a simple string and let SQL take care of it
                case "smalldatetime":
                    return (SqlDbType.SmallDateTime, defUdtType);
                case "int":
                    return (SqlDbType.Int, defUdtType);
                case "smallint":
                    return (SqlDbType.SmallInt, defUdtType);
                case "bigint":
                    return (SqlDbType.BigInt, defUdtType);
                case "bit":
                    return (SqlDbType.Bit, defUdtType);
                case "nvarchar":
                    return (SqlDbType.NVarChar, defUdtType);
                case "varchar":
                    return (SqlDbType.VarChar, defUdtType);
                case "text":
                    return (SqlDbType.Text, defUdtType);
                case "ntext":
                    return (SqlDbType.NText, defUdtType);
                case "varbinary":
                    return (SqlDbType.VarBinary, defUdtType);
                case "decimal":
                    return (SqlDbType.Decimal, defUdtType);
                case "uniqueidentifier":
                    return (SqlDbType.UniqueIdentifier, defUdtType);
                case "money":
                    return (SqlDbType.Money, defUdtType);
                case "char":
                    return (SqlDbType.Char, defUdtType);
                case "nchar":
                    return (SqlDbType.NChar, defUdtType);
                case "xml":
                    return (SqlDbType.Xml, defUdtType);
                case "float":
                    return (SqlDbType.Float, defUdtType);
                case "image":
                    return (SqlDbType.Image, defUdtType);
                case "tinyint":
                    return (SqlDbType.TinyInt, defUdtType);
                case "geography":
                    return (SqlDbType.Udt, "GEOGRAPHY");
                case "geometry":
                    return (SqlDbType.Udt, "GEOMETRY");
                default:
                    throw new NotSupportedException("GetSqlDbTypeFromParameterType::Unsupported data type: " + parameterDataType);
            }
        }
    }
}