using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using jsdal_plugin;

namespace jsdal_server_core
{
    public static class BackgroundThreadManager
    {
        public static void Register(PluginInfo pluginInfo)
        {
            try
            {
                // TODO: Instantiate for each configured App/EP? Also need to look at EP Creation Event and EP stopped & deleted event. 
            }
            catch (Exception ex)
            {
                SessionLog.Error($"Failed to instantiate plugin '{pluginInfo.Name}' ({pluginInfo.Guid}) from assembly {pluginInfo.Assembly.FullName}. See exception that follows.");
                SessionLog.Exception(ex);
            }
        }
    }
}