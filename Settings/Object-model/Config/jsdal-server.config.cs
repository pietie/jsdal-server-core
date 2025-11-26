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

        private bool Exists(string projectName)
        {
            if (this.ProjectList == null) return false;

            var existing = this.ProjectList.FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            return existing != null;
        }
        public static JsDalServerConfig CreateDefault()
        {
            var ret = new JsDalServerConfig();

            ret.Settings = new CommonSettings();
            ret.Settings.WebServer = new WebServerSettings()
            {
                EnableSSL = false,
                EnableBasicHttp = true,
                HttpServerHostname = "localhost",
                HttpServerPort = 9086
            };

            return ret;
        }

        public Endpoint FindEndpoint(string endpointPedigree)
        {
            if (string.IsNullOrWhiteSpace(endpointPedigree)) return null;

            var parts = endpointPedigree.Split('/');

            if (parts.Length != 3) return null;

            var project = this.ProjectList.FirstOrDefault(proj => proj.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));

            if (project == null) return null;

            var app = project.Applications.FirstOrDefault(app => app.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));

            if (app == null) return null;

            return app.Endpoints.FirstOrDefault(ep => ep.Name.Equals(parts[2], StringComparison.OrdinalIgnoreCase));

        }

        public Endpoint FindEndpointById(string endpointId)
        {
            if (string.IsNullOrWhiteSpace(endpointId)) return null;
            return this.ProjectList.SelectMany(p => p.Applications).SelectMany(app => app.Endpoints).FirstOrDefault(ep => ep.Id.Equals(endpointId, StringComparison.OrdinalIgnoreCase));
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

    }

}