using System;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;

using MirrorSharp.Owin;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;


using System.Reflection;

using Microsoft.CodeAnalysis.CSharp;
using MirrorSharp.Advanced;
using System.IO;

namespace jsdal_server_core
{
    public class Startup
    {
        public IConfiguration Configuration { get; private set; }
        public IHostingEnvironment HostingEnvironment { get; private set; }

        public Startup(IConfiguration configuration, IHostingEnvironment env)
        {
            Configuration = configuration;
            HostingEnvironment = env;

            Console.WriteLine($"WebRootPath: {env.WebRootPath}; ");
        }



        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddCors(options => options.AddPolicy("CorsPolicy",
                    builder => builder
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        .AllowAnyOrigin()
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

            services.AddSignalR()
                .AddJsonProtocol(options =>
                {
                    options.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver();
                });

            services.AddMvc()
                    .AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver())
                    .SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
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

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
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
                ReceiveBufferSize = 4 * 1024
            };
            app.UseWebSockets(webSocketOptions);

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

            var mirrorSharpOptions = new MirrorSharp.MirrorSharpOptions()
            {
                SelfDebugEnabled = true,
                IncludeExceptionDetails = true,
                CSharp = {
                            MetadataReferences = ImmutableList.Create<MetadataReference>(all),
                            CompilationOptions = compilationOptions
                         }
            };
 

            app.UseMirrorSharp(mirrorSharpOptions);

            app.UseCors("CorsPolicy");

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseSignalR(routes =>
            {
                routes.MapHub<Hubs.HomeDashboardHub>("/main-stats");
                routes.MapHub<Hubs.WorkerDashboardHub>("/worker-hub");
                routes.MapHub<Hubs.Performance.RealtimeHub>("/performance-realtime-hub");
                routes.MapHub<Hubs.HeartBeat.HeartBeatHub>("/heartbeat");
            });

            app.UseAuthentication();
            app.UseWebSockets();
            app.UseCookiePolicy();


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
