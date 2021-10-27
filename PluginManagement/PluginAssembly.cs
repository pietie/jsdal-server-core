using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using jsdal_server_core.PluginManagement;
using jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core
{
    public class PluginAssembly
    {
        public AssemblyLoadContext AssemblyLoadContext { get; private set; }
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

        public PluginAssembly(AssemblyLoadContext asmCtx, Assembly assembly, string inlineEntryId = null)
        {
            this.AssemblyLoadContext = asmCtx;
            this.Assembly = assembly;
            this.InlineEntryId = inlineEntryId;
            this._plugins = new List<PluginInfo>();
            this.Plugins = _plugins.AsReadOnly();
            this.InstanceId = shortid.ShortId.Generate(true, false, 3);
        }

        public void AddPlugin(PluginInfo plugin)
        {
            try
            {
                this._plugins.Add(plugin);

                if (plugin.Type == PluginType.BackgroundThread)
                {
                    BackgroundThreadPluginManager.Instance.Register(plugin);
                }
                else if (plugin.Type == PluginType.ServerMethod)
                {
                    ServerMethodManager.Register(InstanceId, plugin);
                }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        // public void UpdatePluginList(List<PluginInfo> list)
        // {
        //     this._plugins.Clear();
        //     this._plugins.AddRange(list);
        // }

        public void Unload()
        {
            var serverMethodPlugins = _plugins.Where(pi => pi.Type == PluginType.ServerMethod);

            if (serverMethodPlugins.Count() > 0)
            {
                // TODO: remove from SM manager entirely??!?!?! 
                ServerMethodManager.HandleAssemblyUpdated(InstanceId, serverMethodPlugins.ToList());
            }

            var bgThreadPlugins = _plugins.Where(pi => pi.Type == PluginType.BackgroundThread).ToList();

            if (bgThreadPlugins.Count() > 0)
            {
                BackgroundThreadPluginManager.Instance.StopAll(bgThreadPlugins);
            }

            this._plugins.Clear();
            this.AssemblyLoadContext.Unload();
            this.InlineEntryId = null;
            this.Assembly = null;
            this.AssemblyLoadContext = null;
        }
    }
}