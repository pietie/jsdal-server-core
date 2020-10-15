using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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

        public static BackgroundThreadPluginRegistration Create(PluginInfo pluginInfo, IHubContext<Hubs.BackgroundPluginHub> hub)
        {
            IHubClients hubClients = hub.Clients;

            var reg = new BackgroundThreadPluginRegistration(pluginInfo.Assembly, pluginInfo.TypeInfo, pluginInfo.Guid);

            // TODO: Instantiate for each configured App/EP? Also need to look at EP Creation Event and EP stopped & deleted event. 

            var apps = Settings.SettingsInstance.Instance
                                    .ProjectList
                                    .SelectMany(proj => proj.Applications)
                                    .Where(app => app.IsPluginIncluded(pluginInfo.Guid.ToString()));

            var endpointCollection = apps.SelectMany(app => app.Endpoints);

            // create a default instance just to read the Default Value collection
            var defaultInstance = (BackgroundThreadPlugin)pluginInfo.Assembly.CreateInstance(pluginInfo.TypeInfo.FullName);

            var defaultConfig = defaultInstance.GetDefaultConfig();

            if (defaultConfig.ContainsKey("IsEnabled"))
            {
                var defIsEnabled = defaultConfig["IsEnabled"];
                // TODO: Convert to better typed class (e.g. true/false)
                // TODO: Match up with Endpoint config. EP Config needs to be persisted somewhere
            }

            // TODO: For each BG Plugin catalog the 'server methods' available. (do this once per assembly, not per EP as they are the same for all EPs) 

            foreach (var endpoint in endpointCollection)
            {
                try
                {
                    var pluginInstance = (BackgroundThreadPlugin)pluginInfo.Assembly.CreateInstance(pluginInfo.TypeInfo.FullName);
                    var initMethod = typeof(BackgroundThreadPlugin).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic);

                    if (initMethod != null)
                    {
                        var instanceWrapper = new BackgroundThreadPluginInstance(endpoint, pluginInstance);

                        var logExceptionCallback = new Action<Exception, string>((exception, additionalInfo) =>
                        {
                            // TODO: Throttle logging if it happens too frequently. Possibly stop plugin if too many exceptions?
                            ExceptionLogger.LogException(exception, new Controllers.ExecController.ExecOptions()
                            {
                                project = endpoint.Application.Project.Name,
                                application = endpoint.Application.Name,
                                endpoint = endpoint.Name,
                                schema = "BG PLUGIN",
                                routine = pluginInfo.Name,
                                type = Controllers.ExecController.ExecType.BackgroundThread

                            }, additionalInfo, $"BG PLUGIN - {pluginInfo.Name}", endpoint.Pedigree);
                        });


                        var openSqlConnectionCallback = new Func<System.Data.SqlClient.SqlConnection>(() =>
                        {
                            var execConn = endpoint.GetSqlConnection();
                            if (execConn == null) throw new Exception($"Execution connection not configured on endpoint {endpoint.Pedigree}");

                            var sqlCon = new System.Data.SqlClient.SqlConnection(execConn.ConnectionStringDecrypted);
                            sqlCon.Open();
                            return sqlCon;
                        });

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

                        var addToGroupAsync = new Func<string, string, CancellationToken, Task>((connectionId, groupName, cancellationToken) =>
                        {
                            return hub.Groups.AddToGroupAsync(connectionId, $"{endpoint.Pedigree}.{groupName}", cancellationToken);
                        });

                        var sendToGroupsAsync = new Func<string, string, object[], Task>((groupName, methodName, args) =>
                        {
                            groupName = $"{endpoint.Pedigree}.{groupName}";

                            if (args == null || args.Length == 0) return hub.Clients.Groups(groupName).SendAsync(methodName);
                            else if (args.Length == 1) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0]);
                            else if (args.Length == 2) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1]);
                            else if (args.Length == 3) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1], args[2]);
                            else if (args.Length == 4) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1], args[2], args[3]);
                            else if (args.Length == 5) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1], args[2], args[3], args[4]);
                            else if (args.Length == 6) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1], args[2], args[3], args[4], args[5]);
                            else if (args.Length == 7) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1], args[2], args[3], args[4], args[5], args[6]);
                            else if (args.Length == 8) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]);
                            else if (args.Length == 9) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8]);
                            else if (args.Length == 10) return hub.Clients.Groups(groupName).SendAsync(methodName, args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9]);

                            return null;
                        });


                        initMethod.Invoke(pluginInstance,
                            new object[] {
                                endpoint.Pedigree,
                                logExceptionCallback,
                                openSqlConnectionCallback,
                                updateDataCallback,
                                browserConsoleSendCallback,
                                addToGroupAsync,
                                sendToGroupsAsync,
                                null/*configKeys*/,
                                null/*configSource*/ });

                        reg.AddEnpointInstance(endpoint, instanceWrapper);

                        SessionLog.Info($"BG plugin '{pluginInfo.Name}' instantiated successfully on endpoint {endpoint.Pedigree}");
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

        public BackgroundThreadPluginInstance FindPluginInstance(Endpoint endpoint)
        {
            if (_endpointInstances == null || !_endpointInstances.ContainsKey(endpoint)) return null;

            return _endpointInstances[endpoint];
        }

        public void Shutdown()
        {
            if (this._endpointInstances == null) return;

            Parallel.ForEach(this._endpointInstances.Values, (instance) =>
            {
                instance.Plugin.Stop();
            });


            this._endpointInstances.Clear();
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