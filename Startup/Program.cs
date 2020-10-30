using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Server.HttpSys;
using System.Diagnostics;
using System.Globalization;
using jsdal_server_core.PluginManagement;
using Serilog;
using Serilog.Events;
using jsdal_server_core.Performance.DataCollector;
using jsdal_server_core.Performance;

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

        public static bool IsShuttingDown { get; private set; }


        static System.Collections.Concurrent.ConcurrentDictionary<string, string> dict = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        public static void Main(string[] args)
        {
            IsShuttingDown = false;

            var loggerConfig = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                        .Enrich.FromLogContext()

                        .WriteTo.File("log/detail-.txt",
                                //?restrictedToMinimumLevel: LogEventLevel.Warning,
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 7,
                                shared: true,
                                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3} {Message:lj}{NewLine}{Exception}"
                                )
                      // .WriteTo.File("log/info-.txt",
                      //         rollingInterval: RollingInterval.Day,
                      //         shared: true,
                      //         outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3} {Message:lj}{NewLine}{Exception}",
                      //         restrictedToMinimumLevel: LogEventLevel.Information
                      //         )
                      ;

            try
            {

                var isService = args.Length == 1 && args[0].Equals("--service", StringComparison.OrdinalIgnoreCase);
                var justRun = args.Length == 1 && args[0].Equals("--run", StringComparison.OrdinalIgnoreCase);

                if (isService)
                {
                    var pathToExe = Process.GetCurrentProcess().MainModule.FileName;
                    var pathToContentRootService = Path.GetDirectoryName(pathToExe);
                    Directory.SetCurrentDirectory(pathToContentRootService);
                }
                else if (!Debugger.IsAttached && !justRun)
                {
                    TerminalUI.Init();
                    return;
                }
                else
                {
                    loggerConfig.WriteTo.Console();
                }

                Log.Logger = loggerConfig.CreateLogger();

                var pathToContentRoot = Directory.GetCurrentDirectory();

                if (Debugger.IsAttached)
                {
                    // var pathToExe = Process.GetCurrentProcess().MainModule.FileName;
                    //  pathToContentRoot = Path.GetDirectoryName(pathToExe);
                }
                else
                {
                    // now relying on serilog
                    // OverrideStdout();
                }

                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    var isTerminating = e.IsTerminating;

                    Log.Fatal("Unhandled exception", e.ExceptionObject);
                    ExceptionLogger.LogException(e.ExceptionObject as Exception);

                };

                Log.Information($"Application started with process id {System.Diagnostics.Process.GetCurrentProcess().Id}.");

                // I forget what the different msg types look like
                Log.Warning("This is what a warning looks like");
                Log.Error("This is what an error looks like");
                Log.Fatal("This is what a fatal error looks like");

                Log.Information("Loading users");
                UserManagement.LoadUsersFromFile();

                Log.Information("Initialising exception logger");
                ExceptionLogger.Init();

                Log.Information("Loading settings");

                if (SettingsInstance.LoadSettingsFromFile())
                {
                    //ServerMethodManager.RebuildCacheForAllApps();
                }

                Log.Information("Initialising jsDAL health monitor");
                jsDALHealthMonitorThread.Instance.Init();

                Log.Information("Initialising real-time tracker");
                RealtimeTrackerThread.Instance.Init();

                Log.Information("Initialising data collector");
                DataCollectorThread.Instance.Init();

                Log.Information("Initialising inline module manifest");
                InlineModuleManifest.Instance.Init();

                _startDate = DateTime.Now;

                Log.Information("Configuring global culture");
                var globalCulture = new System.Globalization.CultureInfo("en-US");

                // set global culture to en-US - will help with things like parsing numbers from Javascript(e.g. 10.123) as double/decimal even if server uses a comma as decimal separator for example
                CultureInfo.DefaultThreadCurrentCulture = globalCulture;
                CultureInfo.DefaultThreadCurrentUICulture = globalCulture;

                Log.Information("Building web host");
                var builder = BuildWebHost(pathToContentRoot, args);
                var host = builder.Build();

                if (isService)
                {
                    host.RunAsCustomService();
                }
                else
                {
                    host.RunAsync();

                    Console.WriteLine("\r\nPress CTRL+X to shutdown server");

                    while (true)
                    {
                        var keyInfo = Console.ReadKey(true);

                        if (keyInfo.Key == ConsoleKey.X && (keyInfo.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
                        {
                            //host.StopAsync();
                            ShutdownAllBackgroundThreads();
                            break;
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                SessionLog.Exception(ex);

                ShutdownAllBackgroundThreads();
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static void ShutdownAllBackgroundThreads()
        {
            IsShuttingDown = true;

            SessionLog.Info("Shutting down");

            Log.Information("Shutting down workers...");
            WorkSpawner.Shutdown();
            Log.Information("Shutting down counter monitor...");
            SignalR.HomeDashboard.DotNetCoreCounterListener.Instance?.Stop();
            Log.Information("Shutting down stats counter...");
            Performance.StatsDB.Shutdown();
            Log.Information("Shutting down common notifications...");
            Hubs.CommonNotificationThread.Instance?.Shutdown();
            Log.Information("Shutting down exception logger...");
            ExceptionLogger.Shutdown();
            Log.Information("Shutting down background thread plugins...");
            BackgroundThreadPluginManager.Instance?.Shutdown();


            Log.Information("Shutting down real-time tracker...");
            RealtimeTrackerThread.Instance.Shutdown();
            Log.Information("Shutting down data collector...");
            DataCollectorThread.Instance.Shutdown();

            Log.Information("Shutting jsDAL health monitor...");
            jsDALHealthMonitorThread.Instance.Shutdown();

            Log.Information("Shutting down session log...");
            SessionLog.Shutdown();

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

            }
            catch (Exception)
            {
                // Ignore?
            }

        }

        public static IWebHostBuilder BuildWebHost(string pathToContentRoot, string[] args)
        {
            var webServerSettings = SettingsInstance.Instance.Settings.WebServer;

            // var certPath = "cert.pfx";
            // var certPassPath = Path.GetFullPath("cert.pass");

            // if (!File.Exists(certPassPath))
            // {
            //     throw new Exception($"Unable to find cert path file: {certPassPath}");
            // }

            // var certPass = System.IO.File.ReadAllText(certPassPath);


            //                 var configurationBuilder = new ConfigurationBuilder();

            //                 configurationBuilder.AddJsonFile("./appsettings.json", false, true);

            // var appConfig = configurationBuilder.Build();


            return WebHost
                  .CreateDefaultBuilder(args)
                  .UseSerilog()
                  .UseContentRoot(pathToContentRoot)
                  .UseWebRoot(Path.Combine(pathToContentRoot, "wwwroot"))
                  .UseSetting(WebHostDefaults.SuppressStatusMessagesKey, "true")
                  //   .ConfigureAppConfiguration((builderContext, config) =>
                  //   {
                  //     IHostingEnvironment env = builderContext.HostingEnvironment;

                  //     config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                  //         //.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
                  //   })

                  .UseHttpSys(options =>
                  {
                      options.Authentication.Schemes = AuthenticationSchemes.None;
                      options.Authentication.AllowAnonymous = true;
                      options.MaxConnections = null;
                      options.MaxRequestBodySize = 30000000; //~30MB

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
                                      //!eventLog.Info($"Listening to {httpsUrl}");
                                      options.UrlPrefixes.Add(httpsUrl);
                                      interfaceCnt++;
                                  }
                                  else
                                  {
                                      if (NetshWrapper.AddUrlToACL(true, webServerSettings.HttpsServerHostname, webServerSettings.HttpsServerPort.Value))
                                      {
                                          //!eventLog.Info($"Listening to {httpsUrl}");
                                          options.UrlPrefixes.Add(httpsUrl);
                                          interfaceCnt++;
                                      }
                                      else
                                      {
                                          SessionLog.Error($"The url '{httpsUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                          Log.Error($"The url '{httpsUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                      }
                                  }
                              }
                              else
                              {
                                  SessionLog.Error($"There is no SSL cert binding for '{httpsUrl}' so a listener for this URL cannot be started.");
                                  Log.Error($"There is no SSL cert binding for '{httpsUrl}' so a listener for this URL cannot be started.");
                              }


                          }
                          catch (Exception ex)
                          {
                              SessionLog.Exception(ex);
                              Log.Error(ex, "HTTPS init failed");
                          }
                      }

                      if (webServerSettings.EnableBasicHttp ?? false)
                      {
                          try
                          {
                              var httpUrl = $"http://{webServerSettings.HttpServerHostname}:{webServerSettings.HttpServerPort}";

                              if (NetshWrapper.ValidateUrlAcl(false, webServerSettings.HttpServerHostname, webServerSettings.HttpServerPort.Value))
                              {
                                  //!eventLog.Info($"Listening to {httpUrl}");
                                  options.UrlPrefixes.Add(httpUrl);
                                  interfaceCnt++;
                              }
                              else
                              {
                                  if (NetshWrapper.AddUrlToACL(false, webServerSettings.HttpServerHostname, webServerSettings.HttpServerPort.Value))
                                  {
                                      //!eventLog.Info($"Listening to {httpUrl}");
                                      options.UrlPrefixes.Add(httpUrl);
                                      interfaceCnt++;
                                  }
                                  else
                                  {
                                      SessionLog.Error($"The url '{httpUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                      Log.Error($"The url '{httpUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                  }
                              }
                          }
                          catch (Exception ex)
                          {
                              SessionLog.Exception(ex);
                              Log.Error(ex, "Basic Http init failed");
                          }
                      }

                      if (interfaceCnt == 0)
                      {
                          Log.Warning("No valid interface (http or https) found so defaulting to localhost:9086");
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
                //                       Cons ole.WriteLine(ex.ToString());
                //                       throw;
                //                   }
                //               });
                //           }
                //           else
                //           {
                //               Cons ole.WriteLine("Cannot start HTTPS listener: The cert file '{0}' does not exists.", certPath);
                //           }
                //       }

                //       if (webServerSettings.EnableBasicHttp ?? false)
                //       {
                //           options.Listen(System.Net.IPAddress.Any, webServerSettings.HttpServerPort ?? 9086); // http
                //       }

                //   })
                .UseStartup<Startup>()
                .UseSetting(WebHostDefaults.DetailedErrorsKey, "true")

                ;
            //     .UseUrls("http://localhost:9086", "https://*:4430")


        }

    }
}