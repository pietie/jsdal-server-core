using System;
using System.Linq;
using System.Collections.Generic;

using jsdal_server_core.Settings.ObjectModel;
using Newtonsoft.Json;

namespace jsdal_server_core.Settings
{
    public class JsDalServerConfig
    {
        [JsonProperty("Settings")]
        public CommonSettings Settings { get; private set; }
        public List<Project> ProjectList { get; private set; }

        public JsDalServerConfig()
        {
            this.ProjectList = new List<Project>();
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

        private bool exists(string projectName)
        {
            if (this.ProjectList == null) return false;

            var existing = this.ProjectList.FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            return existing != null;
        }

        public Project getProject(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            return this.ProjectList.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public CommonReturnValue AddProject(string name)/*: { success: boolean, userError ?: string }*/
        {
            if (this.ProjectList == null) this.ProjectList = new List<Project>();

            if (string.IsNullOrWhiteSpace(name))
            {
                return CommonReturnValue.userError("Please provide a valid project name.");
            }

            if (this.exists(name))
            {
                return CommonReturnValue.userError($"A project with the name \"{name}\" already exists.");
            }

            var proj = new Project();
            proj.Name = name;
            this.ProjectList.Add(proj);

            return CommonReturnValue.success();
        }


        public CommonReturnValue UpdateProject(string currentName, string newName)/*: { success: boolean, userError ?: string }*/
        {

            if (this.ProjectList == null) this.ProjectList = new List<Project>();

            if (newName == null || string.IsNullOrWhiteSpace(newName))
            {
                return CommonReturnValue.userError("Please provide a valid project name.");
            }

            if (this.exists(newName))
            {
                return CommonReturnValue.userError($"A project with the name \"{newName}\" already exists.");
            }
            if (!this.exists(currentName))
            {
                return CommonReturnValue.userError($"The project \"{newName}\" does not exist so the update operation cannot continue");
            }

            var existing = this.ProjectList.FirstOrDefault(p => p.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));

            existing.Name = newName;

            return CommonReturnValue.success();
        }

        public CommonReturnValue DeleteProject(string name)
        {
            if (this.ProjectList == null) this.ProjectList = new List<Project>();

            if (!this.exists(name))
            {
                return CommonReturnValue.userError($"The project \"{name}\" does not exist.");
            }

            var existing = this.ProjectList.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            this.ProjectList.Remove(existing);

            return CommonReturnValue.success();
        }

    }

}