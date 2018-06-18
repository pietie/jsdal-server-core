using System;
using System.IO;
using Newtonsoft.Json;
using shortid;

namespace jsdal_server_core.Settings.ObjectModel
{

    public class BasePlugin
    {
        public string Id;

        public string Name;
        public string Description;
        public string Path;

        public string PluginGuid;

        public bool IsValid; // true if it compiles successfully
        public PluginType Type;
    }

    public class ExecPlugin : BasePlugin // called during routine exectuion (aka "normal/original" jsDAL plugins)
    {
        public ExecPlugin()
        {
            this.Type = PluginType.ExecPlugin;
        }
    }


    public class ServerMethodPlugin : BasePlugin  // C# backed method that can be called from frontend
    {
        public ServerMethodPlugin()
        {
            this.Type = PluginType.ServerMethod;
        }

        [JsonIgnore]
        public static string InlinePluginPath //TODO: move property to Settings level?
        {
            get
            {
                return "./inline-plugins";
            }
        }

        public static ServerMethodPlugin Create(string code, string name, string pluginGuid, string description, bool isValid)
        {
            var sm = new ServerMethodPlugin();

            if (name != null) name = name.Trim();

            sm.Id = ShortId.Generate(useNumbers: true, useSpecial: false, length: 6);
            sm.Name = name;
            sm.Description = description;
            sm.PluginGuid = pluginGuid.ToLower();
            sm.IsValid = isValid;

            if (!Directory.Exists(InlinePluginPath))
            {
                Directory.CreateDirectory(InlinePluginPath);
            }

            sm.Path = System.IO.Path.Combine(InlinePluginPath, sm.Id);

            File.WriteAllText(sm.Path, code);

            return sm;
        }
    }


    public class DbNotifyMethodPlugin : BasePlugin // called/pushed from a SQL DB
    {
        public DbNotifyMethodPlugin()
        {
            this.Type = PluginType.DbNotifyMethod;
        }
    }

}