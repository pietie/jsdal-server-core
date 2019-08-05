using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;
using jsdal_server_core.SignalR.HomeDashboard;
using System.Collections.ObjectModel;

namespace jsdal_server_core.Hubs
{
    public class HomeDashboardHub : Hub
    {
        public static readonly string GROUP_NAME = "MainDashboard.Stats";
        public static readonly string GROUP_NAME_CLR_COUNTERS = "MainDashboard.ClrCounters";

        public HomeDashboardHub(DotNetCoreCounterListener dotNetCoreCounterListener)
        {
            //_dotnetCoreCounterListener ??= new DotNetCoreCounterListener(System.Diagnostics.Process.GetCurrentProcess().Id);
            //_dotnetCoreCounterListener.Start();
        }

        public MainStats Init()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME);
            return new MainStats();
        }

        public Dictionary<string, Dictionary<string, jsdal_server_core.Performance.dotnet.CounterEventArgs>> SubscribeToDotnetCorePerfCounters()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME_CLR_COUNTERS);

            return DotNetCoreCounterListener.Instance.CounterValues.ToDictionary((kv)=>kv.Key, kv=>kv.Value);
        }

        public void UnsubscribeFromDotnetCorePerfCounters()
        {
            this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, GROUP_NAME_CLR_COUNTERS);
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            //this.Clients.Group(GROUP_NAME_CLR_COUNTERS).
            //this.Clients.Group().
            return base.OnDisconnectedAsync(exception);
        }

    }

    public class MainStatsMonitorThread
    {
        private readonly IHubContext<HomeDashboardHub> _hubContext;

        public MainStatsMonitorThread(IHubContext<HomeDashboardHub> ctx)
        {
            this._hubContext = ctx;

            ThreadPool.QueueUserWorkItem((state) =>
            {
                while (true)
                {
                    this._hubContext.Clients.Group(HomeDashboardHub.GROUP_NAME).SendAsync("updateStats", new MainStats());

                    // TODO: Provide way to exit this thread?
                    Thread.Sleep(3500);
                }
            });
        }
    }

    public class MainStats
    {
        public DateTime StatsCreateDate { get; private set; }
        private MainStatsPerformance performance;
        public MainStats()
        {
            this.StatsCreateDate = DateTime.Now;
            this.performance = new MainStatsPerformance();
        }
        public DateTime? WebServerStartDate { get { return Program.StartDate; } }
        public int TickCount
        {
            get { return Environment.TickCount; }
        }

        public int ProcessorCount
        {
            get { return Environment.ProcessorCount; }
        }

        public MainStatsPerformance Performance
        {
            get { return performance; }
        }
    }

    public class MainStatsPerformance
    {
        System.Diagnostics.Process process;
        public MainStatsPerformance()
        {
            process = System.Diagnostics.Process.GetCurrentProcess();
        }
        public long WorkingSet { get { return process.WorkingSet64; } }
        public long PeakWorkingSet { get { return process.PeakWorkingSet64; } }
        public long PrivateMemorySize { get { return process.PrivateMemorySize64; } }
    }
}