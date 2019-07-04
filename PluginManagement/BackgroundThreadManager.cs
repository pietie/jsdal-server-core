using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core
{
    public class BackgroundThreadManager
    {
        private readonly IHubContext<Hubs.BackgroundPluginHub> _hubContext;

        public BackgroundThreadManager(IHubContext<Hubs.BackgroundPluginHub> ctx)
        {
            _hubContext = ctx;
        }

        public BackgroundThreadManager()
        {

        }


        public void Register(PluginInfo pluginInfo)
        {
            try
            {
                // TODO: Instantiate for each configured App/EP? Also need to look at EP Creation Event and EP stopped & deleted event. 

                var apps = Settings.SettingsInstance.Instance.ProjectList.SelectMany(proj => proj.Applications).Where(app => app.IsPluginIncluded(pluginInfo.Guid.ToString()));
                var endpointCollection = apps.SelectMany(app => app.Endpoints);

                foreach (var endpoint in endpointCollection)
                {
                    try
                    {
                        // TODO: Keep a reference to the instance around
                        var pluginInstance = (BackgroundThreadPlugin)pluginInfo.Assembly.CreateInstance(pluginInfo.TypeInfo.FullName);
                        var initMethod = typeof(BackgroundThreadPlugin).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic);

                        if (initMethod != null)
                        {
                           // Init(string endpointPedigree, Func<SqlConnection> openSqlConnectionFunc, dynamic hub)
                            initMethod.Invoke(pluginInstance, new object[] { endpoint.Pedigree, null/*TODO*/, _hubContext.Clients });
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                    //pluginInfo.Assembly.CreateInstance(pluginInfo.Type)
                }



            }
            catch (Exception ex)
            {
                SessionLog.Error($"Failed to instantiate plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. See exception that follows.");
                SessionLog.Exception(ex);
            }
        }
    }
}