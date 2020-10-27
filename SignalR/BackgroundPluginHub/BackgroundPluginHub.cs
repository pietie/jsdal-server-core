using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using jsdal_server_core.Controllers;
using jsdal_server_core.Performance;
using jsdal_server_core.PluginManagement;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace jsdal_server_core.Hubs
{
    public class BackgroundPluginHub : Hub
    {
        public static readonly string ADMIN_GROUP_NAME = "BackgroundPluginHub.Admin";
        public static readonly string BROWSER_CONSOLE_GROUP_NAME = "Browser.Console";

        public BackgroundPluginHub()
        {
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

            // tmp for debugging
            //  {
            //     Log.Information($"JoinAdminGroup - Called. Reg count = {BackgroundThreadPluginManager.Instance.Registrations.Count()}");

            //     var asm = string.Join('|', BackgroundThreadPluginManager.Instance.Registrations.Select(r=>r.Assembly.FullName).ToArray());
            //     Log.Information($"JoinAdminGroup - Loaded assemblies: {asm}");
            // }

            var ret = BgPluginsList();

            return ret;
        }

        public static object BgPluginsList()
        {
            var ret = BackgroundThreadPluginManager.Instance.Registrations
                                  .SelectMany(reg => reg.GetLoadedInstances(), (reg, inst) => new { Reg = reg, Instance = inst })
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

        public object InvokePluginMethod(Guid pluginGuid, string endpoint, string methodName, Dictionary<string, string> inputParameters)
        {
            try
            {
                var ep = Settings.SettingsInstance.Instance.FindEndpoint(endpoint);

                var pluginInstance = BackgroundThreadPluginManager.Instance.FindPluginInstance(ep, pluginGuid);

                if (pluginInstance == null) throw new Exception($"Plugin {pluginGuid} not found on endpoint {ep.Pedigree}");

                var mi = PluginHelper.FindBestMethodMatch(pluginInstance.Plugin, methodName, inputParameters);

                (var result, var outputParams, var error) = PluginHelper.InvokeMethod(pluginInstance.Plugin, methodName, mi, inputParameters);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return new { Error = error };
                }
                else
                {
                    return new { Result = result, OutputParams = outputParams };
                }
            }
            catch (Exception ex)
            {
                var exRef = ExceptionLogger.LogException(ex);

                return new { Error = $"Application error occurred. Ref: {exRef}" };
            }
        }


    }

}