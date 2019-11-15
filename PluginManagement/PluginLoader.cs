using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using jsdal_plugin;
using OM = jsdal_server_core.Settings.ObjectModel;
using jsdal_server_core.PluginManagement;
using System.Collections.ObjectModel;
using jsdal_server_core.Settings.ObjectModel;
using Newtonsoft.Json;

namespace jsdal_server_core
{
    public class PluginLoader
    {

        private static readonly string InlinePluginSourcePath = "./inline-plugins";
        private readonly BackgroundThreadPluginManager _backgroundThreadManager;
        public PluginLoader(BackgroundThreadPluginManager bgThreadManager)
        {
            _pluginAssemblies = new List<PluginAssembly>();
            PluginAssemblies = _pluginAssemblies.AsReadOnly();
            this._backgroundThreadManager = bgThreadManager;
        }

        public static PluginLoader Instance  // TODO: temp workaround for all the DI hoops
        {
            get; set;
        }
        private readonly List<PluginAssembly> _pluginAssemblies;
        public ReadOnlyCollection<PluginAssembly> PluginAssemblies { get; private set; }
        public void Init()
        {
            // load from plugin directory
            try
            {
                if (Directory.Exists("./plugins"))
                {
                    var dllCollection = Directory.EnumerateFiles("plugins", "*.dll", SearchOption.TopDirectoryOnly);

                    foreach (var dllPath in dllCollection)
                    {
                        // skip jsdal-plugin base
                        if (dllPath.Equals("plugins\\jsdal-plugin.dll", StringComparison.OrdinalIgnoreCase)) continue;

                        try
                        {
                            var pluginAssembly = Assembly.LoadFrom(dllPath);

                            ParseAndLoadPluginAssembly(pluginAssembly, null);
                        }
                        catch (Exception ee)
                        {
                            SessionLog.Error("Failed to load plugin DLL '{0}'. See exception that follows.", dllPath);
                            SessionLog.Exception(ee);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }

            // load inline assemblies
            try
            {
                foreach (var inlineEntry in InlineModuleManifest.Instance.Entries)
                {
                    var sourcePath = Path.Combine(InlinePluginSourcePath, inlineEntry.Id);

                    if (File.Exists(sourcePath))
                    {
                        var code = File.ReadAllText(sourcePath);
                        var assembly = CSharpCompilerHelper.CompileIntoAssembly(inlineEntry.Name, code, out var problems);


                        if ((problems != null && problems.Count == 0) && assembly != null)
                        {
                            try
                            {
                                ParseAndLoadPluginAssembly(assembly, inlineEntry.Id);
                            }
                            catch (Exception ee)
                            {
                                SessionLog.Error("Failed to load inline plugin assembly '{0}'. See exception that follows.", assembly.FullName);
                                SessionLog.Exception(ee);
                            }
                        }
                        else
                        {
                            SessionLog.Error($"Inline plugin {inlineEntry.Name} ({inlineEntry.Id}) failed to compile with the following error(s): {string.Join(", ", problems)}");
                            continue;
                        }

                    }
                    else
                    {
                        SessionLog.Error($"Inline module {inlineEntry.Name} not found at '{sourcePath}'");
                    }
                }

                // if (Directory.Exists(InlinePluginSourcePath))
                // {
                //     var inlineSourceFileCollection = Directory.EnumerateFiles(InlinePluginSourcePath, "*.cs", SearchOption.TopDirectoryOnly);

                //     foreach (var sourceFile in inlineSourceFileCollection)
                //     {
                //         var code = File.ReadAllText(sourceFile);
                //         var assembly = CSharpCompilerHelper.CompileIntoAssembly(mod.Name, code, out var problems);
                //     }

                //     var inlineAssemblies = InlinePluginManager.Instance.LoadInlineAssemblies();

                //     foreach (var assembly in inlineAssemblies)
                //     {
                //         try
                //         {
                //             ParseAndLoadPluginAssembly(assembly, true);
                //         }
                //         catch (Exception ee)
                //         {
                //             SessionLog.Error("Failed to load inline plugin assembly '{0}'. See exception that follows.", assembly.FullName);
                //             SessionLog.Exception(ee);
                //         }
                //     }

                //     //InstantiateInlinePlugins();
                // }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }

            // init server-wide types
            try
            {
                InitServerWidePlugins();
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        private void InitServerWidePlugins()
        {
            try
            {
                if (PluginAssemblies == null) return;

                var pluginInfoCollection = PluginAssemblies.SelectMany(a => a.Plugins).Where(pi => pi.Type == OM.PluginType.ServerMethod || pi.Type == OM.PluginType.BackgroundThread);

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

        // private void InstantiateInlinePlugins()
        // {
        //     // only instantiate valid modules
        //     foreach(var mod in InlinePluginManager.Instance.Modules?.Where(m=>m.IsValid))
        //     {
        //         // TODO: Instantiate based on type -- so againsts EPs where it makes sense
        //         // TODO: Register types ...
        //     }
        // }

        // private void CompileInlineSource(string source)
        // {
        //     try
        //     {
        //         CSharpParseOptions parseOptions = CSharpParseOptions.Default;
        //         SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, options: parseOptions);

        //         // TODO: Cleanup and find a better way to add the standard deps!
        //         var runtimeAssembly = Assembly.Load("System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
        //         var netstandardAssembly = Assembly.Load("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
        //         var collectionsAssemlby = Assembly.Load("System.Collections, Version=0.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");

        //         var pluginBasePath = "./plugins/jsdal-plugin.dll";

        //         if (Debugger.IsAttached)
        //         {
        //             pluginBasePath = "./../jsdal-plugin/bin/Debug/netcoreapp2.0/jsdal-plugin.dll";
        //         }

        //         var pluginBaseRef = MetadataReference.CreateFromFile(pluginBasePath);

        //         var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);

        //         string assemblyName = Path.GetRandomFileName();

        //         MetadataReference[] references = new MetadataReference[]
        //         {
        //             MetadataReference.CreateFromFile(typeof(Object).Assembly.Location),
        //             MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        //             MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
        //             MetadataReference.CreateFromFile(typeof(System.Data.SqlClient.SqlConnection).Assembly.Location),
        //             MetadataReference.CreateFromFile(typeof(System.Data.Common.DbCommand).Assembly.Location),
        //             MetadataReference.CreateFromFile(typeof(System.Runtime.InteropServices.GuidAttribute).Assembly.Location),
        //             MetadataReference.CreateFromFile(typeof(System.ComponentModel.Component).Assembly.Location),
        //             pluginBaseRef,
        //             MetadataReference.CreateFromFile(runtimeAssembly.Location),
        //             MetadataReference.CreateFromFile(netstandardAssembly.Location),
        //             MetadataReference.CreateFromFile(collectionsAssemlby.Location)
        //         };


        //         var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        //                         .WithUsings(new string[] { "System" });

        //         CSharpCompilation compilation = CSharpCompilation.Create(
        //             assemblyName,
        //             syntaxTrees: new[] { syntaxTree },
        //             references: references,
        //             options: compilationOptions
        //             );


        //         using (var ms = new MemoryStream())
        //         {
        //             EmitResult result = compilation.Emit(ms);

        //             if (!result.Success)
        //             {
        //                 IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
        //                     diagnostic.IsWarningAsError ||
        //                     diagnostic.Severity == DiagnosticSeverity.Error);

        //                 foreach (Diagnostic diagnostic in failures)
        //                 {
        //                     Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
        //                 }
        //             }
        //             else
        //             {
        //                 ms.Seek(0, SeekOrigin.Begin);

        //                 Assembly assembly = Assembly.Load(ms.ToArray());

        //                 ParseAndLoadPluginAssembly(assembly);
        //             }
        //         }

        //     }
        //     catch (Exception ex)
        //     {
        //         SessionLog.Exception(ex);
        //     }
        // }

        // TODO: This needs to be sourcs from somewhere - a Project or DBSource or just system-wide.
        //         public string TmpGetPluginSource()
        //         {
        //             return @"
        //                using System;
        // using System.Data.SqlClient;
        // using System.Runtime.InteropServices;
        // using jsdal_plugin;

        // namespace Plugins
        // {
        //       [Guid(""2003F9A8-5707-4B79-A216-737A7F11EB83"")]
        //     public class TokenGuidAuthPlugin : jsDALPlugin
        //         {
        //             public TokenGuidAuthPlugin()
        //             {
        //                 this.Name = ""TokenGuid Auth"";
        //                 this.Description = ""Call LoginSetContextInfo for sproc authentication, using the 'tokenGuid' query string parameter."";
        //             }

        //             public override void OnConnectionOpened(SqlConnection con)
        //             {
        //                 base.OnConnectionOpened(con);

        //                 if (this.QueryString.ContainsKey(""tokenGuid""))
        //                 {
        //                     var tg = Guid.Parse(this.QueryString[""tokenGuid""]);

        //                     var cmd = new SqlCommand
        //                     {
        //                         Connection = con,
        //                         CommandText = ""LoginSetContextInfo"",
        //                         CommandType = System.Data.CommandType.StoredProcedure
        //                     };

        //                     string sessionId = null;// TODO: get from cookies?


        //                     cmd.Parameters.Add(""@tokenGuid"", System.Data.SqlDbType.UniqueIdentifier).Value = tg;

        //                     cmd.ExecuteNonQuery();
        //                 }
        //             }
        //         }
        //     }

        //                ";
        //         }


        private void ParseAndLoadPluginAssembly(Assembly pluginAssembly, string inlineEntryId = null)
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

                            var conflict = PluginAssemblies.SelectMany(a => a.Plugins).FirstOrDefault(p => p.Guid.Equals(pluginGuid));

                            if (conflict != null)
                            {
                                SessionLog.Error($"Plugin '{pluginType.FullName}' has a conflicting Guid. The conflict is on assembly {conflict.TypeInfo.FullName} and plugin '{conflict.Name}' with Guid value {conflict.Guid}.");
                                continue;

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

                            SessionLog.Info("Plugin '{0}' ({1}) loaded. Assembly: {2}", pluginInfo.Name, pluginInfo.Guid, pluginAssembly.FullName);
                        }
                        catch (Exception ex)
                        {
                            SessionLog.Error("Failed to instantiate type '{0}'. See the exception that follows.", pluginType.FullName);
                            SessionLog.Exception(ex);
                        }

                        var existing = PluginAssemblies.FirstOrDefault(a => a.Assembly == pluginAssembly);

                        if (existing == null)
                        {
                            var newPA = new PluginAssembly(pluginAssembly, inlineEntryId);
                            newPA.AddPlugin(pluginInfo);
                            _pluginAssemblies.Add(newPA);
                        }
                        else
                        {
                            existing.AddPlugin(pluginInfo);
                        }
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

        public CommonReturnValue GetInlinePluginModuleSource(string instanceId, out string source)
        {
            source = null;

            var pluginAssembly = this.PluginAssemblies.FirstOrDefault(p => p.InstanceId.Equals(instanceId, StringComparison.Ordinal) && p.IsInline);

            if (pluginAssembly == null)
            {
                return CommonReturnValue.UserError($"An inline module with the instance Id '{instanceId}' does not exist");
            }

            try
            {
                var existingInlineEntry = InlineModuleManifest.Instance.GetEntryById(pluginAssembly.InlineEntryId);
                var sourcePath = System.IO.Path.Combine(InlinePluginSourcePath, existingInlineEntry.Id);

                if (System.IO.File.Exists(sourcePath))
                {
                    source = System.IO.File.ReadAllText(sourcePath);
                    return CommonReturnValue.Success();
                }
                else
                {
                    return CommonReturnValue.UserError($"Failed to find source at: {sourcePath}");
                }
            }
            catch (Exception e)
            {
                SessionLog.Warning("Failed to fetch file of plugin module with InstanceId = {0}; {1}", pluginAssembly.InstanceId, pluginAssembly.Assembly.FullName);
                SessionLog.Exception(e);
            }

            return CommonReturnValue.Success();
        }
    }

    public class InlineModuleManifest
    {
        private static readonly string InlinePluginManifestPath = "./data/inline-plugins.json";

        private List<InlineModuleManifestEntry> _entries;
        private static InlineModuleManifest _instance;
        public ReadOnlyCollection<InlineModuleManifestEntry> Entries { get; private set; }



        public static InlineModuleManifest Instance
        {
            get
            {
                lock (InlinePluginManifestPath)
                {
                    if (_instance == null) _instance = new InlineModuleManifest();
                    return _instance;
                }
            }
        }
        private InlineModuleManifest()
        {
            this.Load();
        }

        private void Load()
        {
            if (File.Exists(InlinePluginManifestPath))
            {
                var json = File.ReadAllText(InlinePluginManifestPath);

                var entries = JsonConvert.DeserializeObject<InlineModuleManifestEntry[]>(json);

                _entries = new List<InlineModuleManifestEntry>(entries);
                Entries = _entries.AsReadOnly();
            }
            else
            {
                _entries = new List<InlineModuleManifestEntry>();
                Entries = _entries.AsReadOnly();
            }
        }

        private void Save()
        {
            var path = Path.GetFullPath(InlinePluginManifestPath);
            var fi = new FileInfo(path);

            if (!fi.Directory.Exists)
            {
                fi.Directory.Create();
            }

            var json = System.Text.Json.JsonSerializer.Serialize(this.Entries.ToArray());

            File.WriteAllText(path, json);
        }

        public InlineModuleManifestEntry GetEntryById(string id)
        {
            if (this.Entries == null) return null;
            return this.Entries.FirstOrDefault(e => e.Id.Equals(id));
        }

        public void AddUpdateModuleAsync(string id, string name, string description, string code)
        {
            // TODO: Existing check .... 
        }
    }

    public class InlineModuleManifestEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class PluginAssembly
    {
        public Assembly Assembly { get; private set; }

        public string InlineEntryId { get; private set; }
        public bool IsInline
        {
            get
            {
                return !string.IsNullOrWhiteSpace(InlineEntryId);
            }
        }

        private readonly List<PluginInfo> _plugins;
        public ReadOnlyCollection<PluginInfo> Plugins { get; private set; }

        public string InstanceId { get; private set; }

        public PluginAssembly(Assembly assembly, string inlineEntryId = null)
        {
            this.Assembly = assembly;
            this.InlineEntryId = inlineEntryId;
            this._plugins = new List<PluginInfo>();
            this.Plugins = _plugins.AsReadOnly();
            this.InstanceId = shortid.ShortId.Generate(true, false, 3);
        }

        public void AddPlugin(PluginInfo plugin)
        {
            this._plugins.Add(plugin);
        }
    }

    public class InlinePluginAssembly
    {
        public string FileId { get; set; }
    }

}