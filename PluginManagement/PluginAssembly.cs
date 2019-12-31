using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace jsdal_server_core
{
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

        public void UpdatePluginList(List<PluginInfo> list)
        {
            this._plugins.Clear();
            this._plugins.AddRange(list);
        }
    }
}