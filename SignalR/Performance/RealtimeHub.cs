using System;
using System.Collections.Generic;
using System.Linq;
using jsdal_server_core.Performance;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs.Performance
{
    public class RealtimeHub : Hub
    {
        public RealtimeHub()
        {

        }

        public List<RealtimeInfo> Init()
        {
            return ExecTracker.ExecutionList.Select(e =>
                {
                    return new RealtimeInfo()
                    {
                        name = $"[{e.Schema}].[{e.Name}]",
                        createdEpoch = e.CreateDate.ToEpochMS(),
                        durationMS = e.DurationInMS,
                        rowsAffected = e.RowsAffected

                    };
                }).ToList();
        }

        public IObservable<List<RealtimeInfo>> StreamRealtimeList()
        {
            return RealtimeMonitor.Instance;
        }
    }

}