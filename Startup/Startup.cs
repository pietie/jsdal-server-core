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
using Newtonsoft.Json;
using jsdal_server_core.Hubs;
using jsdal_server_core.Hubs.Performance;
using jsdal_server_core.PluginManagement;
using jsdal_server_core.Settings.ObjectModel;
using jsdal_server_core.SignalR.HomeDashboard;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Serilog;
using MirrorSharp.AspNetCore;
using MirrorSharp;

namespace jsdal_server_core
{
    public class Startup
    {
        public IConfiguration Configuration { get; private set; }
        public IWebHostEnvironment HostingEnvironment { get; private set; }

        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;

            HostingEnvironment = env;

            Log.Information($"WebRootPath: {env.WebRootPath}");
            Log.Information($"BarcodeService.URL: {Configuration["AppSettings:BarcodeService.URL"]?.TrimEnd('/')}" ?? "(Not set!)");
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // TODO: make configurable
            // services.AddApplicationInsightsTelemetry(options =>
            // {



            // });

            services.AddSingleton<PluginLoader>();
            services.AddSingleton(typeof(BackgroundThreadPluginManager));
            services.AddSingleton(typeof(CommonNotificationThread));
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

                    Log.Information("Issuer: {0}, Key: {1}", issuer, key);

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

            Log.Information($"dataProtectionKeyPath: {dataProtectionKeyPath}");

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

                CommonNotificationThread.Instance = app.ApplicationServices.GetService<CommonNotificationThread>();

                BackgroundThreadPluginManager.Instance = app.ApplicationServices.GetService<BackgroundThreadPluginManager>();
                WorkerMonitor.Instance = app.ApplicationServices.GetService<WorkerMonitor>();
                RealtimeMonitor.Instance = app.ApplicationServices.GetService<RealtimeMonitor>();
                BackgroundTaskMonitor.Instance = app.ApplicationServices.GetService<BackgroundTaskMonitor>();

                ConnectionStringSecurity.Instance = app.ApplicationServices.GetService<ConnectionStringSecurity>();

                DotNetCoreCounterListener.Instance = app.ApplicationServices.GetService<DotNetCoreCounterListener>();
                DotNetCoreCounterListener.Instance.Start();

                {// More app startup stuff...but have a dependency on the singleton objects above. Can we move this somewhere else?

                    Log.Information("Initialising project object model");
                    // we can only initialise the Project structure once ConnectionStringSecurity exists
                    Settings.SettingsInstance.Instance.ProjectList.ForEach(p => p.AfterDeserializationInit());

                    Log.Information("Initialising plugin loader");
                    PluginLoader.Instance = pmInst;
                    PluginLoader.Instance.Init();

                    ServerMethodManager.RebuildCacheForAllApps();

                    Log.Information("Starting work spawner.");
                    WorkSpawner.Start();
                }
            }

            applicationLifetime.ApplicationStopped.Register(() =>
            {
                Log.Information("Application stopped");
            });

            applicationLifetime.ApplicationStopping.Register(() =>
            {
                Log.Information("Application is shutting down");
            });


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


            // SPA (angular) route fallback
            app.Use(async (context, next) =>
            {
                await next();

                if (context.Response.StatusCode == 404 && !Path.HasExtension(context.Request.Path.Value))
                {
                    context.Request.Path = "/index.html";
                    await next();
                }
            });

            var assemblyBasePath = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location);

            var all = CSharpCompilerHelper.GetCommonMetadataReferences();

            var jsDALBasePluginPath = Path.GetFullPath("./plugins/jsdal-plugin.dll");

            if (File.Exists("./plugins/jsdal-plugin.dll"))
            {
                Array.Resize(ref all, all.Length + 1);

                all[all.Length - 1] = Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(jsDALBasePluginPath);
            }
            else
            {
                Log.Error($"Failed to find base plugin assembly at {jsDALBasePluginPath}");
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

            /*****            
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
            */
            app.UseDefaultFiles();
            app.UseStaticFiles();

            // TODO: This outputs full request detail into log. Perhaps consider outputting this to a different detailed log
            //app.UseSerilogRequestLogging();
            app.UseSerilogRequestLogging(options =>
            {
                options.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                // Customize the message template
                //HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms
                options.MessageTemplate = "Req/Res: {ReqLen,7} {ResLen,7} {StatusCode} {Elapsed,7:0} ms {RequestMethod,4} {RequestPath}";

                //options.GetLevel = (httpContext, elapsed, ex) => Serilog.Events.LogEventLevel.Warning;


                // Attach additional properties to the request completion event
                options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                {
                    diagnosticContext.Set("ResLen", httpContext.Response.ContentLength ?? 0);
                    diagnosticContext.Set("ReqLen", httpContext.Request.ContentLength ?? 0);

                };
            });

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

                var mirrorSharpOptions = new MirrorSharpOptions()
                {
                    SelfDebugEnabled = true,
                    IncludeExceptionDetails = true
                    //SetOptionsFromClient = SetOptionsFromClientExtension()
                    // CSharp = {
                    //             MetadataReferences = ImmutableList.Create<MetadataReference>(all),
                    //             CompilationOptions = compilationOptions
                    //          },
                    //   ExceptionLogger = new MirrorSharpExceptionLogger()
                }.SetupCSharp(cs =>
                {
                    //cs.MetadataReferences = cs.MetadataReferences.Clear();
                    //cs.AddMetadataReferencesFromFiles(all);
                    cs.MetadataReferences = ImmutableList.Create<MetadataReference>(all);
                    cs.CompilationOptions = compilationOptions;

                });

                endpoints.MapMirrorSharp("/mirrorsharp", mirrorSharpOptions);
            });

            app.UseAuthentication();
            app.UseWebSockets();
            app.UseCookiePolicy();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();

                // unhandled exceptions
                app.UseExceptionHandler(errorApp =>
                {
                    errorApp.Run(async context =>
                    {
                        try
                        {
                            var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();

                            var id = ExceptionLogger.LogException(exceptionHandlerPathFeature.Error, exceptionHandlerPathFeature.Path, "jsdal-server", null);

                            context.Response.StatusCode = 500;
                            context.Response.ContentType = "text/plain";

                            await context.Response.WriteAsync($"Server error. Ref: {id}");
                            await context.Response.WriteAsync(new string(' ', 512)); // IE padding
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"Failed to log unhandled exception because of:\r\n {ex.ToString()}");
                        }
                    });
                });
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
