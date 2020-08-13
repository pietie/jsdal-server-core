using System;
using System.Linq;
using System.Threading;
using jsdal_plugin.Util;
using jsdal_server_core.Hubs.HeartBeat;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace jsdal_server_core.Hubs
{
    public class CommonNotificationThread
    {
        private readonly IHubContext<HomeDashboardHub> _homeHubContext;
        private readonly IHubContext<HeartBeatHub> _heartbeatHubContext;

        private Thread _winThread;

        public CommonNotificationThread(IHubContext<HomeDashboardHub> homeHubCtx, IHubContext<HeartBeatHub> heartBeatHubCtx)
        {
            this._homeHubContext = homeHubCtx;
            this._heartbeatHubContext = heartBeatHubCtx;

            _winThread = new Thread(new ThreadStart(this.Run));
            _winThread.Start();
        }

        public static CommonNotificationThread Instance
        {
            get;
            set;
        }

        public bool IsRunning { get; set; }

        public void Shutdown()
        {
            this.IsRunning = false;

            if (_winThread != null)
            {
                if (!_winThread.Join(TimeSpan.FromSeconds(5)))
                {
                    Log.Error("CommonNotificationThread failed to shutdown in time");
                }

                _winThread = null;
            }
        }

        public void Run()
        {
            Thread.CurrentThread.Name = "CommonNotificationThread";

            var checkMainStatsEvery = new CheckEvery(3);
            var checkHeartBeatEvery = new CheckEvery(10);

            this.IsRunning = true;

            while (this.IsRunning)
            {
                try
                {
                    if (checkMainStatsEvery.IsTimeToCheck)
                    {
                        checkMainStatsEvery.UpdateChecked();
                        HomeDashboardHub.SendStats(this._homeHubContext);
                    }

                    if (checkHeartBeatEvery.IsTimeToCheck)
                    {
                        checkHeartBeatEvery.UpdateChecked();
                        HeartBeatHub.Beat(_heartbeatHubContext);
                    }

                    Thread.Sleep(300);
                }
                catch (Exception)
                {
                    // ignore exceptions and just carry on
                    Thread.Sleep(3000);
                }
            }
        }
    }
}