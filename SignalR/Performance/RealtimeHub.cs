using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using jsdal_server_core.Performance;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs.Performance
{
    public class RealtimeHub : Hub
    {
        public static readonly string GROUP_NAME = "RealtimeHub.Changes";

        public override Task OnConnectedAsync()
        {
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            return base.OnDisconnectedAsync(exception);
        }

        public List<RealtimeInfo> Init()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME);

            return RealtimeTracker.GetOrderedList();
        }

        public int GetInitProgress()
        {
            return 10;
        }

    }

}