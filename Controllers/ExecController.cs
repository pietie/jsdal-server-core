using System;
using System.Collections.Generic;
using System.Data;
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
using System.Text.RegularExpressions;
using jsdal_server_core.Performance.DataCollector;
using Microsoft.Data.SqlClient;

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
            public string project { get; set; }
            public string application { get; set; }
            public string endpoint { get; set; }

            public string schema { get; set; }
            public string routine { get; set; }
            public ExecType type { get; set; }

            [JsonIgnore]
            public Dictionary<string, string> OverridingInputParameters { get; set; }

            public Dictionary<string, string> inputParameters { get; set; }

            // matches input with various versions of schema + routine
            public bool? MatchRoutine(string input)
            {
                string m1 = routine;
                string m2 = schema;
                string m3 = $"{schema}.{routine}";
                string m4 = $"[{schema}].{routine}";
                string m5 = $"{schema}.[{routine}]";
                string m6 = $"[{schema}].[{routine}]";

                return m1.Contains(input, StringComparison.OrdinalIgnoreCase)
                    || m2.Contains(input, StringComparison.OrdinalIgnoreCase)
                    || m3.Contains(input, StringComparison.OrdinalIgnoreCase)
                    || m4.Contains(input, StringComparison.OrdinalIgnoreCase)
                    || m5.Contains(input, StringComparison.OrdinalIgnoreCase)
                    || m6.Contains(input, StringComparison.OrdinalIgnoreCase);
            }
        }


        public enum ExecType
        {
            Query = 0,
            NonQuery = 1,
            Scalar = 2,
            ServerMethod = 10,
            BackgroundThread = 20
        }

        [AllowAnonymous]
        [HttpGet("/api/execnq/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/execnq/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public IActionResult execNonQuery([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            ExecOptions execOptions = null;

            try
            {
                execOptions = new ExecOptions() { project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.NonQuery };
                return exec(execOptions);
            }
            catch (Exception ex)
            {
                var requestHeaders = this.Request.Headers.Select(kv => new { kv.Key, Value = kv.Value.FirstOrDefault() }).ToDictionary(kv => kv.Key, kv => kv.Value);

                var appTitle = requestHeaders.Val("app-title");
                var appVersion = requestHeaders.Val("app-ver");

                var exceptionResponse = ApiResponse.ExecException(ex, execOptions, out var exceptionId, null, appTitle, appVersion);

                return Ok(exceptionResponse);
            }
        }

        [AllowAnonymous]
        [HttpGet("/api/exec/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/exec/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public IActionResult execQuery([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            ExecOptions execOptions = null;

            try
            {
                execOptions = new ExecOptions() { project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.Query };
                return exec(execOptions);
            }
            catch (Exception ex)
            {
                var requestHeaders = this.Request.Headers.Select(kv => new { kv.Key, Value = kv.Value.FirstOrDefault() }).ToDictionary(kv => kv.Key, kv => kv.Value);

                var appTitle = requestHeaders.Val("app-title");
                var appVersion = requestHeaders.Val("app-ver");

                var exceptionResponse = ApiResponse.ExecException(ex, execOptions, out var exceptionId, null, appTitle, appVersion);

                return Ok(exceptionResponse);
            }
        }

        [AllowAnonymous]
        [HttpGet("/api/execScalar/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/execScalar/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public IActionResult Scalar([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            ExecOptions execOptions = null;

            try
            {
                execOptions = new ExecOptions() { project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.Scalar };
                return exec(execOptions);

            }
            catch (Exception ex)
            {
                var requestHeaders = this.Request.Headers.Select(kv => new { kv.Key, Value = kv.Value.FirstOrDefault() }).ToDictionary(kv => kv.Key, kv => kv.Value);

                var appTitle = requestHeaders.Val("app-title");
                var appVersion = requestHeaders.Val("app-ver");

                var exceptionResponse = ApiResponse.ExecException(ex, execOptions, out var exceptionId, null, appTitle, appVersion);

                return Ok(exceptionResponse);
            }
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

                var blobData = BlobStore.Get(blobRef);

                var client = new System.Net.Http.HttpClient();

                using (var content = new System.Net.Http.ByteArrayContent(blobData.Data))
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
                        SessionLog.Error("Barcode failed. postUrl = {0}; contentLength: {1}; responseText={2}", postUrl ?? "(null)", blobData?.Data?.Length ?? -1, responseText ?? "(null)");

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
        //[RequestSizeLimit(40000000)]
        public IActionResult PrepareBlob()
        {
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
                    var data = new byte[file.Length];

                    using (var stream = file.OpenReadStream())
                    {
                        stream.Read(data, 0, data.Length);
                    }

                    BlobStore.Instance.Add(new BlobStoreData() { Filename = file.FileName, Data = data, ContentType = file.ContentType }, out var id);
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
        [HttpGet("/api/blob/serve/{ref}")]
        public IActionResult ServeBlob([FromRoute(Name = "ref")] string blobRef)
        {
            try
            {
                if (!BlobStore.Exists(blobRef)) return NotFound("Invalid or expired blob ref");
                var blob = BlobStore.Get(blobRef);

                return new FileContentResult(blob.Data, blob.ContentType);
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
                // always start off not caching whatever we send back
                res.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                res.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
                res.Headers["Content-Type"] = "application/json";

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


        public static int ExceutionsInFlight = 0;
        private IActionResult exec(ExecOptions execOptions)
        {
            try
            {
                Interlocked.Increment(ref ExceutionsInFlight);

                var res = this.Response;
                var req = this.Request;

                string body = null;

                // always start off not caching whatever we send back
                res.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                res.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
                res.Headers["Content-Type"] = "application/json";
                res.Headers["Expires"] = "-1";

                // TODO: log remote IP with exception and associate with request itself?
                var remoteIpAddress = this.HttpContext.Features.Get<IHttpConnectionFeature>()?.RemoteIpAddress.ToString();

                //   var remoteIpAddress2 = req.HttpContext.Connection?.RemoteIpAddress?.ToString() ?? "";

                // convert request headers to normal Dictionary
                var requestHeaders = req.Headers.Select(kv => new { kv.Key, Value = kv.Value.FirstOrDefault() }).ToDictionary(kv => kv.Key, kv => kv.Value);

                var referer = requestHeaders.Val("Referer");
                var appTitle = requestHeaders.Val("app-title");
                var appVersion = requestHeaders.Val("app-ver");

                var isPOST = req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

                if (isPOST)
                {
                    using (var sr = new StreamReader(req.Body))
                    {
                        body = sr.ReadToEnd();

                        execOptions.inputParameters = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                    }
                }
                else
                {
                    execOptions.inputParameters = req.Query.ToDictionary(t => t.Key, t => t.Value.ToString());
                }

                if (execOptions.OverridingInputParameters != null)
                {
                    execOptions.inputParameters = execOptions.OverridingInputParameters;
                }

                if (execOptions.inputParameters == null) execOptions.inputParameters = new Dictionary<string, string>();


                (var result, var routineExecutionMetric, var mayAccess) = ExecuteRoutine(execOptions, requestHeaders,
                    referer, remoteIpAddress,
                    appTitle, appVersion, out var responseHeaders);

                if (responseHeaders != null && responseHeaders.Count > 0)
                {
                    foreach (var kv in responseHeaders)
                    {
                        res.Headers[kv.Key] = kv.Value;
                    }
                }

                if (mayAccess != null && !mayAccess.IsSuccess)
                {
                    res.ContentType = "text/plain";
                    res.StatusCode = 403; // Forbidden
                    return this.Content(mayAccess.userErrorVal);
                }

                // TODO: Only output this if "debug mode" is enabled on the jsDALServer Config (so will come through as a debug=1 or something parameter/header)
                if (routineExecutionMetric != null)
                {
                    res.Headers.Add("Server-Timing", routineExecutionMetric.GetServerTimeHeader());
                }

                if (result is IActionResult) return result as IActionResult;

                return Ok(result);
            }
            finally
            {
                Interlocked.Decrement(ref ExceutionsInFlight);
            }
        }

        public static (object/*ApiResponse | IActionResult*/, RoutineExecution, CommonReturnValue) ExecuteRoutine(ExecOptions execOptions,
            Dictionary<string, string> requestHeaders,
            string referer,
            string remoteIpAddress,
            string appTitle,
            string appVersion,
            out Dictionary<string, string> responseHeaders)
        {
            string debugInfo = null;

            Project project = null;
            Application app = null;
            Endpoint endpoint = null;
            responseHeaders = null;

            List<ExecutionPlugin> pluginList = null;

            RoutineExecution routineExecutionMetric = null;

            responseHeaders = new Dictionary<string, string>();

            try
            {
                if (!ControllerHelper.GetProjectAndAppAndEndpoint(execOptions.project, execOptions.application, execOptions.endpoint, out project, out app, out endpoint, out var resp))
                {
                    return (resp, null, null);
                }

                routineExecutionMetric = new RoutineExecution(endpoint, execOptions.schema, execOptions.routine);

                RealtimeTrackerThread.Instance.Enqueue(routineExecutionMetric);

                //  debugInfo += $"[{execOptions.schema}].[{execOptions.routine}]";

                string jsDALApiKey = null;

                if (requestHeaders.ContainsKey("api-key"))
                {
                    jsDALApiKey = requestHeaders["api-key"];
                }

                // make sure the source domain/IP is allowed access
                var mayAccess = app.MayAccessDbSource(referer, jsDALApiKey);

                if (!mayAccess.IsSuccess) return (null, null, mayAccess);


                Dictionary<string, dynamic> outputParameters;
                int commandTimeOutInSeconds = 60;

                // PLUGINS
                var pluginsInitMetric = routineExecutionMetric.BeginChildStage("Init plugins");

                pluginList = InitPlugins(app, execOptions.inputParameters, requestHeaders);

                pluginsInitMetric.End();

                ////////////////////
                // Auth stage
                ///////////////////
                { // ask all ExecPlugins to authenticate
                    foreach (var plugin in pluginList)
                    {
                        if (!plugin.IsAuthenticated(execOptions.schema, execOptions.routine, out var error))
                        {
                            responseHeaders.Add("Plugin-AuthFailed", plugin.Name);
                            return (new UnauthorizedObjectResult(error), null, null);
                        }
                    }
                }

                var execRoutineQueryMetric = routineExecutionMetric.BeginChildStage("execRoutineQuery");

                string dataCollectorEntryShortId = DataCollectorThread.Enqueue(endpoint, execOptions);

                int rowsAffected;

                ///////////////////
                // Database call
                ///////////////////

                OrmDAL.ExecutionResult executionResult = null;

                try
                {
                    executionResult = OrmDAL.ExecRoutineQuery(
                       execOptions.type,
                       execOptions.schema,
                       execOptions.routine,
                       endpoint,
                       execOptions.inputParameters,
                       requestHeaders,
                       remoteIpAddress,
                       pluginList,
                       commandTimeOutInSeconds,
                       out outputParameters,
                       execRoutineQueryMetric,
                       ref responseHeaders,
                       out rowsAffected
                   );

                    execRoutineQueryMetric.End();

                    ulong? rows = null;

                    if (rowsAffected >= 0) rows = (ulong)rowsAffected;

                    DataCollectorThread.End(dataCollectorEntryShortId, rowsAffected: rows,
                                                                     durationInMS: execRoutineQueryMetric.DurationInMS,
                                                                     bytesReceived: executionResult.BytesReceived,
                                                                     networkServerTimeMS: executionResult.NetworkServerTimeInMS);
                }
                catch (Exception execEx)
                {
                    DataCollectorThread.End(dataCollectorEntryShortId, ex: execEx);
                    throw; // rethrow
                }

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
                    // nothing to do 
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

                // enqueue a second time as we now have an End date and rowsAffected
                RealtimeTrackerThread.Instance.Enqueue(routineExecutionMetric);

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

                    // if (debugInfo == null) debugInfo = "";

                    // if (dbConn != null)
                    // {
                    //     debugInfo = $"{ endpoint.Pedigree } - { dbConn.InitialCatalog } - { debugInfo }";
                    // }
                    // else
                    // {
                    //     debugInfo = $"{ endpoint.Pedigree } - (no connection) - { debugInfo }";
                    // }

                }

                var exceptionResponse = ApiResponse.ExecException(ex, execOptions, out var exceptionId, debugInfo, appTitle, appVersion);

                if (debugInfo == null) debugInfo = "";
                debugInfo = exceptionId + " " + debugInfo;

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
                                string additionalInfo = debugInfo;

                                if (execOptions?.inputParameters != null)
                                {
                                    additionalInfo += " ";
                                    additionalInfo += string.Join(",", execOptions.inputParameters.Select(kv => $"{kv.Key}={kv.Value}").ToArray());
                                }

                                con.Open();
                                ProcessPluginExecutionExceptionHandlers(pluginList, con, ex, additionalInfo, appTitle, appVersion, out externalRef);
                                ((dynamic)exceptionResponse.Data).ExternalRef = externalRef;
                            }
                            catch (Exception e)
                            {
                                ExceptionLogger.LogException(e, "ProcessPluginExecutionExceptionHandlers", "jsdal-server");
                            }

                        }
                    } // else: TODO: Log fact that we dont have a proper connection string.. or should plugins handle that?
                }

                // return it as "200 (Ok)" because the exception has been handled
                return (exceptionResponse, routineExecutionMetric, null);
            }
        }

        private static void ProcessPluginExecutionExceptionHandlers(List<ExecutionPlugin> pluginList,
                        SqlConnection con,
                        Exception ex,
                        string additionalInfo,
                        string appTitle,
                        string appVersion,
                        out string externalRef)
        {
            externalRef = null;
            if (pluginList == null) return;

            foreach (var plugin in pluginList)
            {
                try
                {
                    string msg = null;
                    string externalRefTmp = null;

                    plugin.OnExecutionException(con, ex, additionalInfo, appTitle, appVersion, out externalRefTmp, out msg);

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

        // TODO: Consider reworking this - ExecPlugins should only get instantiated once?! The assembly is instantiated once so maybe creating new instances of the plugin class is not that bad?
        private static List<ExecutionPlugin> InitPlugins(Application app, Dictionary<string, string> queryString, Dictionary<string, string> requestHeaders)
        {
            var concretePlugins = new List<ExecutionPlugin>();

            if (PluginLoader.Instance.PluginAssemblies != null && app.Plugins != null)
            {
                foreach (string pluginGuid in app.Plugins)
                {
                    var plugin = PluginLoader.Instance
                                                .PluginAssemblies
                                                .SelectMany(a => a.Plugins)
                                                .Where(p=>p.Type == PluginType.Execution)
                                                .FirstOrDefault(p => p.Guid.ToString().Equals(pluginGuid, StringComparison.OrdinalIgnoreCase));

                    if (plugin != null)
                    {
                        try
                        {
                            var concrete = (ExecutionPlugin)plugin.Assembly.CreateInstance(plugin.TypeInfo.FullName);

                            initPluginMethod.Invoke(concrete, new object[] { queryString, requestHeaders });

                            concretePlugins.Add(concrete);
                        }
                        catch (Exception ex)
                        {
                            SessionLog.Error("Failed to instantiate '{0}' ({1}) on assembly '{2}'", plugin.TypeInfo.FullName, pluginGuid, plugin.Assembly.FullName);
                            SessionLog.Exception(ex);
                            ExceptionLogger.LogExceptionThrottled(ex,"ExecController::InitPlugins", 2);
                        }
                    }
                }
            }

            return concretePlugins;
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
                case "timestamp":
                    return (SqlDbType.Timestamp, defUdtType);
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