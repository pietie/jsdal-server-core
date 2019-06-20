using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using shortid;

namespace jsdal_server_core.Settings.ObjectModel.Plugins.InlinePlugins
{
    public class InlinePluginModule // TODO: Can we create common base between Inline Plugins and Assembly/FromFile loaded ones?
    {
        [JsonIgnore]
        public static string InlinePluginPath //TODO: move property to Settings level?
        {
            get
            {
                return "./inline-plugins";
            }
        }

        public string Id;
        public string Path;
        public bool IsValid; // true if it compiles successfully

        public List<BasePluginRuntime> PluginList { get; set; }

        public InlinePluginModule(string code, bool isValid)
        {
            this.PluginList = new List<BasePluginRuntime>();
            this.IsValid = isValid;
            this.Create(code);
        }

        public void AddPlugin(BasePluginRuntime plugin)
        {
            this.PluginList.Add(plugin);
        }

        public void AddPluginRange(IEnumerable<BasePluginRuntime> pluginList)
        {
            this.PluginList.AddRange( pluginList);
        }

        private void Create(string code)
        {
            this.Id = ShortId.Generate(useNumbers: true, useSpecial: false, length: 6);

            if (!Directory.Exists(InlinePluginPath))
            {
                Directory.CreateDirectory(InlinePluginPath);
            }

            this.Path = System.IO.Path.Combine(InlinePluginPath, this.Id);
            File.WriteAllText(this.Path, code);
        }

        public virtual void Update(string updatedCode, List<BasePluginRuntime> pluginList, bool isValid)
        {
            this.IsValid = isValid;
            
            // TODO: unload plugins
            this.PluginList.Clear(); 
            this.PluginList = pluginList;

            if (!Directory.Exists(InlinePluginPath))
            {
                Directory.CreateDirectory(InlinePluginPath);
            }

            File.WriteAllText(this.Path, updatedCode);
        }

    }


}