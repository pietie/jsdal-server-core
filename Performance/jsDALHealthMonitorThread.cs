using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LiteDB;
using Serilog;

namespace jsdal_server_core.Performance
{
    public class jsDALHealthMonitorThread
    {
        public bool IsRunning { get; private set; }
        public Thread _winThread;

        private LiteDatabase _database;

        private DateTime? _nextCheck = null;

        private jsDALHealthMonitorThread() { }

        static jsDALHealthMonitorThread()
        {
            Instance = new jsDALHealthMonitorThread();
        }

        public static jsDALHealthMonitorThread Instance { get; private set; }

        public void Init()
        {
            if (IsRunning) return;
            _database = new LiteDB.LiteDatabase("data/health.db");
            _winThread = new Thread(new ThreadStart(Run));
            _winThread.Start();
        }

        public void Run()
        {
            try
            {
                IsRunning = true;
                System.Threading.Thread.CurrentThread.Name = "jsDALHealthMonitorThread thread";

                var sw = new Stopwatch();

                var dbCollection = _database.GetCollection<jsDALHealthDbEntry>($"HealthData");

                dbCollection.EnsureIndex("Created", unique: false);

                while (IsRunning && !Program.IsShuttingDown)
                {
                    if (!_nextCheck.HasValue || DateTime.Now >= _nextCheck.Value)
                    {
                        // var endpoints = Settings.SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications).SelectMany(a => a.Endpoints);

                        // sw.Restart();

                        // var q = from ep in endpoints
                        //         select new EndpointStats()
                        //         {
                        //             Endpoint = ep.Pedigree,
                        //             CachedRoutinesCount = ep.CachedRoutines.Count,
                        //             CachedRoutinesSizeInBytes = CalculateEstSizeInBytes(ep.CachedRoutines)
                        //         };

                        // var endpointStats = q.ToList();

                        // sw.Stop();

                        var proc = Process.GetCurrentProcess();

                        var blobStats = BlobStore.Instance.GetStats();

                        var memInfo = GC.GetGCMemoryInfo();

                        var newEntry = new jsDALHealthDbEntry()
                        {
                            Created = DateTime.Now,
                            // TimeToCalculateSizesInMS = sw.ElapsedMilliseconds,
                            // WorkingSet64 = proc.WorkingSet64,
                            //EndpointStats = endpointStats,
                            HeapSizeMB = (double)memInfo.HeapSizeBytes / 1024.0 / 1024.0,
                            BlobCnt = blobStats.TotalItemsInCache,
                            BlobsBytesInCache = blobStats.TotalBytesInCache,
                            PrivateMemorySize64 = proc.PrivateMemorySize64,
                            ExceutionsInFlight = Controllers.ExecController.ExceutionsInFlight

                        };

                        dbCollection.Insert(newEntry);

                        // delete entries older than 5 days
                        dbCollection.DeleteMany(x => x.Created.Value <= DateTime.Now.AddDays(-5));

                        _database.Checkpoint();
                        _nextCheck = DateTime.Now.AddSeconds(45);
                    }

                    Thread.Sleep(60);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "jsDALHealthMonitorThread failed");
                ExceptionLogger.LogException(ex);
                SessionLog.Error("jsDALHealthMonitorThread failed");
                SessionLog.Exception(ex);
            }
            finally
            {
                IsRunning = false;

                if (_database != null)
                {
                    _database.Checkpoint();
                    _database.Dispose();
                    _database = null;
                }
            }
        }

        public jsDALHealthDbEntry GetLatest()
        {
            var dbCollection = _database?.GetCollection<jsDALHealthDbEntry>($"HealthData");

            return dbCollection?.FindOne(Query.All(Query.Descending));
        }

        private IEnumerable<jsDALHealthDbEntry> BuildBaseQuery(DateTime fromDate, DateTime toDate)
        {
            if (_database == null) return null;

            var collection = _database.GetCollection<jsDALHealthDbEntry>($"HealthData");

            var baseQuery = collection
                    .Query()
                    .Where(x => x.Created >= fromDate
                              && x.Created <= toDate)
                    .ToEnumerable()
                    ;
            return baseQuery;
        }

