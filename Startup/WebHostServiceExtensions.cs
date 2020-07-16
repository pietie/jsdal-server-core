using Microsoft.AspNetCore.Hosting;
using Serilog;
using System;
using System.ServiceProcess;

namespace jsdal_server_core
{
    public static class WebHostServiceExtensions
    {
        public static void RunAsCustomService(this IWebHost host)
        {
            try
            {
                var webHostService = new CustomWebHostService(host);
                ServiceBase.Run(webHostService);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed in RunAsCustomService");
            }
        }
    }
}