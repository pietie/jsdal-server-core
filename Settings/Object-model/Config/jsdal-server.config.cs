using System;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Settings.ObjectModel;
using jsdal_server_core.Settings.ObjectModel.Plugins.InlinePlugins;
using Newtonsoft.Json;


namespace jsdal_server_core.Settings
{
    public class JsDalServerConfig
    {
        [JsonProperty("Settings")]
        public CommonSettings Settings { get; private set; }
        public List<Project> ProjectList { get; private set; }

        [JsonProperty("InlinePluginModules")] public List<InlinePluginModule> InlinePluginModules { get; set; }

        public JsDalServerConfig()
        {
            this.ProjectList = new List<Project>();
            this.InlinePluginModules = new List<InlinePluginModule>();
        }

        /*public static createFromJson(rawJson: any): JsDalServerConfig {
        if (!rawJson) return null;

        let config = new JsDalServerConfig();

        config.Settings = Settings.createFromJson(rawJson.Settings);

        if (typeof (rawJson.ProjectList) !== "undefined") {
            for (var e in rawJson.ProjectList) {
                config.ProjectList.push(Project.createFromJson(rawJson.ProjectList[e]));
            }
}

        return config;

    }*/

        private bool Exists(string projectName)
        {
            if (this.ProjectList == null) return false;

            var existing = this.ProjectList.FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            return existing != null;
        }

        public Project GetProject(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            return this.ProjectList.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public CommonReturnValue AddProject(string name)/*: { success: boolean, userError ?: string }*/
        {
            if (this.ProjectList == null) this.ProjectList = new List<Project>();

            // TODO: Add more validation rules - mostly around having valid URL-friendly chars only - or make sure Project/App/Endpoint names are valid to be used as sub dirs

            if (string.IsNullOrWhiteSpace(name))
            {
                return CommonReturnValue.UserError("Please provide a valid project name.");
            }

            if (this.Exists(name))
            {
                return CommonReturnValue.UserError($"A project with the name \"{name}\" already exists.");
            }

            var proj = new Project();
            proj.Name = name;
            this.ProjectList.Add(proj);

            return CommonReturnValue.Success();
        }


        public CommonReturnValue UpdateProject(string currentName, string newName)/*: { success: boolean, userError ?: string }*/
        {

            if (this.ProjectList == null) this.ProjectList = new List<Project>();

            if (newName == null || string.IsNullOrWhiteSpace(newName))
            {
                return CommonReturnValue.UserError("Please provide a valid project name.");
            }

            if (this.Exists(newName))
            {
                return CommonReturnValue.UserError($"A project with the name \"{newName}\" already exists.");
            }
            if (!this.Exists(currentName))
            {
                return CommonReturnValue.UserError($"The project \"{newName}\" does not exist so the update operation cannot continue");
            }

            var existing = this.ProjectList.FirstOrDefault(p => p.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));

            existing.Name = newName;

            return CommonReturnValue.Success();
        }

        public CommonReturnValue DeleteProject(string name)
        {
            if (this.ProjectList == null) this.ProjectList = new List<Project>();

            if (!this.Exists(name))
            {
                return CommonReturnValue.UserError($"The project \"{name}\" does not exist.");
            }

            var existing = this.ProjectList.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            this.ProjectList.Remove(existing);

            return CommonReturnValue.Success();
        }

        public CommonReturnValue AddInlinePluginModule(InlinePluginModule pluginModule)
        {
            if (this.InlinePluginModules == null) this.InlinePluginModules = new List<InlinePluginModule>();

            // TODO: Also check regular plugins for conflicting Guid

            foreach (var newPluginGuid in pluginModule.PluginList.Select(n => n.PluginGuid))
            {
                var existing = this.InlinePluginModules.SelectMany(pl => pl.PluginList).ToList().FirstOrDefault(p => p.PluginGuid.Equals(newPluginGuid, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    return CommonReturnValue.UserError($"A plugin with the Guid '{existing.PluginGuid}' already exists. Conflict is with '{existing.Name}'");
                }
            }

            this.InlinePluginModules.Add(pluginModule);
            return CommonReturnValue.Success();
        }

        public CommonReturnValue UpdateInlinePluginModule(string id, string code, List<ObjectModel.Plugins.BasePluginRuntime> pluginList, bool isValid)
        {
            if (this.InlinePluginModules == null) this.InlinePluginModules = new List<InlinePluginModule>();

            var existing = this.InlinePluginModules.FirstOrDefault(p => p.Id.Equals(id, StringComparison.Ordinal));

            if (existing == null)
            {
                return CommonReturnValue.UserError($"Update failed. Failed to find the specified plugin module with id {id}");
            }

            existing.Update(code, pluginList, isValid);

            return CommonReturnValue.Success();
        }

        public CommonReturnValue GetInlinePluginModule(string id, out string source)
        {
            source = null;
            if (this.InlinePluginModules == null) this.InlinePluginModules = new List<InlinePluginModule>();

            var existing = this.InlinePluginModules.FirstOrDefault(p => p.Id.Equals(id, StringComparison.Ordinal));

            if (existing == null)
            {
                return CommonReturnValue.UserError($"A plugin with the Id '{id}' does not exist");
            }

            try
            {
                if (System.IO.File.Exists(existing.Path))
                {
                    source = System.IO.File.ReadAllText(existing.Path);
                    return CommonReturnValue.Success();
                }
                else
                {
                    return CommonReturnValue.UserError($"Failed to find source at: {existing.Path}");
                }
            }
            catch (Exception e)
            {
                SessionLog.Warning("Failed to fetch file of plugin module: {0}", existing.Id);
                SessionLog.Exception(e);
            }

            return CommonReturnValue.Success();
        }


        public CommonReturnValue DeleteInlinePluginModule(string id)
        {
            if (this.InlinePluginModules == null) this.InlinePluginModules = new List<InlinePluginModule>();

            var existing = this.InlinePluginModules.FirstOrDefault(p => p.Id.Equals(id, StringComparison.Ordinal));

            if (existing == null)
            {
                return CommonReturnValue.UserError($"A plugin module with the Id '{id}' does not exist");
            }

            this.InlinePluginModules.Remove(existing);

            try
            {
                if (System.IO.File.Exists(existing.Path))
                {
                    System.IO.File.Delete(existing.Path);
                }
            }
            catch (Exception e)
            {
                SessionLog.Warning("Failed to delete file of plugin module: {0}", existing.Id);
                SessionLog.Exception(e);
            }

            return CommonReturnValue.Success();
        }

    }

}