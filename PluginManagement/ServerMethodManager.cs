using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;
using System.Collections.ObjectModel;
using System.Text;
using System.IO;

namespace jsdal_server_core
{
    class ServerMethodRegistration
    {

        private Dictionary<string, List<string>> JavaScriptDefinitions;
        private Dictionary<string, List<string>> TypescriptDefinitions;
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

        private string GenerateAndCacheJsInterface()
        {
            try
            {
                var sbJavascriptAll = new StringBuilder(ServerMethodManager.TEMPLATE_ServerMethodContainer);
                var sbTSDAll = new StringBuilder(ServerMethodManager.TEMPLATE_ServerMethodTypescriptDefinitionsContainer);


                var definitionsJS = JavaScriptDefinitions = new Dictionary<string, List<string>>();
                var definitionsTSD = TypescriptDefinitions = new Dictionary<string, List<string>>();

                JavaScriptDefinitionsHash = TypescriptDefinitionsHash = null;

                // add default namespace 
                definitionsJS.Add("ServerMethods", new List<string>());
                definitionsTSD.Add("ServerMethods", new List<string>());

                foreach (var method in this.Methods)
                {
                    var namespaceKey = "ServerMethods";
                    var namespaceKeyTSD = "ServerMethods";

                    if (!string.IsNullOrEmpty(method.Namespace))
                    {
                        namespaceKey += $".{method.Namespace}";
                        namespaceKeyTSD = method.Namespace;
                    }

                    var isCustomNamespace = !namespaceKeyTSD.Equals("ServerMethods", StringComparison.Ordinal);

                    if (!definitionsJS.ContainsKey(namespaceKey))
                    {
                        definitionsJS.Add(namespaceKey, new List<string>());
                    }

                    if (!definitionsTSD.ContainsKey(namespaceKeyTSD))
                    {
                        definitionsTSD.Add(namespaceKeyTSD, new List<string>());
                    }

                    var methodParameters = method.MethodInfo.GetParameters();

                    // js
                    var methodLineJS = ServerMethodManager.TEMPLATE_ServerMethodFunctionTemplate.Replace("<<FUNC_NAME>>", method.Name);
                    var parmListJS = string.Join(", ", (from parm in methodParameters
                                                        select parm.Name).ToArray());

                    methodLineJS = methodLineJS.Replace("<<ARG_SEP>>", (parmListJS.Length > 0) ? ", " : "");

                    methodLineJS = methodLineJS.Replace("<<PARM_LIST>>", parmListJS);

                    definitionsJS[namespaceKey].Add(methodLineJS);

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

                    var parmListTSD = string.Join(", ", (from parm in methodParameters
                                                         select $"{parm.Name}:{Settings.ObjectModel.RoutineParameterV2.GetTypescriptTypeFromCSharp(parm.ParameterType)}"
                                                         ).ToArray());

                    methodLineTSD = methodLineTSD.Replace("<<PARM_LIST>>", parmListTSD);

                    methodLineTSD = methodLineTSD.Replace("<<RET_TYPE>>", Settings.ObjectModel.RoutineParameterV2.GetTypescriptTypeFromCSharp(method.MethodInfo.ReturnType));

                    definitionsTSD[namespaceKeyTSD].Add(methodLineTSD);
                }


                var jsLines = string.Join("\n", definitionsJS.Select(kv => $"{kv.Key}§{string.Join('\n', kv.Value.ToArray())}").ToArray());
                var tsdLines = string.Join("\n", definitionsTSD.Select(kv => $"{kv.Key}§{string.Join('\n', kv.Value.ToArray())}").ToArray());

                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var jsHash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(jsLines));
                    var tsdHash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(tsdLines));

                    // TODO: Compare with current Hashes

                    this.JavaScriptDefinitionsHash = jsHash;
                    this.TypescriptDefinitionsHash = tsdHash;
                }



                return null;

                var now = DateTime.Now;

                // JavaScript
                {
                    var sbJS = new StringBuilder();

                    foreach (var kv in definitionsJS)
                    {

                        sbJS.AppendLine($"\t{kv.Key} = {{");

                        sbJS.Append(string.Join(",\r\n", kv.Value.Select(l => "\t\t" + l).ToArray()));

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

                    foreach (var kv in definitionsTSD)
                    {
                        var insideCustomNamespace = false;

                        if (!kv.Key.Equals("ServerMethods", StringComparison.Ordinal))
                        {
                            insideCustomNamespace = true;

                            sbTSD.AppendLine($"\t\tstatic {kv.Key}: {{");
                        }

                        sbTSD.Append(string.Join("\r\n", kv.Value.Select(l => (insideCustomNamespace ? "\t" : "") + "\t\t" + l).ToArray()));

                        if (insideCustomNamespace)
                        {
                            sbTSD.AppendLine("\r\n\t\t};\r\n");
                        }
                    }

                    sbTSDAll.Replace("<<DATE>>", now.ToString("dd MMM yyyy, HH:mm"))
                        .Replace("<<MethodsStubs>>", sbTSD.ToString())
                        .Replace("<<FILE_VERSION>>", "001") // TODO: not sure if we need a fileversion here?
                        ;
                }

                File.WriteAllText("./data/Test.js", sbJavascriptAll.ToString());
                File.WriteAllText("./data/Test.d.ts", sbTSDAll.ToString());

                return sbJavascriptAll.ToString();
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                // TODO: Return JS that console.errors some generic failure notice?
                return null;
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

        public static (string /*Js*/, string/*TSD*/) GenerateOutputFiles(IEnumerable<ServerMethodRegistration> registrations)
        {
            // TODO: Merge dictionaries and then build outputs
            // TODO: Also warning/error on method name conflicts
  
            return (null, null);
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
                        var (js, tsd) = ServerMethodRegistration.GenerateOutputFiles(registrations);
                    }


                    // foreach (var reg in registrations)
                    // {
                    //     var s = reg.GenerateMethodJavascript();
                    // }
                }

                // foreach (var reg in ServerMethodManager.Registrations)
                // {
                //     // find apps that have it registered
                //     var appCollection = Settings.SettingsInstance.Instance.ProjectList.SelectMany(p=>p.Applications).Where(app=>app.IsPluginIncluded(reg.PluginGuid));

                //     foreach(var app in appCollection)
                //     {
                //         reg.

                //     }
                // }
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