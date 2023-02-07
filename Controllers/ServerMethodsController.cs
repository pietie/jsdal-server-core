using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel.Plugins;
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
            return Execute(project, app, endpoint, null, methodName);
        }

        [AllowAnonymous]
        [HttpGet("/api/serverMethod/{project}/{app}/{endpoint}/{nameSpace}/{methodName}")]
        [HttpPost("/api/serverMethod/{project}/{app}/{endpoint}/{nameSpace}/{methodName}")]
        public IActionResult Execute([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string nameSpace, [FromRoute] string methodName)
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

                //////////////////// CUT HERE

                // TODO: Cache Reflection info on Plugin Types? e.g. MethodInfo + Parameter Info

                // TODO: Use application to find applicable plugin method based on method name ... pass endpoint in for context?
                //application.

                // GET/CREATE plugin instance
                (var pluginInstance, var method, var notFoundError) = ep.GetServerMethodPluginInstance(nameSpace, methodName, inputParameters);

                if (!string.IsNullOrWhiteSpace(notFoundError))
                {
                    return NotFound(notFoundError);
                }
                else if (pluginInstance == null)
                {
                    return BadRequest("Failed to find or create a plugin instance. Check your server logs. Also make sure the expected plugin is enabled on the Application.");
                }


                ///////////////////////////
                // INJECTED PARAMETERS
                ///////////////////////////
                //{
                    // get items requesting Injection and add them to input params automatically (if they don't aleady exist)

                    var expectedParameters = method.AssemblyMethodInfo.GetParameters();

                    var toBeInjectedParameters = expectedParameters.Where(p => p.GetCustomAttributes(typeof(jsdal_plugin.InjectParamAttribute), false).Count() > 0).ToList();

                    foreach (var toInject in toBeInjectedParameters)
                    {
                        if (!inputParameters.ContainsKey(toInject.Name))
                        {
                            inputParameters.Add(toInject.Name, null);
                        }
                    }

                //}

                // match up input parameters with expected parameters and order according to MethodInfo expectation
                var invokeParameters = (from methodParam in method.AssemblyMethodInfo.GetParameters()
                                        join inputParam in inputParameters on methodParam.Name equals inputParam.Key into grp
                                        from parm in grp.DefaultIfEmpty()
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
                                            Value = parm.Value,
                                            Position = methodParam.Position,
                                            IsParamMatched = parm.Key != null,
                                            IsArray = methodParam.ParameterType.IsArray
                                        }).ToList();

                var invokeParametersConverted = new List<object>();
                var parameterConvertErrors = new List<string>();

                // map & convert input values from JavaScript to the corresponding Method Parameters
                foreach (var p in invokeParameters)
                {
                    try
                    {
                        var needsInjection = toBeInjectedParameters.FirstOrDefault(i=>i.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) != null;

                        object o = null;
                        Type expectedType = p.Type;

                        if (p.IsOut || p.IsByRef || expectedType.IsArray)
                        {
                            // switch from 'ref' type to actual (e.g. System.Int32& to System.Int32)
                            expectedType = expectedType.GetElementType();
                        }

                        var underlyingNullableType = Nullable.GetUnderlyingType(expectedType);
                        var isNullable = underlyingNullableType != null;

                        if (isNullable) expectedType = underlyingNullableType;

                        if (!p.IsParamMatched)
                        {
                            if (p.HasDefault)
                            {
                                o = Type.Missing;
                            }
                        }
                        else if (!needsInjection && (p.Value == null || p.Value.Equals(Strings.@null, StringComparison.Ordinal)))
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
                        else if (needsInjection && expectedType == typeof(Microsoft.AspNetCore.Http.HttpRequest))
                        {
                            o = req;
                        }
                        else if (needsInjection && expectedType == typeof(Microsoft.AspNetCore.Http.HttpResponse))
                        {
                            o = res;
                        }
                        else if (expectedType == typeof(System.Byte))
                        {
                            if (int.TryParse(p.Value, out var singleByteValue))
                            {
                                if (p.IsArray)
                                {
                                    o = new byte[] { (byte)singleByteValue };
                                }
                                else
                                {
                                    o = (byte)singleByteValue;
                                }
                            }
                            else // assume base64
                            {
                                o = System.Convert.FromBase64String(p.Value);

                                if (!p.IsArray) // if we don't expect an array just grab the first byte
                                {
                                    o = ((byte[])o)[0];
                                }
                            }
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

                // TODO: Handle specific types for output and ret valuse --> Bytes, LatLong, Guid? ??? So similiar conversions need to take place that we have on input parameters

                var inputParamArray = invokeParametersConverted.ToArray();

                // INVOKE ServerMerthod
                var invokeResult = method.AssemblyMethodInfo.Invoke(pluginInstance, inputParamArray);

                var isVoidResult = method.AssemblyMethodInfo.ReturnType.FullName.Equals("System.Void", StringComparison.Ordinal);

                // create a lookup of the indices of the out/ref parameters
                var outputLookupIx = invokeParameters.Where(p => p.IsOut || p.IsByRef).Select(p => p.Position).ToArray();

                var outputParametersWithValuesFull = (from p in invokeParameters
                                                      join o in outputLookupIx on p.Position equals o
                                                      select new
                                                      {
                                                          p.Name,
                                                          Value = inputParamArray[o]
                                                          // Value = new ApiSingleValueOutputWrapper(p.Name, inputParamArray[o]) // TODO: need more custom serialization here? Test Lists, Dictionaries, Tuples,Byte[] etc
                                                          //Value = GlobalTypescriptTypeLookup.SerializeCSharpToJavaScript(inputParamArray[o]) // TODO: need more custom serialization here? Test Lists, Dictionaries, Tuples,Byte[] etc
                                                      });

                var outputParametersWithValues = outputParametersWithValuesFull.ToDictionary(x => x.Name, x => x.Value);


                if (isVoidResult)
                {
                    return Ok(ApiResponseServerMethodVoid.Success(outputParametersWithValues));
                }
                else if (invokeResult is IActionResult) // return 'as is' if already IActionResult compatible
                {
                    return (IActionResult)invokeResult;
                }
                else
                {
                    // TODO: Custom control serialization of invokeResult? Test Lists, Dictionaries, Guids...Tuples etc
                    return Ok(ApiResponseServerMethodResult.Success(invokeResult, outputParametersWithValues));
                }

                // TODO: Content-type...Cache headers....Wrap in APIResponse?
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseServerMethodResult.Exception(ex));
            }
        }

        public class ApiSingleValueOutputWrapper
        {
            public string Name { get; private set; }
            private object _value;
            public ApiSingleValueOutputWrapper(string name, object val)
            {
                this.Name = name;
                _value = val;
            }

            public object Value { get { return _value; } }
        }


        [HttpGet("/server-method")]
        public ApiResponse GetServerMethodCollection()
        {
            try
            {
                var q = from asm in PluginLoader.Instance.PluginAssemblies
                        select new
                        {
                            Id = asm.InstanceId,
                            asm.InlineEntryId,
                            Name = asm.Assembly.GetName().Name,
                            IsValid = true, //TODO: Still relevant?
                            asm.IsInline,
                            Plugins = asm.Plugins
                            // .Where(p => p.Type == Settings.ObjectModel.PluginType.ServerMethod)
                            // dont have anywhere else to display other INLINE plugins
                            .Where(p => p.Type == Settings.ObjectModel.PluginType.ServerMethod || asm.IsInline)
                            .Select(p => new { p.Name, p.Description })
                        };

                return ApiResponse.Payload(q.Where(mod => mod.Plugins != null && mod.Plugins.Count() > 0));
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        // [HttpPost("/server-api/{id?}")]
        // public async Task<ApiResponse> AddUpdateServerMethodCollection([FromRoute] string id, dynamic bodyIgnored)
        // {
        //     try
        //     {
        //         if (string.IsNullOrWhiteSpace(id)) id = null;

        //         string code = null;

        //         using (var sr = new System.IO.StreamReader(this.Request.Body))
        //         {
        //             code = sr.ReadToEnd();

        //             var (success, ret) = await CSharpCompilerHelper.Evaluate(code);

        //             if (!success) return ret;
        //         }

        //         if (!CSharpCompilerHelper.ParseAgainstBase<jsdal_plugin.ServerMethodPlugin, BasePluginRuntime>(id, code, out var parsedPluginCollection, out var problems))
        //         {
        //             return ApiResponse.Payload(new { CompilationError = problems });
        //         }

        //         // TODO: if server-method is add or UPDATED we need to refresh/recompile a version in memory that is used when doing the actual execution
        //         // We also need to cache metadata for those

        //         if (id == null)
        //         {
        //             //var pluginModule = ServerMethodPluginRuntime.CreateInlineModule(code, parsedPluginCollection);

        //             var pluginModule = new Settings.ObjectModel.Plugins.InlinePlugins.InlinePluginModule(code, true/*isValid*/);

        //             pluginModule.AddPluginRange(parsedPluginCollection);

        //             // TODO: Validation needs to be in one. Currently we first create the module (and file on disk) and then we complain about a conflicting Guid for example
        //             var ret = SettingsInstance.Instance.AddInlinePluginModule(pluginModule);

        //             if (!ret.IsSuccess)
        //             {
        //                 return ApiResponse.ExclamationModal(ret.userErrorVal);
        //             }

        //             SettingsInstance.SaveSettingsToFile();

        //             id = pluginModule.Id;
        //         }
        //         else
        //         {
        //             var ret = SettingsInstance.Instance.UpdateInlinePluginModule(id, code, parsedPluginCollection, true);

        //             if (!ret.IsSuccess)
        //             {
        //                 return ApiResponse.ExclamationModal(ret.userErrorVal);
        //             }

        //             SettingsInstance.SaveSettingsToFile();
        //         }

        //         return ApiResponse.Payload(new { id = id });
        //     }
        //     catch (Exception ex)
        //     {
        //         return ApiResponse.Exception(ex);
        //     }

        // }

        // [HttpDelete("/server-api/{id}")]
        // public ApiResponse DeleteServerMethodCollection([FromRoute] string id)
        // {
        //     try
        //     {
        //         var ret = SettingsInstance.Instance.DeleteInlinePluginModule(id);

        //         if (ret.IsSuccess)
        //         {
        //             SettingsInstance.SaveSettingsToFile();
        //             return ApiResponse.Success();
        //         }
        //         else
        //         {
        //             return ApiResponse.ExclamationModal(ret.userErrorVal);
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         return ApiResponse.Exception(ex);
        //     }
        // }

        // [HttpGet("/server-api/{id}")]
        // public ApiResponse GetServerMethodCode([FromRoute] string id)
        // {
        //     try
        //     {
        //         var ret = SettingsInstance.Instance.GetInlinePluginModule(id, out var source);

        //         if (ret.IsSuccess)
        //         {
        //             return ApiResponse.Payload(source);
        //         }
        //         else
        //         {
        //             return ApiResponse.ExclamationModal(ret.userErrorVal);
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         return ApiResponse.Exception(ex);
        //     }
        // }


    }
}