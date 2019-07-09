using System;
using Microsoft.AspNetCore.SignalR;
using System.Threading;

namespace jsdal_server_core.Hubs.HeartBeat
{
    public class HeartBeatHub : Hub
    {
        public static readonly string GROUP_NAME = "HeartBeatHub.Tick";
        private readonly IHubContext<HeartBeatHub> _hubContext;

        public HeartBeatHub(IHubContext<HeartBeatHub> ctx)
        {
            this._hubContext = ctx;
            ThreadPool.QueueUserWorkItem((state) =>
            {
                while (true)
                {
                    this._hubContext.Clients.Group(GROUP_NAME).SendAsync("tick", Environment.TickCount);

                    // TODO: Provide way to exit this thread?
                    Thread.Sleep(10000);
                }
            });
        }
        
        public int Init()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME);
            return Environment.TickCount;
        }
    }

}