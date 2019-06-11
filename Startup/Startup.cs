﻿using System;

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

namespace jsdal_server_core
{
    public class Startup
    {
        public IConfiguration Configuration { get; private set; }
        public IHostingEnvironment HostingEnvironment { get; private set; }

        public Startup(IConfiguration configuration, IHostingEnvironment env)
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

            services.Add(new ServiceDescriptor(typeof(JsonSerializer),
                                               provider => serializer,
                                               ServiceLifetime.Transient));

            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
            })
                .AddNewtonsoftJsonProtocol() // required on .NET CORE 3 preview for now as the System.Text JSON implementation does not deserialize Dictionaries correctly (or in the same way at least)
                .AddJsonProtocol(options =>
                {
                    options.UseCamelCase = false;
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

                        //new DefaultContractResolver();
                    })

                    .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            ;


            var dataProtectionKeyPath = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
            services.AddDataProtection()
                    .PersistKeysToFileSystem(dataProtectionKeyPath)
                    .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
                    {
                        EncryptionAlgorithm = EncryptionAlgorithm.AES_256_GCM,
                        ValidationAlgorithm = ValidationAlgorithm.HMACSHA512
                    });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IApplicationLifetime applicationLifetime)
        {
            //app.UseDeveloperExceptionPage();

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
                                        MetadataReference.CreateFromFile(typeof(System.Data.SqlClient.SqlConnection).Assembly.Location),
                                        Microsoft.CodeAnalysis.MetadataReference.CreateFromFile("./plugins/jsdal-plugin.dll")
            };


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

            app.UseSignalR(routes =>
            {
                routes.MapHub<Hubs.HomeDashboardHub>("/main-stats");
                routes.MapHub<Hubs.WorkerDashboardHub>("/worker-hub");
                routes.MapHub<Hubs.Performance.RealtimeHub>("/performance-realtime-hub");
                routes.MapHub<Hubs.HeartBeat.HeartBeatHub>("/heartbeat");
                routes.MapHub<Hubs.BackgroundTaskHub>("/bgtasks-hub");
                routes.MapHub<Hubs.ExecHub>("/exec-hub");
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
