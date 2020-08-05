using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;
using Microsoft.AspNetCore.SignalR;
using System.Collections.ObjectModel;
using jsdal_server_core.Settings.ObjectModel;

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

        public void Register(PluginInfo pluginInfo)
        {
            try
            {
                _registrations.Add(BackgroundThreadPluginRegistration.Create(pluginInfo, _hubContext));
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
    }
}