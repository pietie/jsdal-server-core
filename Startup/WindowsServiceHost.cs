using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.WindowsServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace jsdal_server_core
{
    internal class CustomWebHostService : WebHostService
    {
        private ILogger _logger;

        public CustomWebHostService(IWebHost host) : base(host)
        {
            _logger = host.Services.GetRequiredService<ILogger<CustomWebHostService>>();
        }

        protected override void OnStarting(string[] args)
        {
            _logger.LogInformation("OnStarting method called.");
            base.OnStarting(args);
        }

        protected override void OnStarted()
        {
            _logger.LogInformation("OnStarted method called.");
            base.OnStarted();
        }

        protected override void OnStopping()
        {
            _logger.LogInformation("OnStopping method called.");

            WorkSpawner.Shutdown();
            SignalR.HomeDashboard.DotNetCoreCounterListener.Instance?.Stop();
            Performance.StatsDB.Shutdown();
            Hubs.CommonNotificationThread.Instance?.Shutdown();
            PluginManagement.BackgroundThreadPluginManager.Instance?.Shutdown();

            base.OnStopping();
        }

    }
}