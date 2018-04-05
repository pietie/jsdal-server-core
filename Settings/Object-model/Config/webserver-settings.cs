namespace jsdal_server_core.Settings.ObjectModel
{
    public class WebServerSettings
    {
        public string HttpServerHostname;
        public int? HttpServerPort;
        public bool? EnableBasicHttp = true;
        public bool? EnableSSL = false;
        public string HttpsServerHostname;
        public int? HttpsServerPort;

        public string HttpsCertHash;
/*****
        public static WebServerSettings createFromJson(rawJson: any)
        {
            if (!rawJson || typeof(rawJson.WebServer) === "undefined") return null;
            let settings = new WebServerSettings();

            settings.HttpServerHostname = rawJson.WebServer.HttpServerHostname;
            settings.HttpServerPort = rawJson.WebServer.HttpServerPort;

            settings.EnableBasicHttp = !!rawJson.WebServer.EnableBasicHttp;
            settings.EnableSSL = !!rawJson.WebServer.EnableSSL;
            settings.HttpsServerHostname = rawJson.WebServer.HttpsServerHostname;
            settings.HttpsServerPort = rawJson.WebServer.HttpsServerPort;

            return settings;
        }
        **/
    }

}