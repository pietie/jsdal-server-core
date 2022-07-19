namespace jsdal_server_core.Settings.ObjectModel
{
    public class CommonSettings
    {
        public string GoogleRecaptchaSecret;
        public int DbSource_CheckForChangesInMilliseconds = 800;
        public WebServerSettings WebServer;

        public bool AutoStartTraceCounters { get; set; }

        /*public static createFromJson(rawJson: any): Settings {
            if (!rawJson) return null;
            let settings = new Settings();

            settings.GoogleRecaptchaSecret = rawJson.GoogleRecaptchaSecret;
            settings.DbSource_CheckForChangesInMilliseconds = rawJson.DbSource_CheckForChangesInMilliseconds;
            settings.WebServer = WebServerSettings.createFromJson(rawJson);

            return settings;
        }*/
    }

}