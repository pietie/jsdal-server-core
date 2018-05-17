using System;
using System.IO;
using Newtonsoft.Json;
using System.Linq;

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

        public static bool LoadSettingsFromFile()
        {
            try
            {
                if (!File.Exists(SettingsInstance.settingsFilePath))
                {
                    Console.WriteLine("WARN! Settings file not found: {0}", SettingsInstance.settingsFilePath);
                    return false;
                }

                var data = File.ReadAllText(SettingsInstance.settingsFilePath, System.Text.Encoding.UTF8);

                var settingsInst = JsonConvert.DeserializeObject<JsDalServerConfig>(data, new JsonConverter[] { new ObjectModel.RuleJsonConverter() });

                settingsInst.ProjectList.ForEach(p=>p.UpdateParentReferences());
                settingsInst.ProjectList.SelectMany(p => p.Applications.SelectMany(dbs => dbs.Endpoints))
                            .ToList()
                            .ForEach(ep => ep.LoadCache());

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