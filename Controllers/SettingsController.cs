using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography.X509Certificates;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class SettingsController : Controller
    {

        [HttpGet("/api/settings/certs")]
        public ApiResponse ListSslCerts()
        {
            try
            {
                var certStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);

                certStore.Open(OpenFlags.ReadOnly);

                var certList = certStore.Certificates.Cast<X509Certificate2>().ToList();
                var ret = certList.Select(cert => new
                {
                    cert.HasPrivateKey,
                    FriendlyName = !string.IsNullOrWhiteSpace(cert.FriendlyName) ? cert.FriendlyName : cert.Subject?.Substring(3),
                    cert.Thumbprint,
                    Subject = cert.Subject?.Substring(3),
                    cert.Issuer
                }).OrderBy(c => c.FriendlyName);

                return ApiResponse.Payload(ret);

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }


        }

        [HttpGet("/api/settings/bindings")]
        public ApiResponse GetBindings()
        {
            try
            {
                var ws = Settings.SettingsInstance.Instance.Settings.WebServer;

                return ApiResponse.Payload(new dynamic[]
                {
                    new { enabled = ws.EnableBasicHttp, hostname = ws.HttpServerHostname, port = ws.HttpServerPort },
                    new { enabled = ws.EnableSSL, hostname = ws.HttpsServerHostname, port = ws.HttpsServerPort, certThumb = ws.HttpsCertHash }
                });
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpPost("/api/settings/bindings")]
        public ApiResponse SaveBindings([FromBody] dynamic bindings)
        {
            try
            {
                var http = bindings[0];
                var https = bindings[1];

                var ws = Settings.SettingsInstance.Instance.Settings.WebServer;

                ws.EnableBasicHttp = http.enabled;
                ws.HttpServerHostname = http.hostname;
                ws.HttpServerPort = http.port;

                ws.EnableSSL = https.enabled;
                ws.HttpsServerHostname = https.hostname;
                ws.HttpsServerPort = https.port;
                ws.HttpsCertHash = https.certThumb;

                if (ws.EnableSSL ?? false)
                {
                    if (string.IsNullOrWhiteSpace(ws.HttpsCertHash))
                    {
                        return ApiResponse.ExclamationModal("Please specify an SSL cert to use if HTTPS is enabled.");
                    }
                    // note these commands require admin rights (does not work during debugging but will when run as a Windows service)
                    NetshWrapper.Unregister(ws.HttpsServerHostname, ws.HttpsServerPort.Value);
                    NetshWrapper.Register(ws.HttpsServerHostname, ws.HttpsServerPort.Value, ws.HttpsCertHash);
                }

                Settings.SettingsInstance.SaveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }
    }

}