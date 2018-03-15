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

            var certPath = "cert.pfx"; // TODO: get from config
            var certPass = "L00k@tm3";

            return WebHost.CreateDefaultBuilder(args)
                  .UseContentRoot(pathToContentRoot)
                  .UseKestrel(options =>
                  {
                      options.AddServerHeader = false;

                      if (File.Exists(certPath))
                      {
                          // TODO: Get port from config
                          options.Listen(System.Net.IPAddress.Any, 44312, listenOptions =>
                          {
                              try
                              {
                                  listenOptions.UseHttps(certPath, certPass);
                              }
                              catch (System.Exception ex)
                              {
                                  Console.WriteLine(ex.ToString());
                                  throw;
                              }
                          });
                      }
                      else
                      {
                          Console.WriteLine("Cannot start HTTPS listener: The cert file '{0}' does not exists.", certPath);
                      }

                      options.Listen(System.Net.IPAddress.Any, 9086); // http

                  })
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

                Console.WriteLine(trustedAssembliesPaths);



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
