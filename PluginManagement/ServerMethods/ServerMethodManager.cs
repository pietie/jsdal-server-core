using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace jsdal_server_core.PluginManagement
{
    public static class ServerMethodManager
    {
        private static List<ServerMethodPluginRegistration> Registrations { get; set; }

        public static ReadOnlyCollection<ServerMethodPluginRegistration> GetRegistrations()
        {
            return Registrations.AsReadOnly();
        }

        public static string TEMPLATE_ServerMethodContainer { get; private set; }
        public static string TEMPLATE_ServerMethodFunctionTemplate { get; private set; }
        public static string TEMPLATE_ServerMethodTypescriptDefinitionsContainer { get; private set; }

        static ServerMethodManager()
        {
            try
            {
                Registrations = new List<ServerMethodPluginRegistration>();

                ServerMethodManager.TEMPLATE_ServerMethodContainer = File.ReadAllText("./resources/ServerMethodContainer.txt");
                ServerMethodManager.TEMPLATE_ServerMethodFunctionTemplate = File.ReadAllText("./resources/ServerMethodTemplate.txt");
                ServerMethodManager.TEMPLATE_ServerMethodTypescriptDefinitionsContainer = File.ReadAllText("./resources/ServerMethodsTSDContainer.d.ts");
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public static void Register(PluginInfo pluginInfo)
        {
            try
            {
                Registrations.Add(ServerMethodPluginRegistration.Create(pluginInfo));
            }
            catch (Exception ex)
            {
                SessionLog.Error($"Failed to instantiate plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. See exception that follows.");
                SessionLog.Exception(ex);
            }
        }

        public static void RebuildCacheForAllApps()
        {
            var appCollection = Settings.SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications);

            foreach (var app in appCollection) app.BuildAndCacheServerMethodJsAndTSD();
        }

    }

}