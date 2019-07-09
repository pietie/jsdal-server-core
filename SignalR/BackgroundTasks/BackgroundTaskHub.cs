using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace jsdal_server_core.Hubs
{
    public class BackgroundTaskHub : Hub
    {
        public static readonly string GROUP_NAME = "BackgroundTaskHub.Changes";

        public void Init()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME);
        }


    }

    public class BgTaskInfo
    {
        public string Name;
        public bool IsDone;
        public Guid Guid;
    }

    public class BackgroundTaskMonitor
    {
        public static BackgroundTaskMonitor Instance
        {
            get; set;
        }

        private readonly IHubContext<BackgroundTaskHub> _hubContext;

        public BackgroundTaskMonitor(IHubContext<BackgroundTaskHub> ctx)
        {
            this._hubContext = ctx;
        }

        public void NotifyOfChange(BackgroundWorker bw)
        {
            _hubContext.Clients.Group(BackgroundTaskHub.GROUP_NAME).SendAsync("update", bw);
        }
    }


}