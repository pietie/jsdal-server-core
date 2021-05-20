using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using plugin = jsdal_plugin;
using System;
using Newtonsoft.Json;

namespace jsdal_server_core
{
    public static class PluginHelper
    {
        public static (object/*result*/, object/*outputParams*/, string/*error*/) InvokeMethod(plugin.PluginBase pluginInstance, string methodName, MethodInfo assemblyMethodInfo, Dictionary<string, string> inputParameters)
        {
            if (pluginInstance == null)
            {
                return (null, null, "Failed to find or create a plugin instance. Check your server logs. Also make sure the expected plugin is enabled on the Application.");
            }

            if (inputParameters == null) inputParameters = new Dictionary<string, string>();

            // match up input parameters with expected parameters and order according to MethodInfo expectation
            var invokeParameters = (from methodParam in assemblyMethodInfo.GetParameters()
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
                                    })
                    ;

            var invokeParametersConverted = new List<object>();
            var parameterConvertErrors = new List<string>();

            // map & convert input values from JavaScript to the corresponding Method Parameters
            foreach (var p in invokeParameters)
            {
                try
                {
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
                    else if (p.Value == null || p.Value.Equals("null", StringComparison.Ordinal))
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
                return (null, null, $"Failed to convert one or more parameters to their correct types:\r\n\r\n{string.Join("\r\n", parameterConvertErrors.ToArray())}");
            }

            // TODO: Handle specific types for output and ret valuse --> Bytes, LatLong, Guid? ??? So similiar conversions need to take place that we have on input parameters

            var inputParamArray = invokeParametersConverted.ToArray();

            var invokeResult = assemblyMethodInfo.Invoke(pluginInstance, inputParamArray);

            var isVoidResult = assemblyMethodInfo.ReturnType.FullName.Equals("System.Void", StringComparison.Ordinal);

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
                return (null, outputParametersWithValues, null);
            }
            else
            {
                // TODO: Custom control serialization of invokeResult? Test Lists, Dictionaries, Guids...Tuples etc
                return (invokeResult, outputParametersWithValues, null);
            }

        }

        public static MethodInfo FindBestMethodMatch(plugin.PluginBase plugin, string methodName, Dictionary<string, string> inputParameters)
        {
            // TODO: To support overloading we need to match name + best fit parameter list
            var methodCandidates = plugin.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name.Equals(methodName, StringComparison.Ordinal));

            if (methodCandidates.Count() == 0) return null;

            var weightedMethodList = new List<(decimal/*weight*/, string/*error*/, MethodInfo)>();

            if (inputParameters == null) inputParameters = new Dictionary<string, string>();

            // find the best matching overload (if any)
            foreach (var regMethod in methodCandidates)
            {
                var methodParameters = regMethod.GetParameters();

                if (inputParameters.Count > methodParameters.Length)
                {
                    weightedMethodList.Add((1M, "Too many parameters specified", regMethod));
                    continue;
                }

                var joined = from methodParam in methodParameters
                             join inputParam in inputParameters on methodParam.Name equals inputParam.Key into grp
                             from parm in grp.DefaultIfEmpty()
                             select new { HasMatch = parm.Key != null, Param = methodParam };

                var matched = joined.Where(e => e.HasMatch);
                var notmatched = joined.Where(e => !e.HasMatch);


                var expectedCnt = methodParameters.Count();
                var matchedCnt = matched.Count();

                // out/ref/optional parameters are added as extra credit below (but does not contribute to actual weight)
                var outRefSum = (from p in joined
                                 where (p.Param.IsOut || p.Param.IsOptional || p.Param.ParameterType.IsByRef) && !p.HasMatch
                                 select 1.0M).Sum();


                if (matchedCnt == expectedCnt || matchedCnt + outRefSum == expectedCnt)
                {
                    weightedMethodList.Add((matchedCnt, null, regMethod));
                }
                else
                {
                    //weightedMethodList.Add((matchedCnt, $"Following parameters not specified: {string.Join("\r\n", notmatched.Select(nm => nm.Param.Name))}", regMethod));
                    weightedMethodList.Add((matchedCnt, "Parameter mismatch", regMethod));
                }
            }

            var bestMatch = weightedMethodList.OrderByDescending(k => k.Item1).FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(bestMatch.Item2))
            {
                var parms = bestMatch.Item3.GetParameters();
                var parmDesc = "(no parameters)";
                if (parms.Length > 0)
                {
                    parmDesc = string.Join("\r\n", parms.Select(p => $"{p.Name} ({p.ParameterType.ToString()})")); // TODO: Provide "easy to read" description for type, e.g. nullabe Int32 can be something like 'int?' and 'List<string>' just 'string[]'
                }

                //return (null, bestMatch.Item3, $"Failed to find suitable overload.\r\nError: {bestMatch.Item2}\r\nBest match requires parameters:\r\n{parmDesc}");
                return null;
            }

            var matchedRegMethod = bestMatch.Item3;

            //var cacheKey = $"{matchedRegMethod.Registration.PluginAssemblyInstanceId}; {matchedRegMethod.Registration.TypeInfo.FullName}";

            return matchedRegMethod;
        }
    }
}