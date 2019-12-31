using System;

using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;


using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis.CSharp;
using Extensions;
using System.IO;
using MirrorSharp;
using Newtonsoft.Json;
using jsdal_server_core.Hubs;
using jsdal_server_core.Hubs.Performance;
using jsdal_server_core.PluginManagement;
using jsdal_server_core.Settings.ObjectModel;
using jsdal_server_core.SignalR.HomeDashboard;
using Microsoft.Extensions.Hosting;

namespace jsdal_server_core
{
    public class Startup
    {
        public IConfiguration Configuration { get; private set; }
        public IWebHostEnvironment HostingEnvironment { get; private set; }

        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            //  var configurationBuilder = new ConfigurationBuilder();

            //  configurationBuilder.AddJsonFile("./appsettings.json", false, true);

            //  var c = configurationBuilder.Build();

            HostingEnvironment = env;

            Console.WriteLine($"WebRootPath: {env.WebRootPath}");
            Console.WriteLine($"BarcodeService.URL: {Configuration["AppSettings:BarcodeService.URL"]?.TrimEnd('/')}" ?? "(Not set!)");
        }



        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(typeof(PluginLoader));
            services.AddSingleton(typeof(BackgroundThreadPluginManager));
            services.AddSingleton(typeof(MainStatsMonitorThread));
            services.AddSingleton(typeof(WorkerMonitor));
            services.AddSingleton(typeof(RealtimeMonitor));
            services.AddSingleton(typeof(BackgroundTaskMonitor));
            services.AddSingleton(typeof(Settings.ObjectModel.ConnectionStringSecurity));
            services.AddSingleton(typeof(DotNetCoreCounterListener));

            services.AddCors(options => options.AddPolicy("CorsPolicy",
                    builder => builder
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        //.SetIsOriginAllowed(s=>s.Equals("http://localhost:4200"))
                        .SetIsOriginAllowed(s => true)
                        //.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowCredentials()
                        .AllowAnyHeader()
                        .SetPreflightMaxAge(TimeSpan.FromMinutes(10))
                        .Build()));
            //             var policy = new Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicy();

            //             policy.Headers.Add("*");
            //             policy.Methods.Add("*");
            //             policy.Origins.Add("*");
            //             policy.SupportsCredentials = true;

            // policy.PreflightMaxAge = TimeSpan.FromSeconds(600);
            //             services.AddCors(x => x.AddPolicy("CorsPolicy", policy));

            services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
                .AddJwtBearer(cfg =>
                {
                    cfg.RequireHttpsMetadata = false;
                    cfg.SaveToken = true;
                    cfg.IncludeErrorDetails = false;

                    var issuer = Configuration["Tokens:Issuer"];
                    var key = Configuration["Tokens:Key"];

                    Console.WriteLine("Issuer: {0}, Key: {1}", issuer, key);

                    var symKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

                    cfg.TokenValidationParameters = new TokenValidationParameters()
                    {
                        ValidIssuer = issuer,
                        ValidAudience = issuer,
                        IssuerSigningKey = symKey
                    };

                });

            var settings = new JsonSerializerSettings();
            settings.ContractResolver = new DefaultContractResolver();

            var serializer = JsonSerializer.Create(settings);

