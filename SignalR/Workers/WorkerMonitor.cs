using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs
{
    public class WorkerMonitor
    {
        public static WorkerMonitor Instance
        {
            get; set;
        }

        private readonly IHubContext<WorkerDashboardHub> _hubContext;

        public WorkerMonitor(IHubContext<WorkerDashboardHub> ctx)
        {
            _hubContext = ctx;
        }

        public void NotifyObservers() // TODO: Nice to have will be to notify only about the specific Worker that changed
        {
            var packet = WorkSpawner.workerList.Select(wl =>
                {
                    return new WorkerInfo()
                    {
                        id = wl.ID,
                        name = wl.Endpoint.Pedigree,
                        desc = wl.Description,
                        status = wl.Status,
                        /*lastProgress = wl.lastProgress,
                        lastProgressMoment = wl.lastProgressMoment,
                        lastConnectMoment = wl.lastConnectedMoment,*/
                        isRunning = wl.IsRunning
                    };
                }).ToList();

            _hubContext.Clients.Group(WorkerDashboardHub.GROUP_NAME).SendAsync("updateWorkerList", packet);
        }
    }


}