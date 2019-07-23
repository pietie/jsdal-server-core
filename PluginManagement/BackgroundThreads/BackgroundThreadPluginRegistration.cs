using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using jsdal_plugin;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.PluginManagement
{
    public class BackgroundThreadPluginRegistration
    {
        public Assembly Assembly { get; private set; }
        public TypeInfo TypeInfo { get; private set; }
        public string PluginGuid { get; private set; }

        private Dictionary<Endpoint, BackgroundThreadPluginInstance> _endpointInstances;

        private BackgroundThreadPluginRegistration(Assembly assembly, TypeInfo typeInfo, Guid pluginGuid)
        {
            this.Assembly = assembly;
            this.TypeInfo = typeInfo;
            this.PluginGuid = pluginGuid.ToString();
            this._endpointInstances = new Dictionary<Endpoint, BackgroundThreadPluginInstance>();
        }


        public List<BackgroundThreadPluginInstance> GetLoadedInstances()
        {
            return _endpointInstances.Values.ToList();
        }
        public static BackgroundThreadPluginRegistration Create(PluginInfo pluginInfo, IHubClients hubClients)
        {
            var reg = new BackgroundThreadPluginRegistration(pluginInfo.Assembly, pluginInfo.TypeInfo, pluginInfo.Guid);

            // TODO: Instantiate for each configured App/EP? Also need to look at EP Creation Event and EP stopped & deleted event. 

            var apps = Settings.SettingsInstance.Instance.ProjectList.SelectMany(proj => proj.Applications).Where(app => app.IsPluginIncluded(pluginInfo.Guid.ToString()));
            var endpointCollection = apps.SelectMany(app => app.Endpoints);

            foreach (var endpoint in endpointCollection)
            {
                try
                {
                    var pluginInstance = (BackgroundThreadPlugin)pluginInfo.Assembly.CreateInstance(pluginInfo.TypeInfo.FullName);
                    var initMethod = typeof(BackgroundThreadPlugin).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic);

                    if (initMethod != null)
                    {
                        var instanceWrapper = new BackgroundThreadPluginInstance(endpoint, pluginInstance);
                        var openSqlConnectionCallback = new Func<System.Data.SqlClient.SqlConnection>(() => { return null; });

                        var updateDataCallback = new Func<ExpandoObject, bool>(data =>
                        {
                            dynamic eo = data;

                            eo.InstanceId = instanceWrapper.Id;
                            eo.Endpoint = endpoint.Pedigree;

                            hubClients.Group(Hubs.BackgroundPluginHub.ADMIN_GROUP_NAME).SendAsync("updateData", (object)eo);
                            return true;
                        });


                        var browserConsoleSendCallback = new Func<string, string, bool>((method, line) =>
                        {
                            hubClients.Group(Hubs.BackgroundPluginHub.BROWSER_CONSOLE_GROUP_NAME).SendAsync(method, new
                            {
                                InstanceId = instanceWrapper.Id,
                                Endpoint = endpoint.Pedigree,
                                Line = line
                            });
                            return true;
                        });

                        initMethod.Invoke(pluginInstance, new object[] { endpoint.Pedigree, openSqlConnectionCallback, updateDataCallback, browserConsoleSendCallback, null/*configKeys*/, null/*configSource*/ });

                        reg.AddEnpointInstance(endpoint, instanceWrapper);
                    }
                    else
                    {
                        throw new Exception("Expected Init method not found");
                    }
                }
                catch (Exception ex)
                {
                    SessionLog.Error($"Failed to instantiate plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName} on endpoint {endpoint.Pedigree}. See exception that follows.");
                    SessionLog.Exception(ex);
                }
            }

            return reg;
        }

        private void AddEnpointInstance(Endpoint endpoint, BackgroundThreadPluginInstance instanceWrapper)
        {
            this._endpointInstances.Add(endpoint, instanceWrapper);
        }
    }

    public class BackgroundThreadPluginInstance
    {
        public string Id { get; private set; }

        public BackgroundThreadPlugin Plugin { get; private set; }
        public Endpoint Endpoint { get; private set; }
        public BackgroundThreadPluginInstance(Endpoint endpoint, BackgroundThreadPlugin pluginInstance)
        {
            this.Id = shortid.ShortId.Generate(false, false, 5);
            this.Endpoint = endpoint;
            this.Plugin = pluginInstance;
        }
    }

}