            services.Configure<Microsoft.AspNetCore.Server.HttpSys.HttpSysOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            services.Add(new ServiceDescriptor(typeof(JsonSerializer),
                                               provider => serializer,
                                               ServiceLifetime.Transient));

            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
            })
                .AddNewtonsoftJsonProtocol(options =>
                {
                    options.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver();
                    //                    options.PayloadSerializerSettings.Converters.Add(new AccountIdConverter());

                }) // required on .NET CORE 3 preview for now as the System.Text JSON implementation does not deserialize Dictionaries correctly (or in the same way at least)
                .AddJsonProtocol(options =>
                {
                    //options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.Serialization.JsonNamingPolicy.CamelCase
                    //options.UseCamelCase = false;
                    // TODO: temp solution until .NET Core 3 ships with the PayloadSerializerSettings property again...or until I figure out what dependency I have missing!
                    // var field = options.GetType().GetField("_serializerOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    // var val = field.GetValue(options);
                    // var so = (System.Text.Json.Serialization.JsonSerializerOptions)field.GetValue(options);


                    //!?options.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver();
                });

            services.AddMvc(options => { options.EnableEndpointRouting = false; })
                .AddNewtonsoftJson(options =>
                    {

                        //options.SerializerSettings = new JsonSerializerSettings() { };
                        options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                        options.SerializerSettings.Converters.Add(new ApiSingleValueOutputWrapperConverter());


                        //new DefaultContractResolver();
                    })

                    .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            ;


            var dataProtectionKeyPath = new System.IO.DirectoryInfo(Path.Combine(System.IO.Directory.GetCurrentDirectory(), "data\\keys"));

            Console.WriteLine($"dataProtectionKeyPath: {dataProtectionKeyPath}");

            services.AddDataProtection()
                    .PersistKeysToFileSystem(dataProtectionKeyPath)
                    .SetApplicationName("jsDAL Server")
                    .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
                    {
                        EncryptionAlgorithm = EncryptionAlgorithm.AES_256_GCM,
                        ValidationAlgorithm = ValidationAlgorithm.HMACSHA512
                    });


        }

        public class ApiSingleValueOutputWrapperConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Controllers.ServerMethodsController.ApiSingleValueOutputWrapper);
            }

            // this converter is only used for serialization, not to deserialize
            public override bool CanRead => false;

            // implement this if you need to read the string representation to create an AccountId
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var wrapper = (Controllers.ServerMethodsController.ApiSingleValueOutputWrapper)value;

                if (wrapper.Value == null)
                {
                    writer.WriteNull();
                    return;
                }
                // TODO: Can we use traversal during SerializeCSharpToJavaScript to track symbols and their required Output Converters  
                var serialisedValue = GlobalTypescriptTypeLookup.SerializeCSharpToJavaScript(wrapper.Name, wrapper.Value);

                writer.WriteRawValue(serialisedValue);
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory, IHostApplicationLifetime applicationLifetime)
        {
            //app.UseDeveloperExceptionPage();

            // force instantiation on singletons
            {
                var pmInst = app.ApplicationServices.GetService<PluginLoader>();

                app.ApplicationServices.GetService<MainStatsMonitorThread>();

                WorkerMonitor.Instance = app.ApplicationServices.GetService<WorkerMonitor>();
                RealtimeMonitor.Instance = app.ApplicationServices.GetService<RealtimeMonitor>();
                BackgroundTaskMonitor.Instance = app.ApplicationServices.GetService<BackgroundTaskMonitor>();

                ConnectionStringSecurity.Instance = app.ApplicationServices.GetService<ConnectionStringSecurity>();

                DotNetCoreCounterListener.Instance = app.ApplicationServices.GetService<DotNetCoreCounterListener>();
                DotNetCoreCounterListener.Instance.Start();

                {// More app startup stuff...but have a dependency on the singleton objects above. Can we move this somewhere else?
                    PluginLoader.Instance = pmInst;
                    PluginLoader.Instance.Init();
                    
                    ServerMethodManager.RebuildCacheForAllApps();

                    Console.WriteLine("Starting work spawner.");
                    WorkSpawner.Start();
                }
            }


            applicationLifetime.ApplicationStopped.Register(() =>
            {
                Console.WriteLine("!!!  Stopped reached");
            });

            applicationLifetime.ApplicationStopping.Register(() =>
            {
                Console.WriteLine("!!!  Stopping reached");
            });



            //       loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            //     loggerFactory.AddDebug();





            // app.Use(async (httpContext, next) =>
            // {
            //     //if (httpContext.Request.Path.Value.Contains("api/") && httpContext.Request.Method == "OPTIONS")
            //     if (httpContext.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            //     {
            //         httpContext.Response.Headers.Add("Access-Control-Max-Age", "600");
            //        // return;
            //     }
            //     await next();
            // });

            var webSocketOptions = new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(120),
                ReceiveBufferSize = 4 * 1024,
            };


            app.UseWebSockets(webSocketOptions);
            app.UseCors("CorsPolicy");

            var assemblyBasePath = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location);

            MetadataReference[] all = { MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                                        //?MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "mscorlib.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.Core.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.Runtime.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.Collections.dll")),
                                        MetadataReference.CreateFromFile(Path.Combine(assemblyBasePath, "System.Data.dll")),
                                        //MetadataReference.CreateFromFile(typeof(System.Collections.ArrayList).Assembly.Location),
                                        //MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<string,string>).Assembly.Location),
                                        MetadataReference.CreateFromFile(typeof(System.Data.SqlClient.SqlConnection).Assembly.Location)

            };

            var jsDALBasePluginPath = Path.GetFullPath("./plugins/jsdal-plugin.dll");

            if (File.Exists("./plugins/jsdal-plugin.dll"))
            {
                Array.Resize(ref all, all.Length + 1);

                all[all.Length - 1] = Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(jsDALBasePluginPath);
            }
            else
            {
                Console.WriteLine($"ERR! Failed to find base plugin assembly at {jsDALBasePluginPath}");
                SessionLog.Error($"Failed to find base plugin assembly at {jsDALBasePluginPath}");
            }



            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            //            generalDiagnosticOption: ReportDiagnostic.Suppress 
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
                                            {
                                            { "CS1701", ReportDiagnostic.Suppress }, // Binding redirects
                                            { "CS1702", ReportDiagnostic.Suppress },
                                            { "CS1705", ReportDiagnostic.Suppress }
                                            }
            );

            var mirrorSharpOptions = new MirrorSharpOptions()
            {
                SelfDebugEnabled = true,
                IncludeExceptionDetails = true,
                //SetOptionsFromClient = SetOptionsFromClientExtension()
                // CSharp = {
                //             MetadataReferences = ImmutableList.Create<MetadataReference>(all),
                //             CompilationOptions = compilationOptions
                //          },
                ExceptionLogger = new MirrorSharpExceptionLogger()
            }.SetupCSharp(cs =>
            {
                //cs.MetadataReferences = cs.MetadataReferences.Clear();
                //cs.AddMetadataReferencesFromFiles(all);
                cs.MetadataReferences = ImmutableList.Create<MetadataReference>(all);
                cs.CompilationOptions = compilationOptions;

            });


            app.UseMirrorSharp(mirrorSharpOptions);

            app.UseDefaultFiles();
            app.UseStaticFiles();

            // app.UseSignalR(routes =>
            // {
            //     routes.MapHub<Hubs.HomeDashboardHub>("/main-stats");
            //     routes.MapHub<Hubs.WorkerDashboardHub>("/worker-hub");
            //     routes.MapHub<Hubs.Performance.RealtimeHub>("/performance-realtime-hub");
            //     routes.MapHub<Hubs.HeartBeat.HeartBeatHub>("/heartbeat");
            //     routes.MapHub<Hubs.BackgroundTaskHub>("/bgtasks-hub");
            //     routes.MapHub<Hubs.BackgroundPluginHub>("/bgplugin-hub");
            //     routes.MapHub<Hubs.ExecHub>("/exec-hub");
            // });

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<Hubs.HomeDashboardHub>("/main-stats");
                endpoints.MapHub<Hubs.WorkerDashboardHub>("/worker-hub");
                endpoints.MapHub<Hubs.Performance.RealtimeHub>("/performance-realtime-hub");
                endpoints.MapHub<Hubs.HeartBeat.HeartBeatHub>("/heartbeat");
                endpoints.MapHub<Hubs.BackgroundTaskHub>("/bgtasks-hub");
                endpoints.MapHub<Hubs.BackgroundPluginHub>("/bgplugin-hub");
                endpoints.MapHub<Hubs.ExecHub>("/exec-hub");
            });

            app.UseAuthentication();
            app.UseWebSockets();
            app.UseCookiePolicy();

            app.UseHsts();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }


            app.UseMvc(routes =>
                        {
                            routes.MapRoute(
                                name: "default",
                                template: "{controller=Home}/{action=Index}/{id?}");
                        });
        }
    }
}
