using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using jsdal_plugin;
using jsdal_server_core.PluginManagement;
using System.Text;
using OM = jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core
{
    public class ServerMethodScriptGenerator : ScriptGeneratorBase
    {
        private const string SERVER_TSD_METHOD_NONSTATIC_TEMPLATE = "<<FUNC_NAME>>(<<PARM_LIST>>): <<RET_TYPE>>;";
        private const string SERVER_TSD_METHOD_TEMPLATE = "static <<FUNC_NAME>>(<<PARM_LIST>>): <<RET_TYPE>>;";

        public ServerMethodPluginRegistration Registration { get; private set; }

        // private List<ScriptableMethodInfo> _methods;
        // public ReadOnlyCollection<ScriptableMethodInfo> Methods { get; private set; }

        private ServerMethodScriptGenerator(ServerMethodPluginRegistration registration, string assemblyInstanceId, PluginInfo pluginInfo) : base(assemblyInstanceId, pluginInfo)
        {
            this.Registration = registration;

        }


        public static ServerMethodScriptGenerator Create(ServerMethodPluginRegistration registration, PluginInfo pluginInfo)
        {
            var ret = new ServerMethodScriptGenerator(registration, registration.PluginAssemblyInstanceId, pluginInfo);
            ret.Process();
            return ret;
        }

        protected override void Process()
        {
            this.GenerateAndCacheJsInterface();
        }


        // generates and caches the ServerMethod .js and .tsd for a specific ServerMethod Plugin Registration
        private void GenerateAndCacheJsInterface()
        {
            try
            {
                var definitionsJS = JavaScriptDefinitions = new Dictionary<string, List<Definition>>();
                var definitionsTSD = TypescriptDefinitions = new Dictionary<string, List<Definition>>();

                JavaScriptDefinitionsHash = TypescriptDefinitionsHash = null;

                ConverterLookup = new List<string>();

                var namespaceLookup = new List<string>();

                // add default namespace 
                definitionsJS.Add("ServerMethods", new List<Definition>());
                definitionsTSD.Add("ServerMethods", new List<Definition>());

                foreach (var method in this.Registration.Methods)
                {
                    var namespaceKey = "ServerMethods";
                    var namespaceKeyTSD = "ServerMethods";
                    var jsNamespaceVar = Strings.@null; // null for main ServerMethod namespace

                    if (!string.IsNullOrEmpty(method.Namespace))
                    {
                        namespaceKey = method.Namespace;
                        namespaceKeyTSD = method.Namespace;
                    }

                    var isCustomNamespace = !namespaceKey.Equals("ServerMethods", StringComparison.Ordinal);

                    if (isCustomNamespace)
                    {
                        if (!namespaceLookup.Contains(namespaceKey)) { namespaceLookup.Add(namespaceKey); }
                        jsNamespaceVar = $"_ns[{namespaceLookup.IndexOf(namespaceKey)}]";
                    }

                    if (!definitionsJS.ContainsKey(namespaceKey))
                    {
                        definitionsJS.Add(namespaceKey, new List<Definition>());
                    }

                    if (!definitionsTSD.ContainsKey(namespaceKeyTSD))
                    {
                        definitionsTSD.Add(namespaceKeyTSD, new List<Definition>());
                    }

                    var methodParameters = method.AssemblyMethodInfo.GetParameters();

                    var inputConvertersLookup = new Dictionary<string, ConverterDefinition>();
                    var outputConvertersLookup = new Dictionary<string, ConverterDefinition>();
                    var resultsConvertersLookup = new Dictionary<string, ConverterDefinition>();

                    foreach (var inputParam in methodParameters)
                    {
                        GlobalConverterLookup.AnalyseForRequiredOutputConverters(inputParam.Name, inputParam.ParameterType, null, ref inputConvertersLookup);
                    }

                    foreach (var outputParam in methodParameters.Where(p => p.IsOut || p.ParameterType.IsByRef))
                    {
                        GlobalConverterLookup.AnalyseForRequiredOutputConverters(outputParam.Name, outputParam.ParameterType, null, ref outputConvertersLookup);
                    }

                    GlobalConverterLookup.AnalyseForRequiredOutputConverters("$result$", method.AssemblyMethodInfo.ReturnType, null, ref resultsConvertersLookup);

                    string inputConverter = null;
                    string outputConverter = null;
                    string resultConverter = null;

                    var allRequiredConverters = inputConvertersLookup.Select(c => c.Value.ToJson()).Distinct()
                                .Concat(outputConvertersLookup.Select(c => c.Value.ToJson()).Distinct())
                                .Concat(resultsConvertersLookup.Select(c => c.Value.ToJson()).Distinct())
                                .ToList();

                    if (allRequiredConverters.Count > 0)
                    {
                        foreach (var converterJson in allRequiredConverters)
                        {
                            if (ConverterLookup.IndexOf(converterJson) == -1)
                            {
                                ConverterLookup.Add(converterJson);
                            }
                        }

                    }

                    inputConverter = string.Join(",", (from kv in inputConvertersLookup
                                                       select $"\"{kv.Key}\": $c[{kv.Value.ToJson()}]")); // ignore ConverterOptions for now as we don't actually have any use for it at the moment

                    outputConverter = string.Join(",", (from kv in outputConvertersLookup
                                                        select $"\"{kv.Key}\": $c[{kv.Value.ToJson()}]")); // ignore ConverterOptions for now as we don't actually have any use for it at the moment


                    resultConverter = string.Join(",", (from kv in resultsConvertersLookup
                                                        select $"\"{kv.Key}\": $c[{kv.Value.ToJson()}]")); // ignore ConverterOptions for now as we don't actually have any use for it at the moment

                    inputConverter = string.IsNullOrWhiteSpace(inputConverter) ? null : inputConverter;
                    outputConverter = string.IsNullOrWhiteSpace(outputConverter) ? null : outputConverter;
                    resultConverter = string.IsNullOrWhiteSpace(resultConverter) ? null : resultConverter;

                    var hasConverters = inputConverter != null || outputConverter != null || resultConverter != null;

                    // js
                    var methodLineJS = ServerMethodManager.TEMPLATE_ServerMethodFunctionTemplate
                                    .Replace("<<FUNC_NAME>>", method.Name)
                                    .Replace("<<NAMESPACE>>", jsNamespaceVar);
                    ;

                    if (!hasConverters)
                    {
                        methodLineJS = methodLineJS.Replace("<<CONV_SEP>>", "");
                        methodLineJS = methodLineJS.Replace("<<CONVERTERS>>", "");
                    }

                    if (methodParameters.Count() > 0)
                    {
                        methodLineJS = methodLineJS.Replace("<<ARG_SEP>>", ", ");
                        methodLineJS = methodLineJS.Replace("<<HAS_PARMS>>", "o");
                    }
                    else
                    {
                        methodLineJS = methodLineJS.Replace("<<ARG_SEP>>", "");
                        methodLineJS = methodLineJS.Replace("<<HAS_PARMS>>", "");
                    }

                    definitionsJS[namespaceKey].Add(new Definition() { MethodName = method.Name, Line = methodLineJS, InputConverter = inputConverter, OutputConverter = outputConverter, ResultsConverter = resultConverter });

                    // TSD
                    string methodLineTSD = null;

                    if (isCustomNamespace)
                    {
                        methodLineTSD = SERVER_TSD_METHOD_NONSTATIC_TEMPLATE.Replace("<<FUNC_NAME>>", method.Name);
                    }
                    else
                    {
                        methodLineTSD = SERVER_TSD_METHOD_TEMPLATE.Replace("<<FUNC_NAME>>", method.Name);
                    }

                    var inputParmListLookup = from p in methodParameters
                                              select new
                                              {
                                                  Name = p.Name,
                                                  p.IsOut,
                                                  p.ParameterType.IsByRef,
                                                  p.ParameterType.IsArray,
                                                  p.ParameterType.IsValueType,
                                                  HasDefault = p.IsOptional,
                                                  IsNullable = Nullable.GetUnderlyingType(p.ParameterType) != null,
                                                  TypescriptDataType = GlobalTypescriptTypeLookup.GetTypescriptTypeFromCSharp(p.ParameterType)
                                              };

                    var inputParmsFormatted = from p in inputParmListLookup
                                              select $"{p.Name}{((p.HasDefault) ? "?" : "")}: {p.TypescriptDataType}";
                    // TODO: Revise. Not clear if IsNullable should also be output with a '?'. In TypeScript this means optional and not 'nullable'. So in C# even if a parameter is nullable it is still required to specified it. ? should be reserved for OPTIONAL parameters
                    //select $"{p.Name}{((p.HasDefault || p.IsNullable ) ? "?" : "")}: {p.TypescriptDataType}";


                    string typeNameBase = $"{(isCustomNamespace ? namespaceKeyTSD + "_" : "")}{ method.Name }";
                    string typeNameInputParms = $"{typeNameBase}_In";
                    string typeNameOutputParms = $"{typeNameBase}_Out";
                    string typeNameResult = $"{typeNameBase}_Res";

                    string typeDefInputParms = null;
                    string typeDefOutputParms = null;
                    string typeDefResult = null;


                    // if there are INPUT parameters
                    if (inputParmsFormatted.Count() > 0)
                    {
                        methodLineTSD = methodLineTSD.Replace("<<PARM_LIST>>", "parameters?: __." + typeNameInputParms);

                        typeDefInputParms = $"type {typeNameInputParms} = {"{" + string.Join(", ", inputParmsFormatted) + "}"};";
                        typeNameInputParms = "__." + typeNameInputParms;
                    }
                    else
                    {
                        methodLineTSD = methodLineTSD.Replace("<<PARM_LIST>>", "");
                        typeNameInputParms = "void";
                    }

                    // if there are OUTPUT parameters
                    if (inputParmListLookup.Count(p => p.IsOut) > 0)
                    {
                        typeDefOutputParms = $"type {typeNameOutputParms} = {{ " + string.Join(", ", (from p in inputParmListLookup
                                                                                                      where p.IsOut
                                                                                                      select $"{p.Name}: {p.TypescriptDataType}")) + " };";
                        typeNameOutputParms = "__." + typeNameOutputParms;
                    }
                    else
                    {
                        typeNameOutputParms = "void";
                    }


                    if (method.AssemblyMethodInfo.ReturnType == typeof(void)) // no result
                    {
                        //IServerMethodVoid<OuputParameters, InputParameters>
                        methodLineTSD = methodLineTSD.Replace("<<RET_TYPE>>", $"IServerMethodVoid<{typeNameOutputParms}, {typeNameInputParms}>");
                    }
                    else
                    {
                        //IServerMethod<OuputParameters, ResultType, InputParameters>

                        var retType = GlobalTypescriptTypeLookup.GetTypescriptTypeFromCSharp(method.AssemblyMethodInfo.ReturnType);

                        // if a built-in js/TS type
                        if (new string[] { "number", "string", "date", "boolean", "any", "number[]", "string[]", "date[]", "boolean[]", "any[]" }.Contains(retType.ToLower()))
                        {
                            typeDefResult = null;
                            methodLineTSD = methodLineTSD.Replace("<<RET_TYPE>>", $"IServerMethod<{typeNameOutputParms}, {retType}, {typeNameInputParms}>");
                        }
                        else
                        {
                            typeDefResult = $"type {typeNameResult} = {retType};";
                            methodLineTSD = methodLineTSD.Replace("<<RET_TYPE>>", $"IServerMethod<{typeNameOutputParms}, __.{typeNameResult}, {typeNameInputParms}>");
                        }
                    }

                    definitionsTSD[namespaceKeyTSD].Add(new Definition() { MethodName = method.Name, Line = methodLineTSD, TypesLines = new List<string>() { typeDefInputParms, typeDefOutputParms, typeDefResult } });
                } // foreach (var method in this.Methods)


                var jsLines = string.Join("\n", definitionsJS.Select(kv => $"{kv.Key}§{string.Join('\n', kv.Value.Select(d => d.Line).ToArray())}").ToArray());
                var tsdLines = string.Join("\n", definitionsTSD.Select(kv => $"{kv.Key}§{string.Join('\n', kv.Value.Select(d => d.Line).ToArray())}").ToArray());

                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var jsHash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(jsLines));
                    var tsdHash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(tsdLines));

                    // TODO: Compare with current Hashes. If different then let all relevant Apps know

                    this.JavaScriptDefinitionsHash = jsHash;
                    this.TypescriptDefinitionsHash = tsdHash;
                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }





    }
}