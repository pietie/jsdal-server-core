using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace jsdal_server_core.Hubs
{
    public class WorkerMonitor 
    {
        private static WorkerMonitor _singleton;

        private Channel<List<WorkerInfo>> workerInfoChannel; // TODO: Instead of a List, can we reduce this to single worker info updates -- initially get a list and then just update, or add all new ones to a list

        public Channel<List<WorkerInfo>> WorkerInfoChannel { get { return this.workerInfoChannel; }}

        public static WorkerMonitor Instance
        {
            get
            {
                if (_singleton == null) _singleton = new WorkerMonitor();

                return _singleton;
            }
        }

        private WorkerMonitor()
        {
            workerInfoChannel = Channel.CreateUnbounded<List<WorkerInfo>>();
        }

        public void NotifyObservers()
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

            workerInfoChannel.Writer.WriteAsync(packet);
        }
    }


}