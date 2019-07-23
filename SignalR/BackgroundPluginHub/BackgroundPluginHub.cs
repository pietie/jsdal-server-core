using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using jsdal_server_core.Controllers;
using jsdal_server_core.Performance;
using jsdal_server_core.PluginManagement;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs
{
    public class BackgroundPluginHub : Hub
    {
        public static readonly string ADMIN_GROUP_NAME = "BackgroundPluginHub.Admin";
        public static readonly string BROWSER_CONSOLE_GROUP_NAME = "Browser.Console";
        private readonly BackgroundThreadPluginManager _bgThreadManager;
        public BackgroundPluginHub(BackgroundThreadPluginManager btpm)
        {
            this._bgThreadManager = btpm;
        }
        public Task JoinBrowserDebugGroup()
        {
            return this.Groups.AddToGroupAsync(this.Context.ConnectionId, BROWSER_CONSOLE_GROUP_NAME);
        }

        // join a specific plugin's group
        public string JoinGroup(string projectName, string appName, string endpointName, string pluginGuid, string groupName)
        {
            if (!ControllerHelper.GetProjectAndAppAndEndpoint(projectName, appName, endpointName, out var project, out var app, out var endpoint, out var resp))
            {
                return resp.Message;
            }

            string hubGroupName = $"{endpoint.Pedigree}/{pluginGuid.ToLower()}/{groupName}";

            //this.Clients.Group(hubGroupName).SendAsync("update", DateTime.Now);

            this.Groups.AddToGroupAsync(this.Context.ConnectionId, hubGroupName);
            // TODO: Research if closed connections are auto removed from groups

            return null;
        }


        // listens for progress/status updates on all threads
        public dynamic JoinAdminGroup() // TODO: Do we need to secure endpoints like these?
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, ADMIN_GROUP_NAME);

            var ret = _bgThreadManager.Registrations
                    .SelectMany(reg => reg.GetLoadedInstances(), (reg, inst)=> new { Reg = reg, Instance = inst })
                    .Select(a => new
                    {
                        Assymbly = a.Reg.Assembly.FullName, 
                        InstanceId = a.Instance.Id,
                        a.Instance.Plugin.Name,
                        a.Instance.Plugin.IsRunning,
                        a.Instance.Plugin.EndpointPedigree,
                        a.Instance.Plugin.Status,
                        a.Instance.Plugin.Progress,
                        a.Instance.Plugin.Description,
                        PluginGuid = a.Instance.Plugin.Guid
                    });

            return ret;
        }


    }

}