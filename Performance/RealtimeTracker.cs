using System;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Hubs;

namespace jsdal_server_core.Performance
{

    public class RealtimeTracker
    {
        private static List<RealtimeInfo> List = new List<RealtimeInfo>();

        public static List<RealtimeInfo> RealtimeItems
        {
            get { return List; }
        }
        static RealtimeTracker()
        {
            var t = new System.Timers.Timer(2000);

            t.Elapsed += (s, e) => { Purge(); };
            t.Start();

        }

        public static void Add(RoutineExecution e)
        {
            try
            {
                var ri = new RealtimeInfo(e);

                lock (List)
                {
                    List.Add(ri);

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
                lock (List)
                {
                    var toRemove = new List<RealtimeInfo>();

                    List.ForEach(ri =>
                    {
                        var end = ri.RoutineExectionEndedUtc();

                        if (!end.HasValue) return;

                        if (DateTime.UtcNow.Subtract(end.Value).TotalSeconds > 10)// TODO: Make retention configurable
                        {
                            toRemove.Add(ri);
                        }
                    });

                    if (toRemove.Count > 0)
                    {
                        List.RemoveAll(a => toRemove.Contains(a));
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