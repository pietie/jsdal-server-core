using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core.PluginManagement
{
    public static class ServerMethodManager
    {
        //!private static List<ServerMethodPluginRegistration> Registrations { get; set; }

        private static Dictionary<string/*Assembly Instance Reg*/, List<ServerMethodPluginRegistration>> GlobalRegistrations = new Dictionary<string, List<ServerMethodPluginRegistration>>();

        public static string TEMPLATE_ServerMethodContainer { get; private set; }
        public static string TEMPLATE_ServerMethodFunctionTemplate { get; private set; }
        public static string TEMPLATE_ServerMethodTypescriptDefinitionsContainer { get; private set; }

        static ServerMethodManager()
        {
            try
            {

                //!  Registrations = new List<ServerMethodPluginRegistration>();

                ServerMethodManager.TEMPLATE_ServerMethodContainer = File.ReadAllText("./resources/ServerMethodContainer.txt");
                ServerMethodManager.TEMPLATE_ServerMethodFunctionTemplate = File.ReadAllText("./resources/ServerMethodTemplate.txt");
                ServerMethodManager.TEMPLATE_ServerMethodTypescriptDefinitionsContainer = File.ReadAllText("./resources/ServerMethodsTSDContainer.d.ts");
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        private static Dictionary<string/*Assembly InstanceId*/, HashSet<Endpoint>> EndpointInstanceUse = new Dictionary<string, HashSet<Endpoint>>();
        public static void RegisterInstanceUse(Endpoint endpoint, ServerMethodRegistrationMethod method)
        {
            lock (EndpointInstanceUse)
            {
                if (EndpointInstanceUse.ContainsKey(method.Registration.PluginAssemblyInstanceId))
                {
                    if (EndpointInstanceUse[method.Registration.PluginAssemblyInstanceId].Contains(endpoint))
                    {
                        EndpointInstanceUse[method.Registration.PluginAssemblyInstanceId].Add(endpoint);
                    }
                }
                else
                {
                    EndpointInstanceUse.Add(method.Registration.PluginAssemblyInstanceId, new HashSet<Endpoint>() { endpoint });
                }

                //!method.Registration.Assembly.FullName
            }
        }

        public static List<ServerMethodPluginRegistration> GetRegistrationsForApp(Application app)
        {
            return GlobalRegistrations.SelectMany(kv => kv.Value).Where(v => app.IsPluginIncluded(v.PluginGuid)).ToList();
        }

        public static ServerMethodPluginRegistration GetRegistrationByPluginGuid(string pluginGuid)
        {
            return GlobalRegistrations.SelectMany(kv => kv.Value).FirstOrDefault(reg => reg.PluginGuid.Equals(pluginGuid, StringComparison.OrdinalIgnoreCase));
        }

        public static void Register(string pluginAssemblyInstanceId, PluginInfo pluginInfo)
        {
            try
            {
                var reg = ServerMethodPluginRegistration.Create(pluginAssemblyInstanceId, pluginInfo);

                lock (GlobalRegistrations)
                {
                    if (!GlobalRegistrations.ContainsKey(pluginAssemblyInstanceId))
                    {
                        GlobalRegistrations.Add(pluginAssemblyInstanceId, new List<ServerMethodPluginRegistration>());
                    }

                    GlobalRegistrations[pluginAssemblyInstanceId].Add(reg);
                }
            }
            catch (Exception ex)
            {
                SessionLog.Error($"Failed to instantiate plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. See exception that follows.");
                SessionLog.Exception(ex);
            }
        }

        // called when an inline assembly is updated
        public static void HandleAssemblyUpdated(string pluginAssemblyInstanceId, List<PluginInfo> pluginList)
        {
            lock (GlobalRegistrations)
            {
                if (GlobalRegistrations.ContainsKey(pluginAssemblyInstanceId))
                {
                    ///GlobalConverterLookup.

                    GlobalRegistrations[pluginAssemblyInstanceId].Clear();
                }
                else
                {
                    GlobalRegistrations.Add(pluginAssemblyInstanceId, new List<ServerMethodPluginRegistration>());
                }

                pluginList.ForEach(pluginInfo =>
                {
                    var reg = ServerMethodPluginRegistration.Create(pluginAssemblyInstanceId, pluginInfo);

                    GlobalRegistrations[pluginAssemblyInstanceId].Add(reg);

                });
            }

            lock (EndpointInstanceUse)
            {
                if (EndpointInstanceUse.ContainsKey(pluginAssemblyInstanceId))
                {
                    foreach(var ep in EndpointInstanceUse[pluginAssemblyInstanceId])
                    {
                        ep.HandleAssemblyUpdated(pluginAssemblyInstanceId);
                    }

                    EndpointInstanceUse.Remove(pluginAssemblyInstanceId);
                }
            }
        }

        public static void RebuildCacheForAllApps()
        {
            var appCollection = Settings.SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications);

            foreach (var app in appCollection) app.BuildAndCacheServerMethodJsAndTSD();
        }

    }

}