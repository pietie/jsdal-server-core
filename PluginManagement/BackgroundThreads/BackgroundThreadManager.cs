using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;
using Microsoft.AspNetCore.SignalR;
using System.Collections.ObjectModel;
using jsdal_server_core.Settings.ObjectModel;
using System.Threading.Tasks;

namespace jsdal_server_core.PluginManagement
{
    public class BackgroundThreadPluginManager
    {
        private readonly IHubContext<Hubs.BackgroundPluginHub> _hubContext;
        private readonly List<BackgroundThreadPluginRegistration> _registrations;

        public BackgroundThreadPluginManager(IHubContext<Hubs.BackgroundPluginHub> ctx)
        {
            _hubContext = ctx;
            _registrations = new List<BackgroundThreadPluginRegistration>();
            this.Registrations = _registrations.AsReadOnly();
        }

        public ReadOnlyCollection<BackgroundThreadPluginRegistration> Registrations { get; private set; }

        public static BackgroundThreadPluginManager Instance { get; set; }

        public void Register(PluginInfo pluginInfo)
        {
            try
            {
                var existing = _registrations.FirstOrDefault(r => r.PluginGuid.Equals(pluginInfo.Guid.ToString(), StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    existing = BackgroundThreadPluginRegistration.Create(pluginInfo);
                    _registrations.Add(existing);
                }

                existing.CreateEndpointInstances(_hubContext);

                var list = Hubs.BackgroundPluginHub.BgPluginsList();
                _hubContext.Clients.Group(Hubs.BackgroundPluginHub.ADMIN_GROUP_NAME).SendAsync("updateList", list);
            }
            catch (Exception ex)
            {
                SessionLog.Error($"Failed to instantiate plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. See exception that follows.");
                SessionLog.Exception(ex);
            }
        }

        public BackgroundThreadPluginInstance FindPluginInstance(Endpoint endpoint, Guid pluginGuid)
        {
            if (_registrations == null) return null;

            var reg = _registrations.FirstOrDefault(r => r.PluginGuid.Equals(pluginGuid.ToString(), StringComparison.OrdinalIgnoreCase));

            if (reg == null) return null;

            return reg.FindPluginInstance(endpoint);
        }

        public void StopForApp(Application app, PluginInfo pluginInfo)
        {
            var reg = _registrations.FirstOrDefault(r => r.PluginGuid.Equals(pluginInfo.Guid.ToString(), StringComparison.OrdinalIgnoreCase));

            if (reg == null) return;

            app.Endpoints.ForEach(ep => reg.KillInstance(ep));


            var list = Hubs.BackgroundPluginHub.BgPluginsList();

            _hubContext.Clients.Group(Hubs.BackgroundPluginHub.ADMIN_GROUP_NAME).SendAsync("updateList", list);
        }

        public void Shutdown()
        {
            if (_registrations == null) return;

            Parallel.ForEach(_registrations, reg =>
            {
                reg.Shutdown();
            });
        }
    }
}