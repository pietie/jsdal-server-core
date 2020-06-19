using System;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Hubs;

namespace jsdal_server_core.Performance
{

    public class RealtimeTracker
    {
        //private static SortedList<long, RealtimeInfo> List = new SortedList<long, RealtimeInfo>();
        private static List<RealtimeInfo> _realtimeItemList = new List<RealtimeInfo>();

        // public static List<RealtimeInfo> RealtimeItems
        // {
        //     get
        //     {
        //         return _realtimeItemList;
        //     }
        // }


        static RealtimeTracker()
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