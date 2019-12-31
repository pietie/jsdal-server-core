using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;
using System.Collections.ObjectModel;
using System.Text;
using OM = jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core.PluginManagement
{
    // Represents a single instance of a class that derives from one of the plugin classes (ServerMethodPlugin, ExecutionPlugin)
    // An assembly may contain multiples of these
    public class ServerMethodPluginRegistration
    {
        public Assembly Assembly { get; private set; }

        // TODO: Duplicate of what ServerMethodManager does?
        //private static Dictionary<string, List<ServerMethodPluginRegistration>> GlobalRegistrations = new Dictionary<string, List<ServerMethodPluginRegistration>>();

        public TypeInfo TypeInfo { get; private set; }
        public string PluginGuid { get; private set; }
        private readonly List<ServerMethodRegistrationMethod> _methods;
        public ReadOnlyCollection<ServerMethodRegistrationMethod> Methods { get; private set; }

        public ServerMethodScriptGenerator ScriptGenerator { get; private set; }

        public string PluginAssemblyInstanceId { get; private set; }


        private ServerMethodPluginRegistration(Assembly assembly, TypeInfo typeInfo, Guid pluginGuid, string pluginAssemblyInstanceId)
        {
            _methods = new List<ServerMethodRegistrationMethod>();

            this.Methods = _methods.AsReadOnly();
            this.Assembly = assembly;
            this.TypeInfo = typeInfo;
            this.PluginGuid = pluginGuid.ToString();
            this.PluginAssemblyInstanceId = pluginAssemblyInstanceId;
        }

        public static ServerMethodPluginRegistration Create(string pluginAssemblyInstanceId, PluginInfo pluginInfo)
        {
            var reg = new ServerMethodPluginRegistration(pluginInfo.Assembly, pluginInfo.TypeInfo, pluginInfo.Guid, pluginAssemblyInstanceId);

            reg.ScriptGenerator = ServerMethodScriptGenerator.Create(pluginAssemblyInstanceId, pluginInfo);

            //!    reg.Process(pluginInfo);

            // lock (GlobalRegistrations)
            // {
            //     if (GlobalRegistrations.ContainsKey(pluginAssemblyInstanceId))
            //     {
            //         GlobalRegistrations[pluginAssemblyInstanceId].Add(reg);
            //     }
            //     else
            //     {
            //         GlobalRegistrations.Add(pluginAssemblyInstanceId, new List<ServerMethodPluginRegistration>() { reg });
            //     }
            // }

            return reg;
        }

        // // called when an inline assembly is updated
        // public static void HandleAssemblyUpdated(string pluginAssemblyInstanceId, List<PluginInfo> pluginList)
        // {
        //     lock (GlobalRegistrations)
        //     {
        //         if (GlobalRegistrations.ContainsKey(pluginAssemblyInstanceId))
        //         {
        //             ///GlobalConverterLookup.

        //             GlobalRegistrations[pluginAssemblyInstanceId].Clear();
        //         }

        //         pluginList.ForEach(pluginInfo =>
        //         {
        //             ServerMethodPluginRegistration.
        //             var reg = ServerMethodPluginRegistration.Create(pluginAssemblyInstanceId, pluginInfo);

        //         });
        //     }
        // }

        // private void Process(PluginInfo pluginInfo)
        // {
        //     var classLevelAttrib = pluginInfo.TypeInfo.GetCustomAttribute(typeof(ServerMethodAttribute)) as ServerMethodAttribute;

        //     // static methods not supported 
        //     var methods = pluginInfo.TypeInfo.GetMethods(BindingFlags.Public /* | BindingFlags.Static*/ | BindingFlags.Instance);

        //     string classLevelNamespace = classLevelAttrib?.Namespace;

        //     var serverMethodCollection = (from mi in methods select new { MethodInfo = mi, ServerMethodAttribute = mi.GetCustomAttribute(typeof(ServerMethodAttribute)) as ServerMethodAttribute })
        //                                       .Where(m => m.ServerMethodAttribute != null);

        //     if (serverMethodCollection.Count() == 0)
        //     {
        //         SessionLog.Warning($"No server method's found in plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. Add a [ServerMethod] attribute to the methods you want to expose.");
        //         return;
        //     }

        //     foreach (var m in serverMethodCollection)
        //     {
        //         string ns = m.ServerMethodAttribute?.Namespace;

        //         if (ns == null)
        //         {
        //             ns = classLevelNamespace;
        //         }

        //         this.AddMethod(m.MethodInfo.Name, ns, m.MethodInfo);
        //     }

        //     this.GenerateAndCacheJsInterface();
        // }

        // public ServerMethodRegistrationMethod AddMethod(string name, string nameSpace, MethodInfo methodInfo)
        // {
        //     var method = new ServerMethodRegistrationMethod(this);

        //     method.Name = name;
        //     method.Namespace = nameSpace;
        //     method.MethodInfo = methodInfo;

        //     this._methods.Add(method);

        //     return method;
        // }


        // private const string SERVER_TSD_METHOD_NONSTATIC_TEMPLATE = "<<FUNC_NAME>>(<<PARM_LIST>>): <<RET_TYPE>>;";
        // private const string SERVER_TSD_METHOD_TEMPLATE = "static <<FUNC_NAME>>(<<PARM_LIST>>): <<RET_TYPE>>;";

        // private class Definition
        // {
        //     public string MethodName { get; set; }
        //     public string Line { get; set; }
        //     public List<string> TypesLines { get; set; }

        //     public string InputConverter { get; set; }
        //     public string OutputConverter { get; set; }
        //     public string ResultsConverter { get; set; }
        // }


        // public static (string /*js*/, string/*TSD*/) GenerateOutputFiles(OM.Application app, IEnumerable<ServerMethodPluginRegistration> registrations)
        // {
        //     // TODO: The merged dictionaries can be cached per App and only recalculated each time the plugin config on an app changes? (or inline methods are recompiled)
        //     var combinedJS = new Dictionary<string/*Namespace*/, List<Definition>>();
        //     var combinedTSD = new Dictionary<string/*Namespace*/, List<Definition>>();
        //     var combinedConverterLookup = new List<string>();

        // foreach (var pluginReg in registrations)
        // {
        //     if (!app.IsPluginIncluded(pluginReg.PluginGuid)) continue;

        //     // converters
        //     foreach (var c in pluginReg.ConverterLookup)
        //     {
        //         if (combinedConverterLookup.IndexOf(c) == -1)
        //         {
        //             combinedConverterLookup.Add(c);
        //         }
        //     }

        //     foreach (var namespaceKV in pluginReg.JavaScriptDefinitions)
        //     {
        //         // js
        //         foreach (var definition in namespaceKV.Value)
        //         {
        //             if (!combinedJS.ContainsKey(namespaceKV.Key))
        //             {
        //                 combinedJS.Add(namespaceKV.Key, new List<Definition>());
        //             }

        //             if (combinedJS[namespaceKV.Key].FirstOrDefault(m => m.MethodName.Equals(definition.MethodName, StringComparison.Ordinal)) != null)
        //             {
        //                 // TODO: Consider allowing overloads
        //                 SessionLog.Warning($"{app.Project.Name}/{app.Name} - ServerMethods - conflicting method name '{definition.MethodName}'.");
        //                 continue;
        //             }

        //             var hasConverter = definition.InputConverter != null || definition.OutputConverter != null || definition.ResultsConverter != null;

        //             if (hasConverter)
        //             {
        //                 var convertersSB = new StringBuilder("{ ");
        //                 var lst = new List<string>();

        //                 string inputConverter = definition.InputConverter;
        //                 string outputConverter = definition.OutputConverter;
        //                 string resultConverter = definition.ResultsConverter;

        //                 foreach (var converterJson in combinedConverterLookup)
        //                 {
        //                     if (inputConverter != null)
        //                     {
        //                         inputConverter = inputConverter.Replace(converterJson, combinedConverterLookup.IndexOf(converterJson).ToString());
        //                     }

        //                     if (outputConverter != null)
        //                     {
        //                         outputConverter = outputConverter.Replace(converterJson, combinedConverterLookup.IndexOf(converterJson).ToString());
        //                     }

        //                     if (resultConverter != null)
        //                     {
        //                         resultConverter = resultConverter.Replace(converterJson, combinedConverterLookup.IndexOf(converterJson).ToString());
        //                     }
        //                 }

        //                 if (inputConverter != null)
        //                 {
        //                     lst.Add($"input: {{ {inputConverter} }}");
        //                 }

        //                 if (outputConverter != null)
        //                 {
        //                     lst.Add($"output: {{ {outputConverter} }}");
        //                 }

        //                 if (resultConverter != null)
        //                 {
        //                     lst.Add($"results: {{ {resultConverter} }}");
        //                 }

        //                 convertersSB.Append(string.Join(", ", lst));

        //                 convertersSB.Append(" }");


        //                 definition.Line = definition.Line.Replace("<<CONV_SEP>>", ", ");
        //                 definition.Line = definition.Line.Replace("<<CONVERTERS>>", convertersSB.ToString());
        //             }

        //             combinedJS[namespaceKV.Key].Add(definition);
        //         }
        //     }

        //     // tsd
        //     foreach (var namespaceKV in pluginReg.TypescriptDefinitions)
        //     {
        //         foreach (var definition in namespaceKV.Value)
        //         {
        //             if (!combinedTSD.ContainsKey(namespaceKV.Key))
        //             {
        //                 combinedTSD.Add(namespaceKV.Key, new List<Definition>());
        //             }

        //             if (combinedTSD[namespaceKV.Key].FirstOrDefault(m => m.MethodName.Equals(definition.MethodName, StringComparison.Ordinal)) != null)
        //             {
        //                 // just skip, should have already been handled on the .js side
        //                 continue;
        //             }

        //             combinedTSD[namespaceKV.Key].Add(definition);
        //         }
        //     }


        // } // foreach plugin


        // var sbJavascriptAll = new StringBuilder(ServerMethodManager.TEMPLATE_ServerMethodContainer);
        // var sbTSDAll = new StringBuilder(ServerMethodManager.TEMPLATE_ServerMethodTypescriptDefinitionsContainer);

        // var now = DateTime.Now;

        // // JavaScript
        // {
        //     var sbJS = new StringBuilder();

        //     foreach (var kv in combinedJS)
        //     {// kv.Key is the namespace
        //         string objName = null;

        //         if (kv.Key.Equals("ServerMethods", StringComparison.Ordinal))
        //         {
        //             objName = "var x = dal.ServerMethods";
        //         }
        //         else
        //         {
        //             objName = $"x.{kv.Key}";
        //         }

        //         sbJS.AppendLine($"\t{objName} = {{");

        //         sbJS.Append(string.Join(",\r\n", kv.Value.Select(definition => "\t\t" + definition.Line).ToArray()));

        //         sbJS.AppendLine("\r\n\t};\r\n");
        //     }

        //     var nsLookupArray = string.Join(',', combinedJS.Where(kv => kv.Key != "ServerMethods").Select(kv => $"\"{kv.Key}\"").ToArray());

        //     var converterLookupJS = string.Join(", ", combinedConverterLookup);

        //     sbJavascriptAll.Replace("<<DATE>>", now.ToString("dd MMM yyyy, HH:mm"))
        //         .Replace("<<NAMESPACE_LOOKUP>>", nsLookupArray)
        //         .Replace("<<CONVERTER_LOOKUP>>", converterLookupJS)
        //         .Replace("<<ROUTINES>>", sbJS.ToString())
        //         .Replace("<<FILE_VERSION>>", "001") // TODO: not sure if we need a fileversion here?
        //         ;
        // }

        // // TSD
        // {
        //     var sbTSD = new StringBuilder();
        //     var sbTypeDefs = new StringBuilder();
        //     var sbComplexTypeDefs = new StringBuilder();

        //     foreach (var kv in combinedTSD)
        //     {
        //         var insideCustomNamespace = false;

        //         if (!kv.Key.Equals("ServerMethods", StringComparison.Ordinal))
        //         {
        //             insideCustomNamespace = true;

        //             sbTSD.AppendLine($"\t\tstatic {kv.Key}: {{");
        //         }

        //         sbTSD.AppendLine(string.Join("\r\n", kv.Value.Select(definition => (insideCustomNamespace ? "\t" : "") + "\t\t" + definition.Line).ToArray()));

        //         var typeDefLines = kv.Value.SelectMany(def => def.TypesLines).Where(typeDef => typeDef != null).Select(l => "\t\t" + l).ToArray();

        //         sbTypeDefs.AppendLine(string.Join("\r\n", typeDefLines));

        //         if (insideCustomNamespace)
        //         {
        //             sbTSD.AppendLine("\t\t};");
        //         }
        //     }

        //     // TODO: this should only be for those types that apply to this particular App plugin inclusion..currently we generate types for EVERYTHING found 
        //     // TSD: build types for Complex types we picked up
        //     foreach (var def in GlobalTypescriptTypeLookup.Definitions)
        //     {
        //         sbComplexTypeDefs.AppendLine($"\t\ttype {def.TypeName} = {def.Definition};");
        //     }

        //     sbTypeDefs.Insert(0, sbComplexTypeDefs);

        //     sbTSDAll.Replace("<<DATE>>", now.ToString("dd MMM yyyy, HH:mm"))
        //         .Replace("<<ResultAndParameterTypes>>", sbTypeDefs.ToString().TrimEnd(new char[] { '\r', '\n' }))
        //         .Replace("<<MethodsStubs>>", sbTSD.ToString())
        //         .Replace("<<FILE_VERSION>>", "001") // TODO: not sure if we need a fileversion here?
        //         ;
        // }

        // return (sbJavascriptAll.ToString(), sbTSDAll.ToString());
        //}
    }
}