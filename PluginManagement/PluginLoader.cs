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
using System.Threading.Tasks;

namespace jsdal_server_core
{
    public class PluginLoader
    {

        public static readonly string InlinePluginSourcePath = "./inline-plugins";
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
        public async Task InitAsync()
        {
            // load from plugin directory
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
                {
                    try
                    {
                        var asmName = new AssemblyName(e.Name);
                        var requestingLocation = e.RequestingAssembly.Location;
                        var requestingDir = Path.GetDirectoryName(requestingLocation);

                        // look for a dll in the same location as the requesting assembly
                        var path = Path.Combine(requestingDir, asmName.Name + ".dll");

                        if (!File.Exists(path)) return null;

                        Assembly.LoadFrom(path);

                        return null;
                    }
                    catch
                    {
                        return null;
                    }
                };

                if (Directory.Exists("./plugins"))
                {
                    var dllCollection = Directory.EnumerateFiles("plugins", "*.dll", SearchOption.TopDirectoryOnly);

                    foreach (var dllPath in dllCollection)
                    {
                        // skip jsdal-plugin base
                        if (dllPath.Equals("plugins\\jsdal-plugin.dll", StringComparison.OrdinalIgnoreCase)) continue;

                        try
                        {
                            //var asmBytes = await File.ReadAllBytesAsync(dllPath);
                            //var pluginAssembly = Assembly.Load(asmBytes);
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
                        var code = await File.ReadAllTextAsync(sourcePath);
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

                foreach (var pluginAssembly in PluginAssemblies)
                {
                    var pluginInfoCollection = pluginAssembly.Plugins.Where(p => p.Type == OM.PluginType.ServerMethod || p.Type == OM.PluginType.BackgroundThread);

                    foreach (var pluginInfo in pluginInfoCollection)
                    {
                        if (pluginInfo.Type == OM.PluginType.BackgroundThread)
                        {
                            _backgroundThreadManager.Register(pluginInfo);
                        }
                        else if (pluginInfo.Type == OM.PluginType.ServerMethod)
                        {
                            ServerMethodManager.Register(pluginAssembly.InstanceId, pluginInfo);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }


        // parses an Assembly and checks for Plugin-type interface. If found each of those interfaces are tested for validity in terms of mandatory Attribute values and uniqueness
        private bool ParsePluginAssembly(Assembly pluginAssembly, out List<PluginInfo> pluginInfoList, out List<string> errorList, bool checkForConflict = true)
        {
            errorList = new List<string>();
            pluginInfoList = new List<PluginInfo>();

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
                                errorList.Add($"Plugin '{pluginType.FullName}' from assembly '{pluginAssembly.FullName}' does not have a PluginData attribute defined on the class level. Add a jsdal_plugin.PluginDataAttribute to the class.");
                                continue;
                            }

                            if (!Guid.TryParse(pluginData.Guid, out var pluginGuid))
                            {
                                errorList.Add($"Plugin '{pluginType.FullName}' does not have a valid Guid value set on its PluginData attribute.");
                                continue;
                            }

                            if (checkForConflict)
                            {
                                var conflict = PluginAssemblies.SelectMany(a => a.Plugins).FirstOrDefault(p => p.Guid.Equals(pluginGuid));

                                if (conflict != null)
                                {
                                    errorList.Add($"Plugin '{pluginType.FullName}' has a conflicting Guid. The conflict is on assembly {conflict.TypeInfo.FullName} and plugin '{conflict.Name}' with Guid value {conflict.Guid}.");
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
                                errorList.Add($"Unknown plugin type '{pluginType.FullName}'.");
                                continue;
                            }

                            pluginInfo.Assembly = pluginAssembly;
                            pluginInfo.Name = pluginData.Name;
                            pluginInfo.Description = pluginData.Description;
                            pluginInfo.TypeInfo = pluginType;
                            pluginInfo.Guid = pluginGuid;

                            //errorList.Add($"Plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) loaded. Assembly: {pluginAssembly.FullName}");
                            pluginInfoList.Add(pluginInfo);
                        }
                        catch (Exception ex)
                        {
                            errorList.Add("Failed to instantiate type '{pluginType.FullName}'.");
                            errorList.Add(ex.ToString());

                            SessionLog.Error("Failed to instantiate type '{0}'. See the exception that follows.", pluginType.FullName);
                            SessionLog.Exception(ex);
                        }

                        /*******
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
                                                */
                    }
                }
                else
                {
                    errorList.Add($"Failed to find any jsDAL Server plugins in the assembly '{pluginAssembly.Location}'. Make sure you have a public class available that derives from one of the plugin types.");
                }
            }
            else
            {
                errorList.Add($"Failed to find any jsDAL Server plugins in the assembly '{pluginAssembly.Location}'. Make sure you have a public class available that derives from one of the plugin types.");
            }

            return errorList == null || errorList.Count == 0;
        }


        private void ParseAndLoadPluginAssembly(Assembly assembly, string inlineEntryId = null)
        {
            if (ParsePluginAssembly(assembly, out var pluginInfoList, out var errorList, checkForConflict: true))
            {
                foreach (var pluginInfo in pluginInfoList)
                {
                    SessionLog.Info("Plugin '{0}' ({1}) loaded. Assembly: {2}", pluginInfo.Name, pluginInfo.Guid, assembly.FullName);

                    var existing = PluginAssemblies.FirstOrDefault(a => a.Assembly == assembly);


                    if (existing == null)
                    {
                        var newPA = new PluginAssembly(assembly, inlineEntryId);
                        newPA.AddPlugin(pluginInfo);
                        _pluginAssemblies.Add(newPA);
                    }
                    else
                    {
                        existing.AddPlugin(pluginInfo);
                    }
                }
            }

            // if (pluginAssembly.DefinedTypes != null)
            // {
            //     var pluginTypeList = pluginAssembly.DefinedTypes.Where(typ => typ.IsSubclassOf(typeof(PluginBase))).ToList();

            //     if (pluginTypeList != null && pluginTypeList.Count > 0)
            //     {
            //         foreach (var pluginType in pluginTypeList)
            //         {
            //             var pluginInfo = new PluginInfo();

            //             try
            //             {
            //                 var pluginData = pluginType.GetCustomAttribute(typeof(PluginDataAttribute)) as PluginDataAttribute;

            //                 if (pluginData == null)
            //                 {
            //                     SessionLog.Error($"Plugin '{pluginType.FullName}' from assembly '{pluginAssembly.FullName}' does not have a PluginData attribute defined on the class level. Add a jsdal_plugin.PluginDataAttribute to the class.");
            //                     continue;
            //                 }

            //                 if (!Guid.TryParse(pluginData.Guid, out var pluginGuid))
            //                 {
            //                     SessionLog.Error("Plugin '{0}' does not have a valid Guid value set on its PluginData attribute.", pluginType.FullName);
            //                     continue;
            //                 }

            //                 var conflict = PluginAssemblies.SelectMany(a => a.Plugins).FirstOrDefault(p => p.Guid.Equals(pluginGuid));

            //                 if (conflict != null)
            //                 {
            //                     SessionLog.Error($"Plugin '{pluginType.FullName}' has a conflicting Guid. The conflict is on assembly {conflict.TypeInfo.FullName} and plugin '{conflict.Name}' with Guid value {conflict.Guid}.");
            //                     continue;

            //                 }

            //                 if (pluginType.IsSubclassOf(typeof(ExecutionPlugin)))
            //                 {
            //                     pluginInfo.Type = OM.PluginType.Execution;
            //                 }
            //                 else if (pluginType.IsSubclassOf(typeof(BackgroundThreadPlugin)))
            //                 {
            //                     pluginInfo.Type = OM.PluginType.BackgroundThread;
            //                 }
            //                 else if (pluginType.IsSubclassOf(typeof(ServerMethodPlugin)))
            //                 {
            //                     pluginInfo.Type = OM.PluginType.ServerMethod;


            //                     // TODO: Additional validation: Look for at least on ServerMethod? otherwise just a warning?
            //                     //      What about unique names of ServerMethods?
            //                     //      Validate Custom Namespace validity (must be JavaScript safe)
            //                 }
            //                 else
            //                 {
            //                     SessionLog.Error($"Unknown plugin type '{pluginType.FullName}'.");
            //                     continue;
            //                 }

            //                 pluginInfo.Assembly = pluginAssembly;
            //                 pluginInfo.Name = pluginData.Name;
            //                 pluginInfo.Description = pluginData.Description;
            //                 pluginInfo.TypeInfo = pluginType;
            //                 pluginInfo.Guid = pluginGuid;

            //                 SessionLog.Info("Plugin '{0}' ({1}) loaded. Assembly: {2}", pluginInfo.Name, pluginInfo.Guid, pluginAssembly.FullName);
            //             }
            //             catch (Exception ex)
            //             {
            //                 SessionLog.Error("Failed to instantiate type '{0}'. See the exception that follows.", pluginType.FullName);
            //                 SessionLog.Exception(ex);
            //             }

            //             var existing = PluginAssemblies.FirstOrDefault(a => a.Assembly == pluginAssembly);

            //             if (existing == null)
            //             {
            //                 var newPA = new PluginAssembly(pluginAssembly, inlineEntryId);
            //                 newPA.AddPlugin(pluginInfo);
            //                 _pluginAssemblies.Add(newPA);
            //             }
            //             else
            //             {
            //                 existing.AddPlugin(pluginInfo);
            //             }
            //         }
            //     }
            //     else
            //     {
            //         SessionLog.Warning("Failed to find any jsDAL Server plugins in the assembly '{0}'. Make sure you have a public class available that derives from one of the plugin types.", pluginAssembly.Location);
            //     }
            // }
            // else
            // {
            //     SessionLog.Warning("Failed to find any jsDAL Server plugins in the assembly '{0}'. Make sure you have a public class available that derives from one of the plugin types.", pluginAssembly.Location);
            // }
        }

        public bool LoadOrUpdateInlineAssembly(string inlineEntryId, Assembly newAssembly, out List<string> errorList)
        {
            errorList = null;

            var existingPluginAssembly = this.PluginAssemblies.FirstOrDefault(p => p.InlineEntryId != null
                                && p.InlineEntryId.Equals(inlineEntryId, StringComparison.Ordinal) && p.IsInline);

            if (!ParsePluginAssembly(newAssembly, out var pluginInfoList, out errorList, checkForConflict: false))
            {
                return false;
            }


            if (existingPluginAssembly != null)
            {
                existingPluginAssembly.UpdatePluginList(pluginInfoList);

                SessionLog.Info($"Assembly {existingPluginAssembly.Assembly.FullName} updated");

                var serverMethodPlugins = pluginInfoList.Where(pi => pi.Type == PluginType.ServerMethod);

                if (serverMethodPlugins.Count() > 0)
                {
                    ServerMethodManager.HandleAssemblyUpdated(existingPluginAssembly.InstanceId, serverMethodPlugins.ToList());
                }

                var bgThreadPlugins = pluginInfoList.Where(pi => pi.Type == PluginType.BackgroundThread);

                if (bgThreadPlugins.Count() > 0)
                {
                    // TODO: Reinit
                }


                // unload/recreate any instances
                // for those plugin types that are BG Threads for example they need to be re-init
                //existingPluginAssembly.Plugins[0].Type
                //existingPluginAssembly.Assembly.
            }




            // TODO: Perhaps this can be optimised to run only on apps that are affected by plugin change
            ServerMethodManager.RebuildCacheForAllApps();


            return true;
        }

        public CommonReturnValue GetInlinePluginModuleSource(string inlineEntryId, out InlineModuleManifestEntry existingInlineEntry, out string source)
        {
            source = null;
            existingInlineEntry = null;

            var pluginAssembly = this.PluginAssemblies.FirstOrDefault(p => p.InlineEntryId != null && p.InlineEntryId.Equals(inlineEntryId, StringComparison.Ordinal) && p.IsInline);

            if (pluginAssembly == null)
            {
                return CommonReturnValue.UserError($"An inline module with the Id '{inlineEntryId}' does not exist");
            }

            try
            {
                existingInlineEntry = InlineModuleManifest.Instance.GetEntryById(pluginAssembly.InlineEntryId);
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



    // public class InlinePluginAssembly
    // {
    //     public string FileId { get; set; }
    // }

}