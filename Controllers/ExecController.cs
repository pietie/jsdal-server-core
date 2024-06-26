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
using System.Diagnostics;

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

            public int? CommandTimeoutInSecs { get; set; } = null;

            [JsonIgnore]
            public Dictionary<string, string> OverridingInputParameters { get; set; }

            public Dictionary<string, string>? inputParameters { get; set; }

            [JsonIgnore]
            [LiteDB.BsonIgnore]
            public CancellationToken CancellationToken { get; set; }

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

        [Authorize]
        [HttpGet("/api/execnq-winauth/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/execnq-winauth/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public async Task<IActionResult> execNonQueryWinAuth(CancellationToken cancellationToken, [FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            return await execNonQuery(cancellationToken, project, app, endpoint, schema, routine);
        }

        [AllowAnonymous]
        [HttpGet("/api/execnq/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/execnq/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public async Task<IActionResult> execNonQuery(CancellationToken cancellationToken, [FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            ExecOptions? execOptions = null;

            try
            {
                execOptions = new ExecOptions() { CancellationToken = cancellationToken, project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.NonQuery };
                return await execAsync(execOptions);
            }
            catch (OperationCancelledByUserException)
            {
                return Ok();
            }
            catch (TaskCanceledException)
            {
                return Ok();
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

        [Authorize]
        [HttpGet("/api/exec-winauth/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/exec-winauth/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public async Task<IActionResult> execQueryWinAuth(CancellationToken cancellationToken, [FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            return await execQuery(cancellationToken, project, app, endpoint, schema, routine);
        }
        [AllowAnonymous]
        [HttpGet("/api/exec/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/exec/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public async Task<IActionResult> execQuery(CancellationToken cancellationToken, [FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            ExecOptions execOptions = null;

            try
            {
                execOptions = new ExecOptions() { CancellationToken = cancellationToken, project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.Query };
                return await execAsync(execOptions);
            }
            catch (OperationCancelledByUserException)
            {
                return Ok();
            }
            catch (TaskCanceledException)
            {
                return Ok();
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


        [Authorize]
        [HttpGet("/api/execScalar-winauth/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/execScalar-winauth/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public async Task<IActionResult> ScalarWinAuth(CancellationToken cancellationToken, [FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            return await Scalar(cancellationToken, project, app, endpoint, schema, routine);
        }
        [AllowAnonymous]
        [HttpGet("/api/execScalar/{project}/{app}/{endpoint}/{schema}/{routine}")]
        [HttpPost("/api/execScalar/{project}/{app}/{endpoint}/{schema}/{routine}")]
        public async Task<IActionResult> Scalar(CancellationToken cancellationToken, [FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string schema, [FromRoute] string routine)
        {
            ExecOptions execOptions = null;

            try
            {
                execOptions = new ExecOptions() { CancellationToken = cancellationToken, project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.Scalar };
                return await execAsync(execOptions);
            }
            catch (OperationCancelledByUserException)
            {
                return Ok();
            }
            catch (TaskCanceledException)
            {
                return Ok();
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

                // TODO: DO NOT create new HttpClient each time
                var client = new System.Net.Http.HttpClient();

                using (var content = new System.Net.Http.ByteArrayContent(blobData.Data))
                {
                    var barcodeServiceUrl = this.config["AppSettings:BarcodeService.URL"]?.TrimEnd('/');
                    var postUrl = $"{barcodeServiceUrl}/scan/pdf417?raw={raw}&veh={veh}&drv={drv}";

                    var response = await client.PostAsync(postUrl, content);

                    var responseText = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        var json = JsonConvert.DeserializeObject(responseText);

                        return Ok(ApiResponse.Payload(json!));
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

                    using (var waitToCompleteEvent = new ManualResetEvent(false))
                    {
                        foreach (BatchData batchItem in batchDataCollection)
                        {
                            // TODO: Instead of ThreadPool can we just queue up the awaits and use Task.WaitAll/WaitAny or whatever
                            ThreadPool.QueueUserWorkItem(async (state) =>
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

                                    var ret = (await execAsync(new ExecOptions()
                                    {
                                        project = batchItem.Routine.project,
                                        application = batchItem.Routine.application,
                                        endpoint = batchItem.Routine.endpoint,
                                        schema = batchItem.Routine.schema,
                                        routine = batchItem.Routine.routine,
                                        OverridingInputParameters = inputParameters,
                                        type = ExecType.Query

                                    })) as Microsoft.AspNetCore.Mvc.ObjectResult;

                                    if (ret != null)
                                    {
                                        var apiResponse = ret.Value as ApiResponse;

                                        lock (responses)
                                        {
                                            responses.Add(batchItem.Ix, apiResponse);
                                        }
                                    }
                                }
                                catch (OperationCancelledByUserException)
                                {

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
        private async Task<IActionResult> execAsync(ExecOptions execOptions)
        {
            try
            {
                Interlocked.Increment(ref ExceutionsInFlight);

                var res = this.Response;
                var req = this.Request;

                string? body = null;

                // always start off not caching whatever we send back
                res.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                res.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
                res.Headers["Content-Type"] = "application/json";
                res.Headers["Expires"] = "-1";

                // TODO: log remote IP with exception and associate with request itself?
                var remoteIpAddress = this.HttpContext.Features.Get<IHttpConnectionFeature>()?.RemoteIpAddress?.ToString();

                //   var remoteIpAddress2 = req.HttpContext.Connection?.RemoteIpAddress?.ToString() ?? "";

                // convert request headers to normal Dictionary
                var requestHeaders = req.Headers.Select(kv => new { kv.Key, Value = kv.Value.FirstOrDefault() }).ToDictionary(kv => kv.Key, kv => kv.Value);

                var referer = requestHeaders.Val("Referer");
                var appTitle = requestHeaders.Val("app-title");
                var appVersion = requestHeaders.Val("app-ver");

                if (int.TryParse(requestHeaders.Val("cmdtimeoutsecs"), out var n))
                {
                    execOptions.CommandTimeoutInSecs = n;
                }

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

                //(var result, var routineExecutionMetric, var mayAccess)
                // out var responseHeaders

                var result = await ExecuteRoutineAsync(execOptions, requestHeaders, referer, remoteIpAddress, appTitle, appVersion, HttpContext);

                var responseHeaders = result.ResponseHeaders;

                if (responseHeaders != null && responseHeaders.Count > 0)
                {
                    foreach (var kv in responseHeaders)
                    {
                        res.Headers[kv.Key] = kv.Value;
                    }
                }

                if ((result?.MayAccess != null && !result.MayAccess.IsSuccess))
                {
                    res.ContentType = "text/plain";
                    res.StatusCode = 403; // Forbidden
                    return this.Content(result?.MayAccess?.userErrorVal);
                }

                // TODO: Only output this if "debug mode" is enabled on the jsDALServer Config (so will come through as a debug=1 or something parameter/header)
                if (result.RoutineExecutionMetric != null)
                {
                    res.Headers.Add("Server-Timing", result.RoutineExecutionMetric.GetServerTimeHeader());
                }

                if (result is IActionResult) return result as IActionResult;

                return Ok(result.ApiResponse);
            }
            catch (RoutineAccessSecurityException se)
            {
                return Unauthorized(se.Message);
            }
            finally
            {
                Interlocked.Decrement(ref ExceutionsInFlight);
            }
        }

        public class ExecuteRoutineAsyncResult
        {
            public ExecuteRoutineAsyncResult(object apiResponse, RoutineExecution execMetric, CommonReturnValue mayAccess, Dictionary<string, string> responseHeaders)
            {
                this.ApiResponse = apiResponse;
                this.RoutineExecutionMetric = execMetric;
                this.MayAccess = mayAccess;
                this.ResponseHeaders = responseHeaders;
            }
            public object ApiResponse { get; set; }
            public RoutineExecution RoutineExecutionMetric { get; set; }
            public CommonReturnValue MayAccess { get; set; }
            public Dictionary<string, string> ResponseHeaders { get; set; }
        }

        //(object/*ApiResponse | IActionResult*/, RoutineExecution, CommonReturnValue)
        public static async Task<ExecuteRoutineAsyncResult> ExecuteRoutineAsync(ExecOptions execOptions,
            Dictionary<string, string> requestHeaders,
            string referer,
            string remoteIpAddress,
            string appTitle,
            string appVersion,
            Microsoft.AspNetCore.Http.HttpContext httpContext
            )
        {
            string? debugInfo = null;

            Project? project = null;
            Application? app = null;
            Endpoint? endpoint = null;
            Dictionary<string, string>? responseHeaders = null;

            List<ExecutionPlugin>? pluginList = null;

            RoutineExecution? routineExecutionMetric = null;

            responseHeaders = new Dictionary<string, string>();

            try
            {
                if (!ControllerHelper.GetProjectAndAppAndEndpoint(execOptions.project, execOptions.application, execOptions.endpoint, out project,
                                                                    out app, out endpoint, out var resp))
                {
                    return new ExecuteRoutineAsyncResult(resp, null, null, responseHeaders);
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

                if (!mayAccess.IsSuccess) return new ExecuteRoutineAsyncResult(null, null, mayAccess, responseHeaders);

                Dictionary<string, dynamic>? outputParameters;
                int commandTimeOutInSeconds = 60;
                bool hasExplictCmdTimeout = false;

                if (execOptions.CommandTimeoutInSecs != null)
                {
                    commandTimeOutInSeconds = execOptions.CommandTimeoutInSecs.Value;
                    hasExplictCmdTimeout = true;
                }

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
                            return new ExecuteRoutineAsyncResult(new UnauthorizedObjectResult(error), null, null, responseHeaders);
                        }
                    }
                }

                ////////////////////
                // Execution Policy
                ////////////////////

                ExecutionPolicy? executionPolicy = null;

                executionPolicy = app.GetDefaultExecutionPolicy();

                if (requestHeaders.ContainsKey("exec-policy"))
                {
                    var pol = app.GetExecutionPolicyByName(requestHeaders["exec-policy"]);

                    if (pol != null)
                    {
                        executionPolicy = pol;
                    }
                    else
                    {
                        responseHeaders.Add("exec-policy-error", $"Policy '{requestHeaders["exec-policy"]}' not found");
                    }
                }

                var execRoutineQueryMetric = routineExecutionMetric.BeginChildStage("execRoutineQuery");

                string dataCollectorEntryShortId = DataCollectorThread.Enqueue(endpoint, execOptions);

                ///////////////////
                // Database call
                ///////////////////

                OrmDAL.ExecutionResult? executionResult = null;

                try
                {
                    executionResult = await OrmDAL.ExecRoutineQueryAsync(
                       execOptions.CancellationToken,
                       execOptions.type,
                       execOptions.schema,
                       execOptions.routine,
                       endpoint,
                       execOptions.inputParameters,
                       requestHeaders,
                       remoteIpAddress,
                       pluginList,
                       commandTimeOutInSeconds,
                       execRoutineQueryMetric,
                       responseHeaders,
                       executionPolicy,
                       hasExplictCmdTimeout,
                       httpContext
                   );

                    outputParameters = executionResult.OutputParameterDictionary;
                    responseHeaders = executionResult.ResponseHeaders;

                    execRoutineQueryMetric.End();

                    ulong? rows = null;

                    if (executionResult?.RowsAffected.HasValue ?? false) rows = (ulong)executionResult.RowsAffected.Value;

                    DataCollectorThread.End(dataCollectorEntryShortId, rowsAffected: rows,
                                                                     durationInMS: execRoutineQueryMetric.DurationInMS,
                                                                     bytesReceived: executionResult.BytesReceived,
                                                                     networkServerTimeMS: executionResult.NetworkServerTimeInMS);
                }
                catch (Exception execEx)
                {
                    DataCollectorThread.End(dataCollectorEntryShortId, ex: execEx);
                    throw;
                }

                var prepareResultsMetric = routineExecutionMetric.BeginChildStage("Prepare results");

                if (!string.IsNullOrEmpty(executionResult.userError))
                {
                    return new ExecuteRoutineAsyncResult(ApiResponse.ExclamationModal(executionResult.userError), routineExecutionMetric, mayAccess, responseHeaders);
                }

                var retVal = (IDictionary<string, object>)new System.Dynamic.ExpandoObject()!;
                var ret = ApiResponse.Payload(retVal);

                if (outputParameters != null)
                {
                    retVal.Add("OutputParms", outputParameters);
                }

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
                    if (executionResult.ReaderResults != null)
                    {
                        var keys = executionResult.ReaderResults.Keys.ToList();

                        for (var i = 0; i < keys.Count; i++)
                        {
                            retVal.Add(keys[i], executionResult.ReaderResults[keys[i]]);
                        }

                        retVal.Add("HasResultSets", keys.Count > 0);
                        retVal.Add("ResultSetKeys", keys.ToArray());
                    }
                    else
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
                routineExecutionMetric.End(executionResult.RowsAffected ?? 0);

                // enqueue a second time as we now have an End date and rowsAffected
                RealtimeTrackerThread.Instance.Enqueue(routineExecutionMetric);

                return new ExecuteRoutineAsyncResult(ret, routineExecutionMetric, mayAccess, responseHeaders);
            }
            catch (RoutineAccessSecurityException) { throw; }
            // catch (SqlException ex) when (execOptions.CancellationToken.IsCancellationRequested && ex.Number == 0 && ex.State == 0 && ex.Class == 11)
            // {
            //     // if we ended up here with a SqlException and a Cancel has been requested, we are very likely here because of the exception "Operation cancelled by user."
            //     // since MS does not provide an easy way (like a specific error code) to detect this scenario we have to guess

            //     routineExecutionMetric?.Exception(ex);

            //     throw new OperationCancelledByUserException(ex);
            // }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (JsDALExecutionException re) when (re.InnerException is SqlException se && execOptions.CancellationToken.IsCancellationRequested && se.Number == 0 && se.State == 0 && se.Class == 11)
            {

                // if we ended up here with a SqlException and a Cancel has been requested, we are very likely here because of the exception "Operation cancelled by user."
                // since MS does not provide an easy way (like a specific error code) to detect this scenario we have to guess

                routineExecutionMetric?.Exception(re);

                throw new OperationCancelledByUserException(re);
            }
            catch (JsDALExecutionException execEx)
            {
                routineExecutionMetric?.Exception(execEx);

                Connection dbConn = null;

                if (endpoint != null)
                {
                    // TODO: Fix!
                    dbConn = endpoint.GetSqlConnection();
                }

                var exceptionResponse = ApiResponse.ExecException(execEx, execOptions, out var exceptionId, debugInfo, appTitle, appVersion);

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
                                ProcessPluginExecutionExceptionHandlers(pluginList, con, execEx, additionalInfo, appTitle, appVersion, out externalRef);
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
                return new ExecuteRoutineAsyncResult(exceptionResponse, routineExecutionMetric, null, responseHeaders);
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
                                                .Where(p => p.Type == PluginType.Execution)
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
                            ExceptionLogger.LogExceptionThrottled(ex, "ExecController::InitPlugins", 2);
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
                case Strings.SQL.DATE:
                    return (SqlDbType.Date, defUdtType);
                case Strings.SQL.DATETIME:
                    return (SqlDbType.DateTime, defUdtType);
                case Strings.SQL.TIME:
                    return (SqlDbType.VarChar, defUdtType); // send as a simple string and let SQL take care of it
                case Strings.SQL.SMALLDATETIME:
                    return (SqlDbType.SmallDateTime, defUdtType);
                case Strings.SQL.INT:
                    return (SqlDbType.Int, defUdtType);
                case Strings.SQL.SMALLINT:
                    return (SqlDbType.SmallInt, defUdtType);
                case Strings.SQL.BIGINT:
                    return (SqlDbType.BigInt, defUdtType);
                case Strings.SQL.TIMESTAMP:
                    return (SqlDbType.Timestamp, defUdtType);
                case Strings.SQL.BIT:
                    return (SqlDbType.Bit, defUdtType);
                case Strings.SQL.NVARCHAR:
                    return (SqlDbType.NVarChar, defUdtType);
                case Strings.SQL.VARCHAR:
                    return (SqlDbType.VarChar, defUdtType);
                case Strings.SQL.TEXT:
                    return (SqlDbType.Text, defUdtType);
                case Strings.SQL.NTEXT:
                    return (SqlDbType.NText, defUdtType);
                case Strings.SQL.VARBINARY:
                    return (SqlDbType.VarBinary, defUdtType);
                case Strings.SQL.DECIMAL:
                    return (SqlDbType.Decimal, defUdtType);
                case Strings.SQL.UNIQUEIDENTIFIER:
                    return (SqlDbType.UniqueIdentifier, defUdtType);
                case Strings.SQL.MONEY:
                    return (SqlDbType.Money, defUdtType);
                case Strings.SQL.CHAR:
                    return (SqlDbType.Char, defUdtType);
                case Strings.SQL.NCHAR:
                    return (SqlDbType.NChar, defUdtType);
                case Strings.SQL.XML:
                    return (SqlDbType.Xml, defUdtType);
                case Strings.SQL.FLOAT:
                    return (SqlDbType.Float, defUdtType);
                case Strings.SQL.IMAGE:
                    return (SqlDbType.Image, defUdtType);
                case Strings.SQL.TINYINT:
                    return (SqlDbType.TinyInt, defUdtType);
                case Strings.SQL.GEOGRAPHY:
                    return (SqlDbType.Udt, "GEOGRAPHY");
                case Strings.SQL.GEOMETRY:
                    return (SqlDbType.Udt, "GEOMETRY");
                default:
                    throw new NotSupportedException("GetSqlDbTypeFromParameterType::Unsupported data type: " + parameterDataType);
            }
        }
    }

    public class OperationCancelledByUserException : Exception
    {
        public OperationCancelledByUserException(Exception exception) : base("SQL operation cancelled by user", exception)
        {

        }
    }
}