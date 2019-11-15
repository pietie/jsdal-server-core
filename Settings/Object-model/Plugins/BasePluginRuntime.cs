using System;
using System.Collections.Generic;
using System.IO;
using jsdal_server_core.Settings.ObjectModel.Plugins.InlinePlugins;
using Newtonsoft.Json;
using shortid;

namespace jsdal_server_core.Settings.ObjectModel.Plugins
{

    public class BasePluginRuntime
    {
        public string Name;
        public string Description;

        public string PluginGuid;

        public PluginType Type;

       
        // protected void Init(string name, string description, string pluginGuid)
        // {
        //     this.Name = name;
        //     this.Description = description;
        //     this.PluginGuid = pluginGuid.ToLower();
        // }

        public virtual void Update(string updatedCode, BasePluginRuntime plugin)
        {
            this.Name = plugin.Name;
            this.Description = plugin.Description;
            this.PluginGuid = plugin.PluginGuid.ToLower();
        }
    }

    public class ExecPluginRuntime : BasePluginRuntime // called during routine exectuion (aka "normal/original" jsDAL plugins)
    {
        public ExecPluginRuntime()
        {
            this.Type = PluginType.Execution;
        }
    }


    public class ServerMethodPluginRuntime : BasePluginRuntime  // C# backed method that can be called from frontend
    {
        public ServerMethodPluginRuntime()
        {
            this.Type = PluginType.ServerMethod;
        }


        // public static InlinePluginModule CreateInlineModule(string code, List<BasePluginRuntime> parsedPluginCollection)
        // {
        //     var module = new InlinePluginModule(code, true/*isValid*/);

        //     foreach (var plugin in parsedPluginCollection)
        //     {
        //         var sm = new ServerMethodPluginRuntime();

        //         if (plugin.Name != null) plugin.Name = plugin.Name.Trim();

        //         sm.Create(plugin.Name, plugin.Description, plugin.Guid.ToString().ToLower());

        //         module.AddPlugin(sm);
        //     }

        //     return module;
        // }

    }


    // public class DbNotifyMethodPlugin : BasePlugin // called/pushed from a SQL DB
    // {
    //     public DbNotifyMethodPlugin()
    //     {
    //         this.Type = PluginType.DbNotifyMethod;
    //     }
    // }

}