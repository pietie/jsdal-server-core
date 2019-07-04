using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using jsdal_server_core.Controllers;
using jsdal_server_core.Performance;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs
{
    public class BackgroundPluginHub : Hub
    {
        public BackgroundPluginHub()
        {

        }
        public override Task OnConnectedAsync()
        {
            // var key = Context.ConnectionId; // TODO: Change to something like the logged in userId

            // _connections.Add(key, Context.ConnectionId);

            // Groups.AddToGroupAsync(Context.ConnectionId, "RealtimeHub.Main");

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            // var key = Context.ConnectionId; // TODO: Change to something like the logged in userId
            // _connections.Remove(key, Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        public Task JoinBrowserDebugGroup()
        {
            return this.Groups.AddToGroupAsync(this.Context.ConnectionId, "Browser.Console");
        }

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

    }

}