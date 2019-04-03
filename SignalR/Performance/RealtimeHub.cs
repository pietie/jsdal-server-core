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
        //private readonly static ConnectionMapping<string> _connections = new ConnectionMapping<string>();

        public RealtimeHub()
        {

        }

        public override Task OnConnectedAsync()
        {
            // var key = Context.ConnectionId; // TODO: Change to something like the logged in userId

            // _connections.Add(key, Context.ConnectionId);

            // Groups.AddToGroupAsync(Context.ConnectionId, "RealtimeHub.Main");

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            // var key = Context.ConnectionId; // TODO: Change to something like the logged in userId
            // _connections.Remove(key, Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        public async Task<List<RealtimeInfo>> Init()
        {
            await this.Groups.AddToGroupAsync(this.Context.ConnectionId, "");

            return RealtimeTracker.RealtimeItems;
        }

        public ChannelReader<List<RealtimeInfo>> StreamRealtimeList()
        {
            return RealtimeMonitor.Instance.RealtimeInfoChannel.Reader;
        }



        public int GetInitProgress()
        {
            return 10;
        }

    }

}