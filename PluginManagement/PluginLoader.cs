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
using System.Runtime.Loader;

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

        private int _asmCtxCounter = 0;
        private readonly List<PluginAssembly> _pluginAssemblies;
        public ReadOnlyCollection<PluginAssembly> PluginAssemblies { get; private set; }


        // if I don't keep a ref here the ctx gets collected prematurely...even though PluginAssembly has a ref to it (oO)
        private static List<AssemblyLoadContext> ASM_CTXES = new List<AssemblyLoadContext>();
        public async Task InitAsync()
        {
            // load from plugin directory
            try
            {
                // AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
                // {
                //     try
                //     {
                //         var asmName = new AssemblyName(e.Name);
                //         var requestingLocation = e.RequestingAssembly.Location;
                //         var requestingDir = Path.GetDirectoryName(requestingLocation);

                //         // look for a dll in the same location as the requesting assembly
                //         var path = Path.Combine(requestingDir, asmName.Name + ".dll");

                //         if (!File.Exists(path)) return null;

                //         Assembly.LoadFrom(path);

                //         return null;
                //     }
                //     catch
                //     {
                //         return null;
                //     }
                // };

                var pluginPath = Path.GetFullPath("plugins");

                if (Directory.Exists("./plugins"))
                {
                    var dllCollection = Directory.EnumerateFiles("plugins", "*.dll", SearchOption.TopDirectoryOnly);

                    foreach (var dllPath in dllCollection)
                    {
                        // skip jsdal-plugin base
                        if (dllPath.Equals("plugins\\jsdal-plugin.dll", StringComparison.OrdinalIgnoreCase)) continue;

                        try
                        {
                            var ctxName = $"Plugin Context {++_asmCtxCounter}";
                            var asmCtx = new PluginAssemblyLoadContext(pluginPath, ctxName, true/*enable unloading*/);
                            ASM_CTXES.Add(asmCtx);
                            SessionLog.Info($"Created {ctxName} for {dllPath}".PadRight(35));


                            var dllFullPath = Path.GetFullPath(dllPath);
                            var pluginAssembly = asmCtx.LoadFromAssemblyPath(dllFullPath);

                            ParseAndLoadPluginAssembly(asmCtx, pluginAssembly, null);
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

                        CompileCodeIntoAssemblyContext(inlineEntry, code);
                    }
                    else
                    {
                        SessionLog.Error($"Inline module {inlineEntry.Name} not found at '{sourcePath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }

            // init server-wide types
            // try
            // {
            //     InitServerWidePlugins();
            // }
            // catch (Exception ex)
            // {
            //     SessionLog.Exception(ex);
            // }
        }

        public static void RemoveAssemblyContextRef(PluginAssemblyLoadContext ctx)
        {
            ASM_CTXES.Remove(ctx);
        } 

        private void CompileCodeIntoAssemblyContext(InlineModuleManifestEntry inlineEntry, string code)
        {
            var ctxName = $"Inline Plugin Context {++_asmCtxCounter}";
            var pluginPath = Path.GetFullPath("plugins");
            var asmCtx = new PluginAssemblyLoadContext(pluginPath, ctxName, true/*enable unloading*/);

            ASM_CTXES.Add(asmCtx);

            SessionLog.Info($"Created {ctxName} for {inlineEntry.Name}".PadRight(35));

            var assemblyBytes = CSharpCompilerHelper.CompileIntoAssembly(inlineEntry.Name, code, out var problems);

            if ((problems != null && problems.Count == 0))
            {
                Assembly assembly = null;

                try
                {
                    using (var ms = new MemoryStream(assemblyBytes))
                    {
                        assembly = asmCtx.LoadFromStream(ms);

                        ParseAndLoadPluginAssembly(asmCtx, assembly, inlineEntry.Id);
                    }
                }
                catch (Exception ee)
                {
                    SessionLog.Error($"Failed to load inline plugin assembly '{assembly?.FullName}' {inlineEntry.Name}/{inlineEntry.Id}. See exception that follows.");
                    SessionLog.Exception(ee);
                }
            }
            else
            {
                SessionLog.Error($"Inline plugin {inlineEntry.Name} ({inlineEntry.Id}) failed to compile with the following error(s): {string.Join(", ", problems)}");
            }
        }



        //AssemblyLoadContext, AssemblyName, Assembly?
        // private Assembly OnResolveAssembly(AssemblyLoadContext ctx, AssemblyName asmName)
        // {
        //     try
        //     {
        //         // try and find relative to existing assebmlies loaded in ctx
        //         foreach (var asm in ctx.Assemblies)
        //         {
        //             var fi = new FileInfo(asm.Location);

        //             var path = Path.Combine(fi.DirectoryName, asmName.Name + ".dll");

        //             if (File.Exists(path))
        //             {
        //                 try
        //                 {
        //                     var loadedAsm = ctx.LoadFromAssemblyPath(path);
        //                     if (loadedAsm != null) return loadedAsm;
        //                 }
        //                 catch { }
        //             }
        //         }

        //         SessionLog.Error($"Failed to resolve {asmName.FullName} in context {ctx.Name}");
        //         return null;
        //     }
        //     catch
        //     {
        //         return null;
        //     }
        // }

        // private Assembly OnResolveAssembly(object sender, ResolveEventArgs e)
        // {
        //     try
        //     {
        //         var asmName = new AssemblyName(e.Name);
        //         var requestingLocation = e.RequestingAssembly.Location;
        //         var requestingDir = Path.GetDirectoryName(requestingLocation);

        //         // look for a dll in the same location as the requesting assembly
        //         var path = Path.Combine(requestingDir, asmName.Name + ".dll");

        //         if (!File.Exists(path)) return null;

        //         Assembly.LoadFrom(path);

        //         return null;
        //     }
        //     catch
        //     {
        //         return null;
        //     }
        // }

        // private void InitServerWidePlugins()
        // {
        //     try
        //     {
        //         if (PluginAssemblies == null) return;

        //         foreach (var pluginAssembly in PluginAssemblies)
        //         {
        //             var pluginInfoCollection = pluginAssembly.Plugins.Where(p => p.Type == OM.PluginType.ServerMethod || p.Type == OM.PluginType.BackgroundThread);

        //             foreach (var pluginInfo in pluginInfoCollection)
        //             {
        //                 if (pluginInfo.Type == OM.PluginType.BackgroundThread)
        //                 {
        //                     _backgroundThreadManager.Register(pluginInfo);
        //                 }
        //                 else if (pluginInfo.Type == OM.PluginType.ServerMethod)
        //                 {
        //                     ServerMethodManager.Register(pluginAssembly.InstanceId, pluginInfo);
        //                 }
        //             }
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         SessionLog.Exception(ex);
        //     }
        // }


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

        private void ParseAndLoadPluginAssembly(AssemblyLoadContext asmCtx, Assembly assembly, string inlineEntryId = null)
        {
            if (ParsePluginAssembly(assembly, out var pluginInfoList, out var errorList, checkForConflict: true))
            {
                foreach (var pluginInfo in pluginInfoList)
                {
                    SessionLog.Info($"{(inlineEntryId != null ? "(Inline) " : "")}Plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) found in assembly: {assembly.FullName}");

                    var existing = PluginAssemblies.FirstOrDefault(a => a.Assembly == assembly);

                    if (existing == null)
                    {
                        var newPA = new PluginAssembly(asmCtx, assembly, inlineEntryId);
                        newPA.AddPlugin(pluginInfo);
                        _pluginAssemblies.Add(newPA);
                    }
                    else
                    {
                        existing.AddPlugin(pluginInfo);
                    }
                }
            }
        }

        public bool LoadOrUpdateInlineAssembly(InlineModuleManifestEntry inlineEntry, string code, out List<string> errorList)
        {
            errorList = null;

            var existingPluginAssembly = this.PluginAssemblies.FirstOrDefault(p => p.InlineEntryId != null
                                && p.InlineEntryId.Equals(inlineEntry.Id, StringComparison.Ordinal) && p.IsInline);

            if (existingPluginAssembly != null)
            {
                var existingAsmCtx = AssemblyLoadContext.GetLoadContext(existingPluginAssembly.Assembly);
                // unload existing PluginAssembly
                existingPluginAssembly.Unload();

                this._pluginAssemblies.Remove(existingPluginAssembly);

                Instance.CompileCodeIntoAssemblyContext(inlineEntry, code);

                // if (!ParsePluginAssembly(newAssembly, out var pluginInfoList, out errorList, checkForConflict: false))
                // {
                //     return false;
                // }

                //existingPluginAssembly.UpdatePluginList(pluginInfoList);
                //SessionLog.Info($"Assembly {existingPluginAssembly.Assembly.FullName} updated");


            }
            else
            {
                // new?
                Instance.CompileCodeIntoAssemblyContext(inlineEntry, code);
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