        public dynamic GetReport(DateTime fromDate, DateTime toDate)
        {
            var baseQuery = BuildBaseQuery(fromDate, toDate);

            int groupByMin = 10;

            int? specificHour = null;
            int? specificMinute = null;
            var rangeInMins = (int)toDate.Subtract(fromDate).TotalMinutes;


            /*
                        groupByMin = rangeInMins switch
                        {
                            var n when n >= 120 => 60,
                            var n when n > 60 => 15,
                            _ => 10
                        };
            */
            switch (rangeInMins)
            {
                case var n when n >= 60 * 24:
                    specificHour = specificMinute = 0;
                    break;
                case var n when n > 180:
                    groupByMin = 60;
                    break;
                case var n when n > 60:
                    groupByMin = 15;
                    break;
                default:
                    groupByMin = 10;
                    break;
            }

            var totals = from x in baseQuery
                         group x by new
                         {
                             Created = new DateTime(x.Created.Value.Year, x.Created.Value.Month, x.Created.Value.Day,
                                                                            specificHour ?? x.Created.Value.Hour,
                                                                            specificMinute ?? (int)((double)x.Created.Value.Minute / (double)groupByMin) * groupByMin,
                                                                            0)
                         } into grp1
                         select new
                         {
                             grp1.Key.Created,
                             BlobsInCacheCnt = grp1.Max(c => c.BlobCnt),
                             BlobsInCacheSizeInMB = Math.Round(grp1.Max(c => (double)c.BlobsBytesInCache) / 1024.0 / 1024.0, 2),
                             PrivateMemorySize64 = grp1.Average(c => c.PrivateMemorySize64),
                             //WorkingSet64 = grp1.Average(c => c.WorkingSet64),
                             //TimeToCalculateSizesInMS = (int)grp1.Average(c => c.TimeToCalculateSizesInMS),
                            //CachedRoutinesSizeInMB = Math.Round(grp1.Average(c => (double)c.EndpointStats.Sum(e => e.CachedRoutinesSizeInBytes)) / 1024.0 / 1024.0, 2),
                             HeapSizeInMB = Math.Round(grp1.Average(c => c.HeapSizeMB), 2),
                             ExecutionsInFlight = (int)grp1.Sum(c => c.ExceutionsInFlight)

                         };

            var labels = totals.Select(x => x.Created.ToString("dd MMM yyyy HH:mm")).ToArray();

            var blobsInCacheCntDataset = new
            {
                label = "Blob count",
                data = (from e in totals select e.BlobsInCacheCnt),
            };

            var blobsMBInCacheCntDataset = new
            {
                label = "Blob cache (MB)",
                data = (from e in totals select e.BlobsInCacheSizeInMB),
            };

             var heapSizeDataset = new
            {
                label = "Heap Size (MB)",
                data = (from e in totals
                        select Math.Round((double)e.HeapSizeInMB, 2))
            };

            // var workingSetDataset = new
            // {
            //     label = "Working set (MB)",
            //     data = (from e in totals
            //             select Math.Round((double)e.WorkingSet64 / 1024.0 / 1024.0, 2))
            // };

            var privateDataset = new
            {
                label = "Private memory (MB)",
                data = (from e in totals
                        select Math.Round((double)e.PrivateMemorySize64 / 1024.0 / 1024.0, 2)),
            };

            // var timeToCalcSizesDataset = new
            // {
            //     label = "Time to calc sizes (ms)",
            //     data = (from e in totals select e.TimeToCalculateSizesInMS),
            // };

            // var cachedRoutineSizeDataset = new
            // {
            //     label = "Cached routine size (MB)",
            //     data = (from e in totals select e.CachedRoutinesSizeInMB),
            // };

            var execInFlightDataset = new
            {
                label = "Executions in flight",
                data = (from e in totals select e.ExecutionsInFlight),
            };

            dynamic ret = new
            {
                labels = labels,
                datasets = new object[] { blobsInCacheCntDataset, blobsMBInCacheCntDataset, heapSizeDataset, privateDataset, execInFlightDataset }
            };

            return ret;

        }

        private long CalculateEstSizeInBytes(object o)
        {
            using (var ms = new MemoryStream())
            {
                var bf = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();

                bf.Serialize(ms, o);

                return ms.Length;
            }
        }

        public void Shutdown()
        {
            IsRunning = false;
            if (_winThread != null)
            {
                if (!_winThread.Join(TimeSpan.FromSeconds(15)))
                {
                    Log.Error("jsDALHealthMonitorThread failed to shutdown in time");
                }
                _winThread = null;
            }

            if (_database != null)
            {
                _database.Checkpoint();
                _database.Dispose();
                _database = null;
            }
        }

    }

    public class jsDALHealthDbEntry
    {
        public int Id { get; set; } // auto set by LiteDB

        public DateTime? Created { get; set; }

        //public long TimeToCalculateSizesInMS { get; set; }
        //public long WorkingSet64 { get; set; }

        public double HeapSizeMB { get; set; }

        public long BlobsBytesInCache { get; set; }
        public int BlobCnt { get; set; }

        //public List<EndpointStats> EndpointStats { get; set; }

        public long PrivateMemorySize64 { get; set; }

        public int ExceutionsInFlight { get; set; }
    }

    // public class EndpointStats
    // {
    //     public string Endpoint { get; set; }
    //     public int CachedRoutinesCount { get; set; }
    //     public long CachedRoutinesSizeInBytes { get; set; }
    // }
}