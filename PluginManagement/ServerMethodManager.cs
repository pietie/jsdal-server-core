using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;
using System.Collections.ObjectModel;
using System.Text;
using System.IO;
using OM = jsdal_server_core.Settings.ObjectModel;


namespace jsdal_server_core
{
    class ServerMethodRegistration
    {

        private Dictionary<string, List<Definition>> JavaScriptDefinitions;
        private Dictionary<string, List<Definition>> TypescriptDefinitions;
        private byte[] JavaScriptDefinitionsHash;
        private byte[] TypescriptDefinitionsHash;

        public ServerMethodRegistration(ServerMethodPlugin pluginInstance, Guid pluginGuid)
        {
            _methods = new List<ServerMethodRegistrationMethod>();
            this.Methods = _methods.AsReadOnly();
            this.PluginInstance = pluginInstance;
            this.PluginGuid = pluginGuid.ToString();
        }
        public ServerMethodPlugin PluginInstance { get; private set; }

        public string PluginGuid { get; private set; }

        private readonly List<ServerMethodRegistrationMethod> _methods;
        public ReadOnlyCollection<ServerMethodRegistrationMethod> Methods { get; private set; }

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

        private void GenerateAndCacheJsInterface()
        {
            try
            {
                var definitionsJS = JavaScriptDefinitions = new Dictionary<string, List<Definition>>();
                var definitionsTSD = TypescriptDefinitions = new Dictionary<string, List<Definition>>();

                JavaScriptDefinitionsHash = TypescriptDefinitionsHash = null;

                // add default namespace 
                definitionsJS.Add("ServerMethods", new List<Definition>());
                definitionsTSD.Add("ServerMethods", new List<Definition>());

                foreach (var method in this.Methods)
                {
                    var namespaceKey = "ServerMethods";
                    var namespaceKeyTSD = "ServerMethods";

                    if (!string.IsNullOrEmpty(method.Namespace))
                    {
                        //namespaceKey += $".{method.Namespace}";
                        namespaceKey = method.Namespace;
                        namespaceKeyTSD = method.Namespace;
                    }

                    var isCustomNamespace = !namespaceKey.Equals("ServerMethods", StringComparison.Ordinal);

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
                    var methodLineJS = ServerMethodManager.TEMPLATE_ServerMethodFunctionTemplate.Replace("<<FUNC_NAME>>", method.Name);
                    // var parmListJS = string.Join(", ", (from parm in methodParameters
                    //                                     select parm.Name).ToArray());

                    // methodLineJS = methodLineJS.Replace("<<ARG_SEP>>", (parmListJS.Length > 0) ? ", " : "");

                    // methodLineJS = methodLineJS.Replace("<<PARM_LIST>>", parmListJS);

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

                    // TODO: Compare with current Hashes

                    this.JavaScriptDefinitionsHash = jsHash;
                    this.TypescriptDefinitionsHash = tsdHash;
                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public void Process(MethodInfo[] methods, PluginInfo pluginInfo, string classLevelNamespace)
        {
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

        public static (string /*js*/, string/*TSD*/) GenerateOutputFiles(OM.Application app, IEnumerable<ServerMethodRegistration> registrations)
        {
            // TODO: The merged dictionaries can be cached per App and only recalculated each time the plugin config on an app changes? (or inline methods are recompiled)
            var combinedJS = new Dictionary<string/*Namespace*/, List<Definition>>();
            var combinedTSD = new Dictionary<string/*Namespace*/, List<Definition>>();

            foreach (var reg in registrations)
            {
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
                {
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

                sbJavascriptAll.Replace("<<DATE>>", now.ToString("dd MMM yyyy, HH:mm"))
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
                    .Replace("<<ResultAndParameterTypes>>", sbTypeDefs.ToString())
                    .Replace("<<MethodsStubs>>", sbTSD.ToString())
                    .Replace("<<FILE_VERSION>>", "001") // TODO: not sure if we need a fileversion here?
                    ;
            }

            return (sbJavascriptAll.ToString(), sbTSDAll.ToString());
        }
    }

    class ServerMethodRegistrationMethod
    {
        public ServerMethodRegistrationMethod(ServerMethodRegistration reg)
        {
            this.Registration = reg;
        }

        public ServerMethodRegistration Registration { get; private set; }

        public string Namespace { get; set; } // inherited if null and class level is specified otherwhise none
        public string Name { get; set; }
        public MethodInfo MethodInfo { get; set; }

        public void Execute(/*TODO: Parms */)
        {
            try
            {
                // ...
                //this.MethodInfo.Invoke(PluginInstance, ...);
            }
            catch (Exception ex)
            {
                // TODO: !!!
            }
        }


    }

    public static class ServerMethodManager
    {
        private static List<ServerMethodRegistration> Registrations { get; set; }

        public static string TEMPLATE_ServerMethodContainer { get; private set; }
        public static string TEMPLATE_ServerMethodFunctionTemplate { get; private set; }
        public static string TEMPLATE_ServerMethodTypescriptDefinitionsContainer { get; private set; }

        static ServerMethodManager()
        {
            try
            {
                Registrations = new List<ServerMethodRegistration>();


                ServerMethodManager.TEMPLATE_ServerMethodContainer = File.ReadAllText("./resources/ServerMethodContainer.txt");
                ServerMethodManager.TEMPLATE_ServerMethodFunctionTemplate = File.ReadAllText("./resources/ServerMethodTemplate.txt");
                ServerMethodManager.TEMPLATE_ServerMethodTypescriptDefinitionsContainer = File.ReadAllText("./resources/ServerMethodsTSDContainer.d.ts");
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public static void Register(PluginInfo pluginInfo)
        {
            try
            {
                // create new instance
                var serverMethodPlugin = (ServerMethodPlugin)pluginInfo.Assembly.CreateInstance(pluginInfo.TypeInfo.FullName);

                var classLevelAttrib = serverMethodPlugin.GetType().GetCustomAttribute(typeof(ServerMethodAttribute)) as ServerMethodAttribute;

                // TODO: Not sure if we want to support Static methods...or ONLY static?
                var methods = serverMethodPlugin.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

                var reg = new ServerMethodRegistration(serverMethodPlugin, pluginInfo.Guid);

                Registrations.Add(reg);

                reg.Process(methods, pluginInfo, classLevelAttrib?.Namespace);
            }
            catch (Exception ex)
            {
                SessionLog.Error($"Failed to instantiate plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. See exception that follows.");
                SessionLog.Exception(ex);
            }
        }

        public static void GenerateJavascript()
        {
            try
            {
                var appCollection = Settings.SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications);

                foreach (var app in appCollection)
                {
                    // find all registered ServerMethods for this app
                    var registrations = ServerMethodManager.Registrations.Where(reg => app.IsPluginIncluded(reg.PluginGuid));

                    if (registrations.Count() > 0)
                    {
                        try
                        {
                            var (js, tsd) = ServerMethodRegistration.GenerateOutputFiles(app, registrations);

                            // TODO: Persist somewhere, ready to serve
                            File.WriteAllText("./data/Test.js", js);
                            File.WriteAllText("./data/Test.d.ts", tsd);

                        }
                        catch (Exception ex)
                        {
                            SessionLog.Error($"Failed to generate ServerMethod output files for {app.Project.Name}/{app.Name}.See exception that follows.");
                            SessionLog.Exception(ex);
                        }
                    }

                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public static void Execute()
        {

        }
    }

}