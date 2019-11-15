using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using shortid;
using System.Text.Json.Serialization;

namespace jsdal_server_core.Settings.ObjectModel.Plugins.InlinePlugins
{
    public class InlinePluginModule
    {
        [JsonIgnore]
        public static string InlinePluginPath
        {
            get
            {
                return "./inline-plugins";
            }
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CodePath;
        public bool IsValid; // true if it compiles successfully

        [JsonIgnore]
        public List<BasePluginRuntime> PluginList { get; set; }

        public InlinePluginModule()
        {
            this.PluginList = new List<BasePluginRuntime>();
        }
        public void AddPlugin(BasePluginRuntime plugin)
        {
            this.PluginList.Add(plugin);
        }

        public static bool Create(string id, string name, string description, string code, bool isValid, IEnumerable<BasePluginRuntime> pluginList, bool saveToDisk, out InlinePluginModule newModule, out string error)
        {
            newModule = null;
            error = null;

            newModule = new InlinePluginModule(); ;

            if (!string.IsNullOrWhiteSpace(id))
            {
                newModule.Id = id;
            }
            else
            {
                newModule.Id = ShortId.Generate(useNumbers: true, useSpecial: false, length: 6);
            }

            newModule.CodePath = System.IO.Path.Combine(InlinePluginPath, newModule.Id);

            if (!newModule.Update(name, description, code, isValid, pluginList, saveToDisk: true, out error))
            {
                newModule = null;
                return false;
            }

            return true;
        }

        public virtual bool Update(string name, string description, string updatedCode, bool isValid, IEnumerable<BasePluginRuntime> pluginList, bool saveToDisk, out string error)
        {
            error = null;
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            // List<string> invalidCharsReadable = new List<string>();

            // foreach (var ch in invalidChars)
            // {
            //     if (System.Enum.TryParse(typeof(ConsoleKey), Convert.ToInt32(ch).ToString(), false, out var consoleKey))
            //     {
            //         if (((ConsoleKey)consoleKey).ToString() == Convert.ToInt32(ch).ToString())
            //         {
            //             invalidCharsReadable.Add(ch.ToString());
            //         }
            //         else
            //         {
            //             invalidCharsReadable.Add(consoleKey.ToString());
            //         }
            //     }
            //     else
            //     {
            //         // ???
            //     }
            // }

            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(invalidChars) >= 0)
            {
                error = $"Invalid assembly name specified. Please only use alphanumeric characters.";
                return false;
            }

            // TODO: check for conflicts on name?

            this.IsValid = isValid;

            // TODO: unload plugins
            this.PluginList.Clear();

            if (pluginList != null)
            {
                this.PluginList = new List<BasePluginRuntime>(pluginList);
            }

            this.Name = name.Trim();
            this.Description = description.Trim();

            if (saveToDisk)
            {
                if (!Directory.Exists(InlinePluginPath))
                {
                    Directory.CreateDirectory(InlinePluginPath);
                }

                File.WriteAllText(this.CodePath, updatedCode);
            }

            return true;
        }

    }


}