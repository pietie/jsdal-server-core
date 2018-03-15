﻿using System;

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
            var policy = new Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicy();
            policy.Headers.Add("*");
            policy.Methods.Add("*");
            policy.Origins.Add("*");
            policy.SupportsCredentials = true;

            services.AddCors(x => x.AddPolicy("CorsPolicy", policy));

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

            services.AddSignalR(s => s.JsonSerializerSettings.ContractResolver = new DefaultContractResolver());
            services.AddMvc().AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver());

            var dataProtectionKeyPath = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
            services.AddDataProtection()
                    .PersistKeysToFileSystem(dataProtectionKeyPath)
                    .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
                    {
                        EncryptionAlgorithm = EncryptionAlgorithm.AES_256_GCM,
                        ValidationAlgorithm = ValidationAlgorithm.HMACSHA512
                    });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseCors("CorsPolicy");
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseAuthentication();

            app.UseWebSockets();
            app.UseSignalR(routes =>
            {
                routes.MapHub<Hubs.HomeDashboardHub>("main-stats");
                routes.MapHub<Hubs.WorkerDashboardHub>("worker-hub");
                routes.MapHub<Hubs.Performance.RealtimeHub>("performance-realtime-hub");
                routes.MapHub<Hubs.HeartBeat.HeartBeatHub>("heartbeat");
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseMvc();
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }


}
