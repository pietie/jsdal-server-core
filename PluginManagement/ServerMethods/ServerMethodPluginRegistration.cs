using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;
using System.Collections.ObjectModel;
using System.Text;
using OM = jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core.ServerMethods
{
    // Represents a single instance of a class that derives from one of the plugin classes (ServerMethodPlugin, ExecutionPlugin)
    // An assembly may contain multiples of these
    public class ServerMethodPluginRegistration
    {
        private Dictionary<string, List<Definition>> JavaScriptDefinitions;
        private Dictionary<string, List<Definition>> TypescriptDefinitions;
        private byte[] JavaScriptDefinitionsHash;
        private byte[] TypescriptDefinitionsHash;
        public Assembly Assembly { get; private set; }
        public TypeInfo TypeInfo { get; private set; }
        public string PluginGuid { get; private set; }
        private readonly List<ServerMethodRegistrationMethod> _methods;
        public ReadOnlyCollection<ServerMethodRegistrationMethod> Methods { get; private set; }

        private ServerMethodPluginRegistration(Assembly assembly, TypeInfo typeInfo, Guid pluginGuid)
        {
            _methods = new List<ServerMethodRegistrationMethod>();
            this.Methods = _methods.AsReadOnly();
            this.Assembly = assembly;
            this.TypeInfo = typeInfo;
            this.PluginGuid = pluginGuid.ToString();
        }

        public static ServerMethodPluginRegistration Create(PluginInfo pluginInfo)
        {
            var reg = new ServerMethodPluginRegistration(pluginInfo.Assembly, pluginInfo.TypeInfo, pluginInfo.Guid);
            reg.Process(pluginInfo);
            return reg;
        }

        private void Process(PluginInfo pluginInfo)
        {
            var classLevelAttrib = pluginInfo.TypeInfo.GetCustomAttribute(typeof(ServerMethodAttribute)) as ServerMethodAttribute;

            // static methods not supported 
            var methods = pluginInfo.TypeInfo.GetMethods(BindingFlags.Public /* | BindingFlags.Static*/ | BindingFlags.Instance);

            string classLevelNamespace = classLevelAttrib?.Namespace;

            var serverMethodCollection = (from mi in methods select new { MethodInfo = mi, ServerMethodAttribute = mi.GetCustomAttribute(typeof(ServerMethodAttribute)) as ServerMethodAttribute })
                                              .Where(m => m.ServerMethodAttribute != null);

            if (serverMethodCollection.Count() == 0)
            {
                SessionLog.Warning($"No server method's found in plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. Add a [ServerMethod] attribute to the methods you want to expose.");
                return;
            }

            foreach (var m in serverMethodCollection)
            {
                string ns = m.ServerMethodAttribute?.Namespace;

                if (ns == null)
                {
                    ns = classLevelNamespace;
                }

                this.AddMethod(m.MethodInfo.Name, ns, m.MethodInfo);
            }

            this.GenerateAndCacheJsInterface();
        }

        public ServerMethodRegistrationMethod AddMethod(string name, string nameSpace, MethodInfo methodInfo)
        {
            var method = new ServerMethodRegistrationMethod(this);

            method.Name = name;
            method.Namespace = nameSpace;
            method.MethodInfo = methodInfo;

            this._methods.Add(method);

            return method;
        }

        private const string SERVER_TSD_METHOD_NONSTATIC_TEMPLATE = "<<FUNC_NAME>>(<<PARM_LIST>>): <<RET_TYPE>>;";
        private const string SERVER_TSD_METHOD_TEMPLATE = "static <<FUNC_NAME>>(<<PARM_LIST>>): <<RET_TYPE>>;";

        private class Definition
        {
            public string MethodName { get; set; }
            public string Line { get; set; }
            public List<string> TypesLines { get; set; }
        }

        // generates and caches the ServerMethod .js and .tsd for a specific ServerMethod Plugin Registration
        private void GenerateAndCacheJsInterface()
        {
            try
            {
                var definitionsJS = JavaScriptDefinitions = new Dictionary<string, List<Definition>>();
                var definitionsTSD = TypescriptDefinitions = new Dictionary<string, List<Definition>>();

                JavaScriptDefinitionsHash = TypescriptDefinitionsHash = null;

                var namespaceLookup = new List<string>();

                // add default namespace 
                definitionsJS.Add("ServerMethods", new List<Definition>());
                definitionsTSD.Add("ServerMethods", new List<Definition>());

                foreach (var method in this.Methods)
                {
                    var namespaceKey = "ServerMethods";
                    var namespaceKeyTSD = "ServerMethods";
                    var jsNamespaceVar = "null"; // null for main ServerMethod namespace

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

                    var methodParameters = method.MethodInfo.GetParameters();

                    // js
                    var methodLineJS = ServerMethodManager.TEMPLATE_ServerMethodFunctionTemplate
                                    .Replace("<<FUNC_NAME>>", method.Name)
                                    .Replace("<<NAMESPACE>>", jsNamespaceVar);
                    ;

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

                    definitionsJS[namespaceKey].Add(new Definition() { MethodName = method.Name, Line = methodLineJS });

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
                                                  TypescriptDataType = GlobalTypescriptTypeLookup.GetTypescriptTypeFromCSharp(p.ParameterType)
                                              };

                    var inputParmsFormatted = from p in inputParmListLookup
                                              select $"{p.Name}{(p.HasDefault ? "?" : "")}: {p.TypescriptDataType}";


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


                    if (method.MethodInfo.ReturnType == typeof(void)) // no result
                    {
                        //IServerMethodVoid<OuputParameters, InputParameters>
                        methodLineTSD = methodLineTSD.Replace("<<RET_TYPE>>", $"IServerMethodVoid<{typeNameOutputParms}, {typeNameInputParms}>");
                    }
                    else
                    {
                        //IServerMethod<OuputParameters, ResultType, InputParameters>

                        var retType = GlobalTypescriptTypeLookup.GetTypescriptTypeFromCSharp(method.MethodInfo.ReturnType);

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

        public static (string /*js*/, string/*TSD*/) GenerateOutputFiles(OM.Application app, IEnumerable<ServerMethodPluginRegistration> registrations)
        {
            // TODO: The merged dictionaries can be cached per App and only recalculated each time the plugin config on an app changes? (or inline methods are recompiled)
            var combinedJS = new Dictionary<string/*Namespace*/, List<Definition>>();
            var combinedTSD = new Dictionary<string/*Namespace*/, List<Definition>>();

            foreach (var reg in registrations)
            {
                if (!app.IsPluginIncluded(reg.PluginGuid)) continue;

                foreach (var namespaceKV in reg.JavaScriptDefinitions)
                {
                    // js
                    foreach (var definition in namespaceKV.Value)
                    {
                        if (!combinedJS.ContainsKey(namespaceKV.Key))
                        {
                            combinedJS.Add(namespaceKV.Key, new List<Definition>());
                        }

                        if (combinedJS[namespaceKV.Key].FirstOrDefault(m => m.MethodName.Equals(definition.MethodName, StringComparison.Ordinal)) != null)
                        {
                            // TODO: Consider allowing overloads
                            SessionLog.Warning($"{app.Project.Name}/{app.Name} - ServerMethods - conflicting method name '{definition.MethodName}'.");
                            continue;
                        }

                        combinedJS[namespaceKV.Key].Add(definition);
                    }
                }

                // tsd
                foreach (var namespaceKV in reg.TypescriptDefinitions)
                {
                    foreach (var definition in namespaceKV.Value)
                    {
                        if (!combinedTSD.ContainsKey(namespaceKV.Key))
                        {
                            combinedTSD.Add(namespaceKV.Key, new List<Definition>());
                        }

                        if (combinedTSD[namespaceKV.Key].FirstOrDefault(m => m.MethodName.Equals(definition.MethodName, StringComparison.Ordinal)) != null)
                        {
                            // just skip, should have already been handled on the .js side
                            continue;
                        }

                        combinedTSD[namespaceKV.Key].Add(definition);
                    }
                }

            }

            var sbJavascriptAll = new StringBuilder(ServerMethodManager.TEMPLATE_ServerMethodContainer);
            var sbTSDAll = new StringBuilder(ServerMethodManager.TEMPLATE_ServerMethodTypescriptDefinitionsContainer);

            var now = DateTime.Now;

            // JavaScript
            {
                var sbJS = new StringBuilder();

                foreach (var kv in combinedJS)
                {// kv.Key is the namespace
                    string objName = null;

                    if (kv.Key.Equals("ServerMethods", StringComparison.Ordinal))
                    {
                        objName = "var x = dal.ServerMethods";
                    }
                    else
                    {
                        objName = $"x.{kv.Key}";
                    }

                    sbJS.AppendLine($"\t{objName} = {{");

                    sbJS.Append(string.Join(",\r\n", kv.Value.Select(definition => "\t\t" + definition.Line).ToArray()));

                    sbJS.AppendLine("\r\n\t};\r\n");
                }

                var nsLookupArray = string.Join(',', combinedJS.Where(kv => kv.Key != "ServerMethods").Select(kv => $"\"{kv.Key}\"").ToArray());

                sbJavascriptAll.Replace("<<DATE>>", now.ToString("dd MMM yyyy, HH:mm"))
                    .Replace("<<NAMESPACE_LOOKUP>>", nsLookupArray)
                    .Replace("<<ROUTINES>>", sbJS.ToString())
                    .Replace("<<FILE_VERSION>>", "001") // TODO: not sure if we need a fileversion here?
                    ;
            }

            // TSD
            {
                var sbTSD = new StringBuilder();
                var sbTypeDefs = new StringBuilder();
                var sbComplexTypeDefs = new StringBuilder();

                foreach (var kv in combinedTSD)
                {
                    var insideCustomNamespace = false;

                    if (!kv.Key.Equals("ServerMethods", StringComparison.Ordinal))
                    {
                        insideCustomNamespace = true;

                        sbTSD.AppendLine($"\t\tstatic {kv.Key}: {{");
                    }

                    sbTSD.AppendLine(string.Join("\r\n", kv.Value.Select(definition => (insideCustomNamespace ? "\t" : "") + "\t\t" + definition.Line).ToArray()));

                    var typeDefLines = kv.Value.SelectMany(def => def.TypesLines).Where(typeDef => typeDef != null).Select(l => "\t\t" + l).ToArray();

                    sbTypeDefs.AppendLine(string.Join("\r\n", typeDefLines));

                    if (insideCustomNamespace)
                    {
                        sbTSD.AppendLine("\t\t};");
                    }
                }

                // TODO: this should only be for those types that apply to this particular App plugin inclusion..currently we generate types for EVERYTHING found 
                // TSD: build types for Complex types we picked up
                foreach (var def in GlobalTypescriptTypeLookup.Definitions)
                {
                    sbComplexTypeDefs.AppendLine($"\t\ttype {def.TypeName} = {def.Definition};");
                }

                sbTypeDefs.Insert(0, sbComplexTypeDefs);

                sbTSDAll.Replace("<<DATE>>", now.ToString("dd MMM yyyy, HH:mm"))
                    .Replace("<<ResultAndParameterTypes>>", sbTypeDefs.ToString().TrimEnd(new char[] { '\r', '\n' }))
                    .Replace("<<MethodsStubs>>", sbTSD.ToString())
                    .Replace("<<FILE_VERSION>>", "001") // TODO: not sure if we need a fileversion here?
                    ;
            }

            return (sbJavascriptAll.ToString(), sbTSDAll.ToString());
        }
    }
}