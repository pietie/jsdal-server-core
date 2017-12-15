using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs
{
    public class WorkerDashboardHub : Hub
    {
        //private WorkerMonitor workerMonitor;
        public WorkerDashboardHub()
        {
            
        }

        public IObservable<List<WorkerInfo>> StreamWorkerDetail()
        {
            return WorkerMonitor.Instance;
        }
    }

}