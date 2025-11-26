using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using jsdal_plugin;

namespace jsdal_server_core
{
    public class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver _resolver;


        public PluginAssemblyLoadContext(string mainAssemblyToLoadPath, string name, bool isCollectible = false) : base(name, isCollectible)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyToLoadPath);

            this.Resolving += ResolveReference;
            this.Unloading += OnUnloading;
        }

        ~PluginAssemblyLoadContext()
        {
            this.Resolving -= ResolveReference;
            this.Unloading -= OnUnloading;
        }

        private Assembly ResolveReference(AssemblyLoadContext ctx, AssemblyName asmName)
        {
            try
            {
                // try and find relative to existing assebmlies loaded in ctx
                foreach (var asm in ctx.Assemblies)
                {
                    var fi = new FileInfo(asm.Location);

                    var path = Path.Combine(fi.DirectoryName, asmName.Name + ".dll");

                    if (File.Exists(path))
                    {
                        try
                        {
                            var loadedAsm = ctx.LoadFromAssemblyPath(path);
                            if (loadedAsm != null) return loadedAsm;
                        }
                        catch { }
                    }
                }

                SessionLog.Error($"Failed to resolve {asmName.FullName} in context {ctx.Name}");
                return null;
            }
            catch (Exception ex)
            {
                SessionLog.Error($"(Exception) Failed to resolve {asmName.FullName} in context {ctx.Name}. {ex.ToString()}");
                return null;
            }
        }

        private void OnUnloading(AssemblyLoadContext ctx)
        {
            SessionLog.Info($"Unloading assembly context {ctx.Name}");
            PluginLoader.RemoveAssemblyContextRef(this);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // 1. Share jsdal-plugin.dll with default ALC
            var sharedName = typeof(PluginBase).Assembly.GetName().Name;
            if (assemblyName.Name == sharedName)
            {
                // This is the same instance the host uses
                return typeof(PluginBase).Assembly;
            }

            string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }



        // protected PluginAssemblyLoadContext() 
        // {

        // }

        // protected PluginAssemblyLoadContext(bool isCollectible) : base(isCollectible)
        // {
        // }
    }
}