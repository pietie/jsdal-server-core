using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs
{
    public class WorkerDashboardHub : Hub
    {
        public static readonly string GROUP_NAME = "WorkerDasboard.Changes";

        public List<WorkerInfo> Init()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME);

            return WorkSpawner.workerList.Select(wl =>
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
        }

    }

}