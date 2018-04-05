using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using jsdal_plugin;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Diagnostics;
using Microsoft.AspNetCore.Server.HttpSys;

namespace jsdal_server_core
{
    public class Program
    {
        private static DateTime? _startDate;

        public static DateTime? StartDate
        {
            get { return _startDate; }
        }

        // TODO: Move somewhere else?
        public static Dictionary<Assembly, List<PluginInfo>> PluginAssemblies { get; private set; }

        static StreamWriter consoleWriter;
        static FileStream fs;

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

                if (SettingsInstance.loadSettingsFromFile())
                {
                    WorkSpawner.Start();
                }

                CompileListOfAvailablePlugins();

                _startDate = DateTime.Now;
                var host = BuildWebHost(pathToContentRoot, args);

                host.Run();
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                Console.WriteLine(ex.ToString());
            }
        }

        public static IWebHost BuildWebHost(string pathToContentRoot, string[] args)
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
                                      SessionLog.Error($"The url '{httpsUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                      Console.WriteLine($"ERROR: The url '{httpsUrl}' was not found in ACL list so a listener for this URL cannot be started.");
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
                                  SessionLog.Error($"The url '{httpUrl}' was not found in ACL list so a listener for this URL cannot be started.");
                                  Console.WriteLine($"ERROR: The url '{httpUrl}' was not found in ACL list so a listener for this URL cannot be started.");
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
                .UseStartup<Startup>()
                //     .UseUrls("http://localhost:9086", "https://*:4430")
                .Build();

        }

        private static void CompileListOfAvailablePlugins()
        {
            PluginAssemblies = new Dictionary<Assembly, List<PluginInfo>>();

            LoadPluginsFromSource();

            if (Directory.Exists("./plugins"))
            {
                var dllCollection = Directory.EnumerateFiles("plugins", "*.dll", SearchOption.TopDirectoryOnly);

                foreach (var dll in dllCollection)
                {
                    LoadPluginDLL(dll);
                }
            }
        }

        private static void LoadPluginsFromSource()
        {
            try
            {
                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(TmpGetPluginSource());

                // TODO: CLeanup and find a better way to add the standard deps!
                var runtimeAssembly = Assembly.Load("System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
                var netstandardAssembly = Assembly.Load("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
                var collectionsAssemlby = Assembly.Load("System.Collections, Version=0.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");

                var pluginBasePath = "jsdal-plugin.dll";

                if (Debugger.IsAttached)
                {
                    pluginBasePath = "./../jsdal-plugin/bin/Debug/netstandard2.0/jsdal-plugin.dll";
                }

                var pluginBaseRef = MetadataReference.CreateFromFile(pluginBasePath);

                var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);


                string assemblyName = Path.GetRandomFileName();
                MetadataReference[] references = new MetadataReference[]
                {
                    MetadataReference.CreateFromFile(typeof(Object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Data.SqlClient.SqlConnection).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Data.Common.DbCommand).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Runtime.InteropServices.GuidAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.ComponentModel.Component).Assembly.Location),
                    pluginBaseRef,
                    MetadataReference.CreateFromFile(runtimeAssembly.Location),
                    MetadataReference.CreateFromFile(netstandardAssembly.Location),
                    MetadataReference.CreateFromFile(collectionsAssemlby.Location)
                };


                var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                                .WithUsings(new string[] { "System" });

                CSharpCompilation compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: compilationOptions
                    );


                using (var ms = new MemoryStream())
                {
                    EmitResult result = compilation.Emit(ms);

                    if (!result.Success)
                    {
                        IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                            diagnostic.IsWarningAsError ||
                            diagnostic.Severity == DiagnosticSeverity.Error);

                        foreach (Diagnostic diagnostic in failures)
                        {
                            Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                        }
                    }
                    else
                    {
                        ms.Seek(0, SeekOrigin.Begin);
                        Assembly assembly = Assembly.Load(ms.ToArray());

                        LoadPluginFromAssembly(assembly);
                        // var whatthe = assembly.DefinedTypes.ToList();
                        //                         var xxxxx = assembly.GetExportedTypes();
                        //                         var tt = assembly.GetTypes();


                        // // Type type = assembly.GetType("Plugins.TokenGuidAuthPlugin");

                        // // var guid = pluginType.GetCustomAttribute(typeof(System.Runtime.InteropServices.GuidAttribute)) as System.Runtime.InteropServices.GuidAttribute;

                        // // if (guid == null || string.IsNullOrEmpty(guid.Value))
                        // // {
                        // //     SessionLog.Error("Plugin '{0}' does not have a Guid attribute defined on the class level. Add a System.Runtime.InteropServices.GuidAttribute to the class with a unique Guid value.", pluginType.FullName);
                        // //     continue;
                        // // }

                        // // jsDALPlugin plugin = (jsDALPlugin)Activator.CreateInstance(type);

                        // // PluginInfo plugInfo = new PluginInfo();

                        // // plugInfo.Assembly = assembly;
                        // // plugInfo.Description = plugin.Description;
                        // // //plugInfo.Guid = Gui
                        // // plugInfo.Name = plugin.Name;

                        // // PluginAssemblies.Add(assembly, new List<PluginInfo>() { plugInfo });


                    }
                }

            }
            catch (Exception ex)
            {
                //SessionLog.Error("Failed to load plugin DLL '{0}'. See exception that follows.", filepath);
                SessionLog.Exception(ex);
            }
        }

        private static void LoadPluginDLL(string filepath)
        {
            try
            {
                var pluginAssembly = Assembly.LoadFrom(filepath);

                LoadPluginFromAssembly(pluginAssembly);
            }
            catch (Exception ex)
            {
                SessionLog.Error("Failed to load plugin DLL '{0}'. See exception that follows.", filepath);
                SessionLog.Exception(ex);
            }
        }

        private static void LoadPluginFromAssembly(Assembly pluginAssembly)
        {
            if (pluginAssembly.DefinedTypes != null)
            {
                var pluginTypeList = pluginAssembly.DefinedTypes.Where(typ => typ.IsSubclassOf(typeof(jsDALPlugin))).ToList();

                if (pluginTypeList != null && pluginTypeList.Count > 0)
                {
                    foreach (var pluginType in pluginTypeList)
                    {
                        jsDALPlugin concrete;
                        var pluginInfo = new PluginInfo();

                        try
                        {
                            var guid = pluginType.GetCustomAttribute(typeof(System.Runtime.InteropServices.GuidAttribute)) as System.Runtime.InteropServices.GuidAttribute;

                            if (guid == null || string.IsNullOrEmpty(guid.Value))
                            {
                                SessionLog.Error("Plugin '{0}' does not have a Guid attribute defined on the class level. Add a System.Runtime.InteropServices.GuidAttribute to the class with a unique Guid value.", pluginType.FullName);
                                continue;
                            }

                            concrete = (jsDALPlugin)pluginAssembly.CreateInstance(pluginType.FullName);

                            pluginInfo.Assembly = pluginAssembly;
                            pluginInfo.Name = concrete.Name;
                            pluginInfo.Description = concrete.Description;
                            pluginInfo.TypeInfo = pluginType;
                            pluginInfo.Guid = Guid.Parse(guid.Value);

                            //pluginInfo.InitMethod = typeof(jsDALPlugin).GetMethod("InitPlugin", BindingFlags.NonPublic | BindingFlags.Instance);

                            SessionLog.Info("Plugin '{0}' ({1}) loaded. Assembly: {2}", pluginInfo.Name, pluginInfo.Guid, pluginAssembly.FullName);
                        }
                        catch (Exception ex)
                        {
                            SessionLog.Error("Failed to instantiate type '{0}'. See the exception that follows.", pluginType.FullName);
                            SessionLog.Exception(ex);
                        }


                        if (!PluginAssemblies.ContainsKey(pluginAssembly)) PluginAssemblies.Add(pluginAssembly, new List<PluginInfo>());
                        PluginAssemblies[pluginAssembly].Add(pluginInfo);
                    }


                }
                else
                {
                    SessionLog.Warning("Failed to find any jsDAL Server plugins in the assembly '{0}'. Make sure you have public class available that derives from jsDALPlugin.", pluginAssembly.Location);
                }
            }
            else
            {
                SessionLog.Warning("Failed to find any jsDAL Server plugins in the assembly '{0}'. Make sure you have public class available that derives from jsDALPlugin.", pluginAssembly.Location);
            }
        }

        // TODO: This needs to be sourcs from somewhere - a Project or DBSource or just system-wide.
        public static string TmpGetPluginSource()
        {
            return @"
               using System;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using jsdal_plugin;

namespace Plugins
{
      [Guid(""2003F9A8-5707-4B79-A216-737A7F11EB83"")]
    public class TokenGuidAuthPlugin : jsDALPlugin
        {
            public TokenGuidAuthPlugin()
            {
                this.Name = ""TokenGuid Auth"";
                this.Description = ""Call LoginSetContextInfo for sproc authentication, using the 'tokenGuid' query string parameter."";
            }

            public override void OnConnectionOpened(SqlConnection con)
            {
                base.OnConnectionOpened(con);

                if (this.QueryString.ContainsKey(""tokenGuid""))
                {
                    var tg = Guid.Parse(this.QueryString[""tokenGuid""]);

                    var cmd = new SqlCommand
                    {
                        Connection = con,
                        CommandText = ""LoginSetContextInfo"",
                        CommandType = System.Data.CommandType.StoredProcedure
                    };

                    string sessionId = null;// TODO: get from cookies?


                    cmd.Parameters.Add(""@tokenGuid"", System.Data.SqlDbType.UniqueIdentifier).Value = tg;

                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

               ";
        }
    }



}


// TODO: Move to separate lib?
/*
public class jsDALPlugin
{
    public Dictionary<string, string> QueryString { get; private set; }

    public string Name { get; protected set; }
    public string Description { get; protected set; }

    private void InitPlugin(Dictionary<string, string> queryStringCollection)
    {
        this.QueryString = queryStringCollection;
    }

    public jsDALPlugin()
    {

    }

    public virtual void OnConnectionOpened(System.Data.SqlClient.SqlConnection con) { }
}

 */
