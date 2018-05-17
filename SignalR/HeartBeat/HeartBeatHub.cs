using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using System.Reactive.Linq;

namespace jsdal_server_core.Hubs.HeartBeat
{
    public class HeartBeatHub : Hub
    {
        public HeartBeatHub()
        {
            // System.Timers.Timer t = new System.Timers.Timer(10000);
            // t.Elapsed += (s, e) => { HeartBeatMonitor.Instance.NotifyObservers(); };
            // t.Start();
        }

        public int Init()
        {
            return Environment.TickCount;
        }

        // public IObservable<int> StreamTickOld()
        // {
        //     return HeartBeatMonitor.Instance;
        // }

        public ChannelReader<int> StreamTick()
        {
            var channel = Channel.CreateUnbounded<int>();

            // TODO: move this loop to a central thread? So that the 'tick' is shared by all who subscribe to it?
            Task.Run(async () => 
            {
                while (true)
                {
                    await channel.Writer.WriteAsync(Environment.TickCount);
                    await Task.Delay(10000);
                }

                //channel.Writer.TryComplete();
            });

            return channel.Reader;
        }

        public override async Task OnConnectedAsync()
        {
            //            await Groups.AddToGroupAsync(Context.ConnectionId, "SignalR Users");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            //await Groups.RemoveFromGroupAsync(Context.ConnectionId, "SignalR Users");
            await base.OnDisconnectedAsync(exception);
        }
    }

}