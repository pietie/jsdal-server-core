using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace jsdal_server_core.Hubs
{
    public class HomeDashboardHub : Hub
    {
        private MainStatsMonitor mainStatsObs;
        public HomeDashboardHub()
        {
        }

        public MainStats Init()
        {
            return new MainStats();
        }

        public ChannelReader<MainStats> StreamMainStats()
        {
            return MainStatsMonitor.Instance.MainStatsChannel.Reader;
        }
    }

    public class MainStatsMonitor
    {
        List<IObserver<MainStats>> observers;
        private Channel<MainStats> channel;

        public Channel<MainStats> MainStatsChannel { get { return this.channel; } }

        private MainStatsMonitor()
        {
            channel = Channel.CreateUnbounded<MainStats>();

            ThreadPool.QueueUserWorkItem((state) =>
            {
                while (true)
                {
                    this.channel.Writer.WriteAsync(new MainStats());

                    // TODO: Provide way to exit this thread?
                    Thread.Sleep(3500);
                }
            });
        }

        private static MainStatsMonitor _instance;
        public static MainStatsMonitor Instance { get { if (_instance == null) _instance = new MainStatsMonitor(); return _instance; } }

    }

    public class MainStats
    {
        private MainStatsPerformance performance;
        public MainStats()
        {
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