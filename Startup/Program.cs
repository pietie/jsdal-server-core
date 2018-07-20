using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Server.HttpSys;
using System.Diagnostics;

namespace jsdal_server_core
{
    public class Program
    {
        private static DateTime? _startDate;

        public static DateTime? StartDate
        {
            get { return _startDate; }
        }

        static StreamWriter consoleWriter;
        static FileStream fs;


        public static void Main(string[] args)
        {
            try
            {
                var pathToContentRoot = Directory.GetCurrentDirectory();
                //OverrideStdout();
                if (Debugger.IsAttached)
                {
                    // var pathToExe = Process.GetCurrentProcess().MainModule.FileName;
                    //  pathToContentRoot = Path.GetDirectoryName(pathToExe);
                }
                else
                {
                    OverrideStdout();
                }

                Console.WriteLine("=================================");
                Console.WriteLine("Application started.");

                UserManagement.loadUsersFromFile();
                ExceptionLogger.Init();

                Settings.ObjectModel.ConnectionStringSecurity.init();

                if (SettingsInstance.LoadSettingsFromFile())
                {
                    WorkSpawner.Start();
                }

                PluginManager.CompileListOfAvailablePlugins();

                _startDate = DateTime.Now;

                BuildWebHost(pathToContentRoot, args).Build().Run();
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                Console.WriteLine(ex.ToString());
            }
        }

        public static void OverrideStdout()
        {
            try
            {
                var logDir = Path.GetFullPath("./log");

                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var filename = string.Format("{0:yyyy-MM-dd}.txt", DateTime.Now);

                fs = new FileStream(Path.Combine(logDir, filename), FileMode.Append);

                consoleWriter = new LogWriter(fs);

                consoleWriter.AutoFlush = true;

                Console.SetOut(consoleWriter);
                Console.SetError(consoleWriter);
                //    Console.WriteLine("Test: {0}{1}{2}{3}{4}{5}",1,2,3,4,5,6);
                //Console.WriteLine("Test: {0}{1}{2}{3}{4}{5}",1,2,3,4,5,6);

            }
            catch (Exception)
            {
                // Ignore?
            }

        }


        public static IWebHostBuilder BuildWebHost(string pathToContentRoot, string[] args)
        {
            Console.WriteLine("pathToContentRoot: {0}", pathToContentRoot);

            var webServerSettings = SettingsInstance.Instance.Settings.WebServer;

            // var certPath = "cert.pfx";
            // var certPassPath = Path.GetFullPath("cert.pass");

            // if (!File.Exists(certPassPath))
            // {
            //     throw new Exception($"Unable to find cert path file: {certPassPath}");
            // }

            // var certPass = System.IO.File.ReadAllText(certPassPath);



            return WebHost.CreateDefaultBuilder(args)
                  .UseContentRoot(pathToContentRoot)
                  .UseWebRoot(Path.Combine(pathToContentRoot, "wwwroot"))
                  .UseHttpSys(options =>
                  {
                      options.Authentication.Schemes = AuthenticationSchemes.None;
                      options.Authentication.AllowAnonymous = true;
                      options.MaxConnections = null;
                      options.MaxRequestBodySize = 30000000;


                      int interfaceCnt = 0;

                      if ((webServerSettings.EnableSSL ?? false)
                      && !string.IsNullOrWhiteSpace(webServerSettings.HttpsServerHostname)
                      && webServerSettings.HttpsServerPort.HasValue
                      && !string.IsNullOrWhiteSpace(webServerSettings.HttpsCertHash))
                      {
                          try
                          {
                              var httpsUrl = $"https://{webServerSettings.HttpsServerHostname}:{webServerSettings.HttpsServerPort.Value}";

                              if (NetshWrapper.ValidateSSLCertBinding(webServerSettings.HttpsServerHostname, webServerSettings.HttpsServerPort.Value))
                              {
                                  if (NetshWrapper.ValidateUrlAcl(true, webServerSettings.HttpsServerHostname, webServerSettings.HttpsServerPort.Value))
                                  {
                                      options.UrlPrefixes.Add(httpsUrl);
                                      interfaceCnt++;
                                  }
                                  else
                                  {
                                      if (NetshWrapper.AddUrlToACL(true, webServerSettings.HttpsServerHostname, webServerSettings.HttpsServerPort.Value))
                                      {
                                          options.UrlPrefixes.Add(httpsUrl);
                                          interfaceCnt++;
                                      }
                                      else
                                      {
                                          SessionLog.Error($"The url '{httpsUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                          Console.WriteLine($"ERROR: The url '{httpsUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                      }
                                  }
                              }
                              else
                              {
                                  SessionLog.Error($"There is no SSL cert binding for '{httpsUrl}' so a listener for this URL cannot be started.");
                                  Console.WriteLine($"There is no SSL cert binding for '{httpsUrl}' so a listener for this URL cannot be started.");
                              }


                          }
                          catch (Exception ex)
                          {
                              SessionLog.Exception(ex);
                          }
                      }

                      if (webServerSettings.EnableBasicHttp ?? false)
                      {
                          try
                          {
                              var httpUrl = $"http://{webServerSettings.HttpServerHostname}:{webServerSettings.HttpServerPort}";

                              if (NetshWrapper.ValidateUrlAcl(false, webServerSettings.HttpServerHostname, webServerSettings.HttpServerPort.Value))
                              {
                                  options.UrlPrefixes.Add(httpUrl);
                                  interfaceCnt++;
                              }
                              else
                              {
                                  if (NetshWrapper.AddUrlToACL(false, webServerSettings.HttpServerHostname, webServerSettings.HttpServerPort.Value))
                                  {
                                      options.UrlPrefixes.Add(httpUrl);
                                      interfaceCnt++;
                                  }
                                  else
                                  {
                                      SessionLog.Error($"The url '{httpUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                      Console.WriteLine($"ERROR: The url '{httpUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                  }
                              }
                          }
                          catch (Exception ex)
                          {
                              SessionLog.Exception(ex);
                          }
                      }

                      if (interfaceCnt == 0)
                      {
                          Console.WriteLine("No valid interface (http or https) found so defaulting to localhost:9086");
                          options.UrlPrefixes.Add("http://localhost:9086");
                      }



                  })
                //   .UseKestrel(options =>
                //   {
                //       options.AddServerHeader = false;

                //       // TODO: Allow more config here, especially the limits
                //       //!options.Limits.MaxConcurrentConnections


                //       if (webServerSettings.EnableSSL ?? false)
                //       {

                //           if (File.Exists(certPath))
                //           {
                //               options.Listen(System.Net.IPAddress.Any, webServerSettings.HttpsServerPort ?? 44312, listenOptions =>
                //               {
                //                   try
                //                   {
                //                       listenOptions.UseHttps(certPath, certPass);
                //                   }
                //                   catch (System.Exception ex)
                //                   {
                //                       SessionLog.Exception(ex);
                //                       Console.WriteLine(ex.ToString());
                //                       throw;
                //                   }
                //               });
                //           }
                //           else
                //           {
                //               Console.WriteLine("Cannot start HTTPS listener: The cert file '{0}' does not exists.", certPath);
                //           }
                //       }

                //       if (webServerSettings.EnableBasicHttp ?? false)
                //       {
                //           options.Listen(System.Net.IPAddress.Any, webServerSettings.HttpServerPort ?? 9086); // http
                //       }

                //   })
                .UseStartup<Startup>();
            //     .UseUrls("http://localhost:9086", "https://*:4430")


        }

    }



}
