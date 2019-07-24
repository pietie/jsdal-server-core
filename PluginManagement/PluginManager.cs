using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using jsdal_plugin;
using OM = jsdal_server_core.Settings.ObjectModel;
using jsdal_server_core.PluginManagement;

namespace jsdal_server_core
{
    public class PluginManager
    {
        private readonly BackgroundThreadPluginManager _backgroundThreadManager;
        public PluginManager(BackgroundThreadPluginManager bgThreadManager)
        {
            this._backgroundThreadManager = bgThreadManager;
        }

        public static PluginManager Instance  // TODO: temp workaround for all the DI hoops
        {
            get; set;
        }

        public Dictionary<Assembly, List<PluginInfo>> PluginAssemblies { get; private set; }

        public void CompileListOfAvailablePlugins()
        {
            try
            {
                PluginAssemblies = new Dictionary<Assembly, List<PluginInfo>>();

                //TODO: Test LoadPluginsFromSource();

                if (Directory.Exists("./plugins"))
                {
                    var dllCollection = Directory.EnumerateFiles("plugins", "*.dll", SearchOption.TopDirectoryOnly);

                    foreach (var dll in dllCollection)
                    {
                        // skip jsdal-plugin base
                        if (dll.Equals("plugins\\jsdal-plugin.dll", StringComparison.OrdinalIgnoreCase)) continue;

                        LoadPluginDLL(dll);
                    }
                }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public void InitServerWidePlugins()
        {
            try
            {
                if (PluginAssemblies == null) return;

                var pluginInfoCollection = PluginAssemblies.SelectMany(a => a.Value).Where(pi => pi.Type == OM.PluginType.ServerMethod || pi.Type == OM.PluginType.BackgroundThread);

                foreach (var pluginInfo in pluginInfoCollection)
                {
                    // try
                    // {
                    if (pluginInfo.Type == OM.PluginType.BackgroundThread)
                    {
                        _backgroundThreadManager.Register(pluginInfo);
                    }
                    else if (pluginInfo.Type == OM.PluginType.ServerMethod)
                    {
                        ServerMethodManager.Register(pluginInfo);
                    }
                    // }
                    // catch (Exception ex)
                    // {
                    //     SessionLog.Error($"Failed to instantiate plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. See exception that follows.");
                    //     SessionLog.Exception(ex);
                    // }
                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        private void LoadPluginsFromSource()
        {
            try
            {
                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(TmpGetPluginSource());

                // TODO: CLeanup and find a better way to add the standard deps!
                var runtimeAssembly = Assembly.Load("System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
                var netstandardAssembly = Assembly.Load("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
                var collectionsAssemlby = Assembly.Load("System.Collections, Version=0.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");

                var pluginBasePath = "jsdal-plugin.dll";

                if (Debugger.IsAttached)
                {
                    pluginBasePath = "./../jsdal-plugin/bin/Debug/netcoreapp2.0/jsdal-plugin.dll";
                }

                var pluginBaseRef = MetadataReference.CreateFromFile(pluginBasePath);

                var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);


                string assemblyName = Path.GetRandomFileName();
                MetadataReference[] references = new MetadataReference[]
                {
                    MetadataReference.CreateFromFile(typeof(Object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Data.SqlClient.SqlConnection).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Data.Common.DbCommand).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Runtime.InteropServices.GuidAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.ComponentModel.Component).Assembly.Location),
                    pluginBaseRef,
                    MetadataReference.CreateFromFile(runtimeAssembly.Location),
                    MetadataReference.CreateFromFile(netstandardAssembly.Location),
                    MetadataReference.CreateFromFile(collectionsAssemlby.Location)
                };


                var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                                .WithUsings(new string[] { "System" });

                CSharpCompilation compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: compilationOptions
                    );


                using (var ms = new MemoryStream())
                {
                    EmitResult result = compilation.Emit(ms);

                    if (!result.Success)
                    {
                        IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                            diagnostic.IsWarningAsError ||
                            diagnostic.Severity == DiagnosticSeverity.Error);

                        foreach (Diagnostic diagnostic in failures)
                        {
                            Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                        }
                    }
                    else
                    {
                        ms.Seek(0, SeekOrigin.Begin);
                        Assembly assembly = Assembly.Load(ms.ToArray());

                        LoadPluginFromAssembly(assembly);
                    }
                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        // TODO: This needs to be sourcs from somewhere - a Project or DBSource or just system-wide.
        public string TmpGetPluginSource()
        {
            return @"
               using System;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using jsdal_plugin;

namespace Plugins
{
      [Guid(""2003F9A8-5707-4B79-A216-737A7F11EB83"")]
    public class TokenGuidAuthPlugin : jsDALPlugin
        {
            public TokenGuidAuthPlugin()
            {
                this.Name = ""TokenGuid Auth"";
                this.Description = ""Call LoginSetContextInfo for sproc authentication, using the 'tokenGuid' query string parameter."";
            }

            public override void OnConnectionOpened(SqlConnection con)
            {
                base.OnConnectionOpened(con);

                if (this.QueryString.ContainsKey(""tokenGuid""))
                {
                    var tg = Guid.Parse(this.QueryString[""tokenGuid""]);

                    var cmd = new SqlCommand
                    {
                        Connection = con,
                        CommandText = ""LoginSetContextInfo"",
                        CommandType = System.Data.CommandType.StoredProcedure
                    };

                    string sessionId = null;// TODO: get from cookies?


                    cmd.Parameters.Add(""@tokenGuid"", System.Data.SqlDbType.UniqueIdentifier).Value = tg;

                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

               ";
        }

        private void LoadPluginDLL(string filepath)
        {
            try
            {
                var pluginAssembly = Assembly.LoadFrom(filepath);

                LoadPluginFromAssembly(pluginAssembly);
            }
            catch (Exception ex)
            {
                SessionLog.Error("Failed to load plugin DLL '{0}'. See exception that follows.", filepath);
                SessionLog.Exception(ex);
            }
        }

        private void LoadPluginFromAssembly(Assembly pluginAssembly)
        {
            if (pluginAssembly.DefinedTypes != null)
            {
                var pluginTypeList = pluginAssembly.DefinedTypes.Where(typ => typ.IsSubclassOf(typeof(PluginBase))).ToList();

                if (pluginTypeList != null && pluginTypeList.Count > 0)
                {
                    foreach (var pluginType in pluginTypeList)
                    {
                        var pluginInfo = new PluginInfo();

                        try
                        {
                            var pluginData = pluginType.GetCustomAttribute(typeof(PluginDataAttribute)) as PluginDataAttribute;

                            if (pluginData == null)
                            {
                                SessionLog.Error($"Plugin '{pluginType.FullName}' from assembly '{pluginAssembly.FullName}' does not have a PluginData attribute defined on the class level. Add a jsdal_plugin.PluginDataAttribute to the class.");
                                continue;
                            }

                            if (!Guid.TryParse(pluginData.Guid, out var pluginGuid))
                            {
                                SessionLog.Error("Plugin '{0}' does not have a valid Guid value set on its PluginData attribute.", pluginType.FullName);
                                continue;
                            }

                            var conflict = PluginAssemblies.FirstOrDefault(a => a.Value.FirstOrDefault(pi => pi.Guid.Equals(pluginGuid)) != null);

                            if (conflict.Key != null)
                            {
                                var existingPI = conflict.Value.FirstOrDefault(pi => pi.Guid.Equals(pluginGuid));

                                if (existingPI != null)
                                {
                                    SessionLog.Error($"Plugin '{pluginType.FullName}' has a conflicting Guid. The conflict is on assembly {conflict.Key.FullName} and plugin '{existingPI.Name}' with Guid value {existingPI.Guid}.");
                                    continue;
                                }
                            }

                            if (pluginType.IsSubclassOf(typeof(ExecutionPlugin)))
                            {
                                pluginInfo.Type = OM.PluginType.Execution;
                            }
                            else if (pluginType.IsSubclassOf(typeof(BackgroundThreadPlugin)))
                            {
                                pluginInfo.Type = OM.PluginType.BackgroundThread;
                            }
                            else if (pluginType.IsSubclassOf(typeof(ServerMethodPlugin)))
                            {
                                pluginInfo.Type = OM.PluginType.ServerMethod;


                                // TODO: Additional validation: Look for at least on ServerMethod? otherwise just a warning?
                                //      What about unique names of ServerMethods?
                                //      Validate Custom Namespace validity (must be JavaScript safe)
                            }
                            else
                            {
                                SessionLog.Error($"Unknown plugin type '{pluginType.FullName}'.");
                                continue;
                            }
                            pluginInfo.Assembly = pluginAssembly;
                            pluginInfo.Name = pluginData.Name;
                            pluginInfo.Description = pluginData.Description;
                            pluginInfo.TypeInfo = pluginType;
                            pluginInfo.Guid = pluginGuid;

                            //pluginInfo.InitMethod = typeof(jsDALPlugin).GetMethod("InitPlugin", BindingFlags.NonPublic | BindingFlags.Instance);

                            SessionLog.Info("Plugin '{0}' ({1}) loaded. Assembly: {2}", pluginInfo.Name, pluginInfo.Guid, pluginAssembly.FullName);
                        }
                        catch (Exception ex)
                        {
                            SessionLog.Error("Failed to instantiate type '{0}'. See the exception that follows.", pluginType.FullName);
                            SessionLog.Exception(ex);
                        }

                        if (!PluginAssemblies.ContainsKey(pluginAssembly)) PluginAssemblies.Add(pluginAssembly, new List<PluginInfo>());
                        PluginAssemblies[pluginAssembly].Add(pluginInfo);
                    }
                }
                else
                {
                    SessionLog.Warning("Failed to find any jsDAL Server plugins in the assembly '{0}'. Make sure you have a public class available that derives from one of the plugin types.", pluginAssembly.Location);
                }
            }
            else
            {
                SessionLog.Warning("Failed to find any jsDAL Server plugins in the assembly '{0}'. Make sure you have a public class available that derives from one of the plugin types.", pluginAssembly.Location);
            }
        }
    }
}