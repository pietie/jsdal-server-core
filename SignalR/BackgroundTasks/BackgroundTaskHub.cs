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
        //private MainStatsMonitor mainStatsObs;
        public BackgroundTaskHub()
        {
        }

        public ChannelReader<BackgroundWorker> Stream()
        {
            return BackgroundTaskMonitor.Instance.WorkerInfoChannel.Reader;
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
        private static BackgroundTaskMonitor _singleton;

        private Channel<BackgroundWorker> workerInfoChannel; 

        public Channel<BackgroundWorker> WorkerInfoChannel { get { return this.workerInfoChannel; }}

        public static BackgroundTaskMonitor Instance
        {
            get
            {
                if (_singleton == null) _singleton = new BackgroundTaskMonitor();

                return _singleton;
            }
        }

        private BackgroundTaskMonitor()
        {
            workerInfoChannel = Channel.CreateUnbounded<BackgroundWorker>();
        }

        public void NotifyOfChange(BackgroundWorker bw)
        {
           // var packet = WorkSpawner.workerList.Select(wl =>
            //     {
            //         return new WorkerInfo()
            //         {
            //             id = wl.ID,
            //             name = wl.Endpoint.Pedigree,
            //             desc = wl.Description,
            //             status = wl.Status,
            //             /*lastProgress = wl.lastProgress,
            //             lastProgressMoment = wl.lastProgressMoment,
            //             lastConnectMoment = wl.lastConnectedMoment,*/
            //             isRunning = wl.IsRunning
            //         };
            //     }).ToList();

            workerInfoChannel.Writer.WriteAsync(bw);
        }
    }

  
}