using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class ServerMethodsController : Controller
    {
        [AllowAnonymous]
        [HttpGet("/api/serverMethod/{project}/{app}/{endpoint}/{methodName}")]
        [HttpPost("/api/serverMethod/{project}/{app}/{endpoint}/{methodName}")]
        public IActionResult Execute([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string methodName)
        {
            var res = this.Response;
            var req = this.Request;
            Dictionary<string, string> inputParameters = null;
            string body = null;

            var isPOST = req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

            try
            {
                var syncIOFeature = HttpContext.Features.Get<IHttpBodyControlFeature>();
                if (syncIOFeature != null)
                {
                    syncIOFeature.AllowSynchronousIO = true;
                }

                if (!ControllerHelper.GetProjectAndAppAndEndpoint(project, app, endpoint, out var proj, out var application, out var ep, out var resp))
                {
                    return NotFound($"The specified endpoint does not exist: {project}/{app}/{endpoint}");
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

                // TODO: Cache Reflection info on Plugin Types? e.g. MethodInfo + Parameter Info
                // TODO: Use application to find applicable plugin method based on method name ... pass endpoint in for context?
                //application.

                // find all registered ServerMethods for this app
                var registrations = ServerMethodManager.GetRegistrations().Where(reg => application.IsPluginIncluded(reg.PluginGuid));

                // TODO: To support overloading we need to match name + best fit parameter list
                var methodCandidates = registrations.SelectMany(reg => reg.Methods)
                            .Where(m => m.Name.Equals(methodName, StringComparison.Ordinal))
                            .Select(m => new { m.Registration, m.MethodInfo, Parameters = m.MethodInfo.GetParameters().ToList() })
                            ;

                if (methodCandidates.Count() == 0) return NotFound("Method name not found.");

                //TODO: Possible to support default parameters? So then input params wont match method params...nice to have!
                //TODO: Don't need to specify out params

                // look for overload with same parameters as input
                var inputParmsCsv = string.Join(",", inputParameters.Select(kv => kv.Key));
                var method = methodCandidates.FirstOrDefault(m => string.Join(",", m.Parameters.Select(p => p.Name)).Equals(inputParmsCsv, StringComparison.Ordinal));

                if (method == null) return NotFound("No matching overload found that matches input parameters.");


                // match up input parameters with expected parameters and order according to MethodInfo expectation
                var invokeParameters = (from methodParam in method.Parameters
                                        join inputParam in inputParameters on methodParam.Name equals inputParam.Key
                                        orderby methodParam.Position
                                        select new
                                        {
                                            Name = methodParam.Name,
                                            Type = methodParam.ParameterType,
                                            HasDefault = methodParam.HasDefaultValue,
                                            IsOptional = methodParam.IsOptional, // compiler dependent
                                            DefaultValue = methodParam.RawDefaultValue,
                                            Value = inputParam.Value
                                        })
                        ;

                var invokeParametersConverted = new List<object>();
                var parameterConvertErrors = new List<string>();

                foreach (var p in invokeParameters)
                {
                    try
                    {
                        object o = null;
                        Type expectedType = p.Type;

                        var underlyingNullableType = Nullable.GetUnderlyingType(expectedType);
                        var isNullable = underlyingNullableType != null;

                        if (isNullable) expectedType = underlyingNullableType;

                        if (p.Value.Equals("null", StringComparison.Ordinal))
                        {
                            // if null was passed it's null
                            o = null;

                            if (!isNullable)
                            {
                                parameterConvertErrors.Add($"Unable to set parameter '{p.Name}' to null. The parameter is not nullable. Expected type: {p.Type.FullName}");
                                continue;
                            }
                        }
                        else if (expectedType == typeof(Guid))
                        {
                            o = Guid.Parse(p.Value);
                        }
                        else if (expectedType.IsGenericType && expectedType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        { // assume json object was passed
                          //      var keyValueTypes = expectedType.GetGenericArguments();

                            if (p.Value != null)
                            {
                                //var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(p.Value);
                                o = JsonConvert.DeserializeObject(p.Value, expectedType);
                            }
                        }
                        else if (expectedType.IsGenericType && expectedType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            o = JsonConvert.DeserializeObject(p.Value, expectedType);
                        }
                        else if (expectedType.GetInterface("IConvertible") != null)
                        {
                            o = Convert.ChangeType(p.Value, expectedType);
                        }

                        invokeParametersConverted.Add(o);
                    }
                    catch (Exception ex)
                    {
                        parameterConvertErrors.Add($"Unable to set parameter '{p.Name}'. Expected type: {p.Type.FullName}");
                    }
                }

                if (parameterConvertErrors.Count > 0)
                {
                    return BadRequest($"Failed to convert one or more parameters to their correct types:\r\n\r\n{string.Join("\r\n", parameterConvertErrors.ToArray())}");
                }


                // TODO: To support "Context" we need to instantiate a new instance with each call..or at least a new instance per EP and then cache that instance for a while?

                var ret = method.MethodInfo.Invoke(method.Registration.PluginInstance, invokeParametersConverted.ToArray());

                // TODO: Content-type...Cache headers....Wrap in APIResponse?

                return Ok(ret);
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Exception(ex));
            }
        }

        [HttpGet("/server-api")]
        public ApiResponse GetServerMethodCollection()
        {
            try
            {
                var q = from p in SettingsInstance.Instance.InlinePlugins
                        where p.Type == PluginType.ServerMethod
                        select new
                        {
                            p.Id,
                            p.IsValid,
                            p.Name,
                            p.Description
                        };

                return ApiResponse.Payload(q);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        [HttpPost("/server-api/{id?}")]
        public async Task<ApiResponse> AddUpdateServerMethodCollection([FromRoute] string id, dynamic bodyIgnored)
        {
            try
            {
                string code = null;

                using (var sr = new System.IO.StreamReader(this.Request.Body))
                {
                    code = sr.ReadToEnd();

                    var (success, ret) = await CSharpCompilerHelper.Evaluate(code);

                    if (!success) return ret;
                }

                if (!CSharpCompilerHelper.ParseAgainstBase<jsdal_plugin.ServerMethodPlugin>(code, out var pluginName, out var pluginGuid, out var pluginDesc, out var problems))
                {
                    return ApiResponse.Payload(new { CompilationError = problems });
                }


                // TODO: if server-method is add or UPDATED we need to refresh/recompile a version in memory that is used when doing the actual execution
                // We also need to cache metadata for those
                if (id == null)
                {
                    var plugin = ServerMethodPlugin.Create(code, pluginName, pluginGuid, pluginDesc, true/*TODO:?!?!?!?!*/);

                    var ret = SettingsInstance.Instance.AddInlinePlugin(plugin);

                    if (ret.isSuccess)
                    {
                        SettingsInstance.SaveSettingsToFile();
                    }
                    else
                    {
                        return ApiResponse.ExclamationModal(ret.userErrorVal);
                    }
                }


                return ApiResponse.Success();
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        [HttpDelete("/server-api/{id}")]
        public ApiResponse DeleteServerMethodCollection([FromRoute] string id)
        {
            try
            {
                var ret = SettingsInstance.Instance.DeleteInlinePlugin(id);

                if (ret.isSuccess)
                {
                    SettingsInstance.SaveSettingsToFile();
                    return ApiResponse.Success();
                }
                else
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


    }
}