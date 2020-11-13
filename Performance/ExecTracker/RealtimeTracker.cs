using System;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Hubs;
using jsdal_server_core.Util;

namespace jsdal_server_core.Performance
{

    public class RealtimeTrackerThread : QueueThread<RoutineExecution>
    {
        private List<RealtimeInfo> _realtimeItemList = new List<RealtimeInfo>();

        private DateTime? _lastPurge = null;

        private RealtimeTrackerThread() : base(flushTimeoutInSeconds: 1, flushCountThreshold: 10, threadName: "RealtimeTrackerThread")
        {
        }

        public static RealtimeTrackerThread Instance { get; private set; }

        static RealtimeTrackerThread()
        {
            Instance = new RealtimeTrackerThread();
        }

        protected override void ProcessQueueEntries(List<RoutineExecution> entryCollection)
        {
            lock (_realtimeItemList)
            {
                foreach (var entry in entryCollection)
                {
                    var match = _realtimeItemList.FirstOrDefault(existing => existing.RoutineExecution.ShortId.Equals(entry.ShortId, StringComparison.Ordinal));

                    if (match != null)
                    {
                        match.RoutineExecution = entry;
                    }
                    else
                    {
                        _realtimeItemList.Add(new RealtimeInfo(entry));
                    }
                }
            }

            Hubs.Performance.RealtimeMonitor.Instance.NotifyObservers();
        }

        protected override void DoWork()
        {
            if (!_lastPurge.HasValue || DateTime.Now.Subtract(_lastPurge.Value).TotalSeconds > 2)
            {
                _lastPurge = DateTime.Now;

                try
                {
                    Purge();
                }
                catch (System.Exception ex)
                {
                    ExceptionLogger.LogExceptionThrottled(ex, "RealtimeTrackerThread", 2);
                }
            }
        }

        public List<RealtimeInfo> GetOrderedList()
        {
            lock (_realtimeItemList)
            {
                return _realtimeItemList.OrderByDescending(r => r.createdEpoch).ToList();
            }
        }

        public void Purge()
        {
            lock (_realtimeItemList)
            {
                var epochCutOff = DateTime.Now.AddSeconds(-10).ToEpochMS();
                int removeCnt = 0;

                for (var i = 0; i < _realtimeItemList.Count; i++)
                {
                    var item = _realtimeItemList[i];

                    if (item.createdEpoch <= epochCutOff)
                    {
                        _realtimeItemList.RemoveAt(i);
                        removeCnt++;
                        i--;
                    }
                    else
                    {
                        // the keys are sorted so we are guaranteed the next (later) key will also not be within the cutoff
                        break;
                    }

                }

                if (removeCnt > 0)
                {
                    Hubs.Performance.RealtimeMonitor.Instance.NotifyObservers();
                }
            }
        }

    }

    public class RealtimeTrackerOld
    {
        private static List<RealtimeInfo> _realtimeItemList = new List<RealtimeInfo>();

        static RealtimeTrackerOld()
        {
            var t = new System.Timers.Timer(2000);

            t.Elapsed += (s, e) => { Purge(); };
            t.Start();
        }

        public static List<RealtimeInfo> GetOrderedList()
        {
            lock (_realtimeItemList)
            {
                return _realtimeItemList.OrderByDescending(r => r.createdEpoch).ToList();
            }
        }

        public static void Add(RoutineExecution e)
        {
            try
            {
                var ri = new RealtimeInfo(e);

                lock (_realtimeItemList)
                {
                    _realtimeItemList.Add(ri);

                    Hubs.Performance.RealtimeMonitor.Instance.NotifyObservers();
                }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public static void Purge()
        {
            try
            {
                lock (_realtimeItemList)
                {
                    // TODO: Make retention configurable
                    var epochCutOff = DateTime.Now.AddSeconds(-10).ToEpochMS();
                    int remoteCnt = 0;

                    for (var i = 0; i < _realtimeItemList.Count; i++)
                    {
                        var item = _realtimeItemList[i];

                        if (item.createdEpoch <= epochCutOff)
                        {
                            _realtimeItemList.RemoveAt(i);
                            remoteCnt++;
                            i--;
                        }
                        else
                        {
                            // the keys are sorted so we are guaranteed the next (later) key will also not be within the cutoff
                            break;
                        }

                    }

                    if (remoteCnt > 0)
                    {
                        Hubs.Performance.RealtimeMonitor.Instance.NotifyObservers();
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}