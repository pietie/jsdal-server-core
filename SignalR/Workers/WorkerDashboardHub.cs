using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs
{
    public class WorkerDashboardHub : Hub
    {
        //private WorkerMonitor workerMonitor;
        public WorkerDashboardHub()
        {

        }

        public List<WorkerInfo> Init()
        {
            return WorkSpawner.workerList.Select(wl =>
                {
                    return new WorkerInfo()
                    {
                        id = wl.ID,
                        name = wl.DBSource.Name,
                        desc = wl.Description,
                        status = wl.Status,
                        /*lastProgress = wl.lastProgress,
                        lastProgressMoment = wl.lastProgressMoment,
                        lastConnectMoment = wl.lastConnectedMoment,*/
                        isRunning = wl.IsRunning
                    };
                }).ToList();
        }

        public IObservable<List<WorkerInfo>> StreamWorkerDetail()
        {
            return WorkerMonitor.Instance;
        }
    }

}