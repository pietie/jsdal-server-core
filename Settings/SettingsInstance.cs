using System;
using System.IO;
using Newtonsoft.Json;

namespace jsdal_server_core.Settings
{
    public class SettingsInstance
    {
        private static JsDalServerConfig _instance;
        public static JsDalServerConfig Instance
        {
            get
            {
                return SettingsInstance._instance;
            }
        }

        public static string settingsFilePath
        {
            get { return "./jsdal-server.json"; }
        }

        public static void saveSettingsToFile()
        {
            try
            {
                var json = JsonConvert.SerializeObject(SettingsInstance._instance);

                File.WriteAllText(SettingsInstance.settingsFilePath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ExceptionLogger.logException(ex);
            }
        }

        public static bool loadSettingsFromFile()
        {
            try
            {
                if (!File.Exists(SettingsInstance.settingsFilePath)) return false;

                var data = File.ReadAllText(SettingsInstance.settingsFilePath, System.Text.Encoding.UTF8);

                var settingsInst = JsonConvert.DeserializeObject<JsDalServerConfig>(data, new JsonConverter[] { new ObjectModel.RuleJsonConverter() });

                settingsInst.ProjectList.ForEach(p => p.DatabaseSources.ForEach(dbs =>
                                {
                                    dbs.loadCache();
                                }));


                SettingsInstance._instance = settingsInst;
                return true;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return false;
            }

        }

    }
}