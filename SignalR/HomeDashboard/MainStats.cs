using System;
using System.Threading;

namespace jsdal_server_core.Hubs
{
    public class MainStats
    {
        public DateTime StatsCreateDate { get; private set; }
        private MainStatsPerformance performance;
        private static int _numOfActiveSubWatchers = 0;

        private int _numbOfWorkerThreadsAvailable = 0;
        private int _numbOfAsyncIOThreadsAvailable = 0;

        public MainStats()
        {
            this.StatsCreateDate = DateTime.Now;
            this.performance = new MainStatsPerformance();

            ThreadPool.GetAvailableThreads(out _numbOfWorkerThreadsAvailable, out _numbOfAsyncIOThreadsAvailable);

            this.ProcessThreadCount = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
            this.HandleCount = System.Diagnostics.Process.GetCurrentProcess().HandleCount;


            this.GCDetail = new
            {
                Total = GC.GetTotalMemory(false),
                MemInfo = GC.GetGCMemoryInfo(),
                Settings = new
                {
                    System.Runtime.GCSettings.IsServerGC,
                    LargeObjectHeapCompactionMode = System.Runtime.GCSettings.LargeObjectHeapCompactionMode.ToString(),
                    LatencyMode = System.Runtime.GCSettings.LatencyMode.ToString()
                }
            };


        }
        public DateTime? WebServerStartDate { get { return Program.StartDate; } }

        public object GCDetail
        {
            get;
            private set;
        }

        public int TickCount
        {
            get { return Environment.TickCount; }
        }

        public int ProcessorCount
        {
            get { return Environment.ProcessorCount; }
        }

        public int HandleCount
        {
            get; private set;
        }

        public int ProcessThreadCount
        {
            get; private set;
        }

        public int ActiveSubWatchersCount
        {
            get { return _numOfActiveSubWatchers; }
        }

        public int WorkerThreadsAvailable
        {
            get { return _numbOfWorkerThreadsAvailable; }
        }

        public int AsyncIOThreadsAvailable
        {
            get { return _numbOfAsyncIOThreadsAvailable; }
        }

        public MainStatsPerformance Performance
        {
            get { return performance; }
        }

        public static void IncreaseSubWatchers()
        {
            Interlocked.Increment(ref _numOfActiveSubWatchers);
        }

        public static void DecreaseSubWatchers()
        {
            Interlocked.Decrement(ref _numOfActiveSubWatchers);
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