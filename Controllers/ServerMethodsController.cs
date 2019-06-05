using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                            .Select(m => m);
                //!.Select(m => new { m.Registration, m.MethodInfo, Parameters = m.MethodInfo.GetParameters().ToList() })
                ;

                if (methodCandidates.Count() == 0) return NotFound("Method name not found.");

                //TODO: Possible to support default parameters? So then input params wont match method params...nice to have!
                //TODO: Don't need to specify out params

                var weightedMethodList = new List<(int/*weight*/, string/*error*/, ServerMethodRegistrationMethod)>();

                // find the best matching overload (if any)
                foreach (var regMethod in methodCandidates)
                {
                    var methodParameters = regMethod.MethodInfo.GetParameters();

                    if (inputParameters.Count > methodParameters.Length)
                    {
                        weightedMethodList.Add((1, "Too many parameters specified", regMethod));
                        continue;
                    }

                    var matchedOnName = from methodParam in methodParameters
                                        join inputParam in inputParameters on methodParam.Name equals inputParam.Key
                                        select 1;


                    weightedMethodList.Add((matchedOnName.Count(), null, regMethod));
                }

                var bestMatch = weightedMethodList.OrderByDescending(k => k.Item1).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(bestMatch.Item2))
                {
                    // TODO: Extend error description
                    return NotFound(bestMatch.Item2);
                }

                var method = bestMatch.Item3;

                // look for overload with same parameters as input
                // TODO: This not take into account that the order might be different!
                // var inputParmsCsv = string.Join(",", inputParameters.Select(kv => kv.Key));
                // var method = methodCandidates.FirstOrDefault(m => string.Join(",", m.Parameters.Select(p => p.Name)).Equals(inputParmsCsv, StringComparison.Ordinal));

                // if (method == null) return NotFound("No matching overload found that matches input parameters.");


                // match up input parameters with expected parameters and order according to MethodInfo expectation
                var invokeParameters = (from methodParam in method.MethodInfo.GetParameters()
                                        join inputParam in inputParameters on methodParam.Name equals inputParam.Key
                                        orderby methodParam.Position
                                        select new
                                        {
                                            Name = methodParam.Name,
                                            Type = methodParam.ParameterType,
                                            HasDefault = methodParam.HasDefaultValue,
                                            IsOptional = methodParam.IsOptional, // compiler dependent
                                            IsOut = methodParam.IsOut,
                                            IsByRef = methodParam.ParameterType.IsByRef,
                                            DefaultValue = methodParam.RawDefaultValue,
                                            Value = inputParam.Value,
                                            Position = methodParam.Position
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

                        if (p.IsOut || p.IsByRef)
                        {
                            // switch from 'ref' type to actual (e.g. System.Int32& to System.Int32)
                            expectedType = expectedType.GetElementType();
                        }

                        var underlyingNullableType = Nullable.GetUnderlyingType(expectedType);
                        var isNullable = underlyingNullableType != null;

                        if (isNullable) expectedType = underlyingNullableType;

                        if (p.Value == null || p.Value.Equals("null", StringComparison.Ordinal))
                        {
                            // if null was passed it's null
                            o = null;

                            if (!isNullable && expectedType.IsValueType)
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

                            if (p.Value != null)
                            {
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

                var inputParamArray = invokeParametersConverted.ToArray();

                var ret = method.MethodInfo.Invoke(method.Registration.PluginInstance, inputParamArray);

                // create a lookup of the indices of the out/ref parameters
                var outputLookupIx = invokeParameters.Where(p => p.IsOut || p.IsByRef).Select(p => p.Position).ToArray();

                var outputParametersWithValues = (from p in invokeParameters
                                                  join o in outputLookupIx on p.Position equals o
                                                  select new
                                                  {
                                                      p.Name,
                                                      Value = inputParamArray[o]
                                                  }).ToList(); // TODO: ToDictionary!!!
                //var outputParam = inputParamArray.Where((o, i) => outputLookupIx.Contains(i));

                // TODO: Content-type...Cache headers....Wrap in APIResponse?

                // TODO: Match expected ApiResponse Result & Out values!

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