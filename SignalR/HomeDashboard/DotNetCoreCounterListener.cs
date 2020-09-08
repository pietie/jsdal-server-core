using jsdal_server_core.Hubs;
using jsdal_server_core.Performance.dotnet;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace jsdal_server_core.SignalR.HomeDashboard
{
    public class DotNetCoreCounterListener
    {
        private readonly string _filename;
        private readonly int _pid;
        private CounterMonitor _counterMonitor;
        //private List<(string name, double value)> _countersValue;
        private Dictionary<string/*provider*/, Dictionary<string/*counter-name*/, CounterEventArgs/*counter*/>> _counterValues;

        private readonly IHubContext<HomeDashboardHub> _hubContext;
        public DotNetCoreCounterListener(IHubContext<HomeDashboardHub> ctx)
        {
            _hubContext = ctx;
            _counterValues = new Dictionary<string, Dictionary<string, CounterEventArgs>>();
            _pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            
            this.CounterValues = new ReadOnlyDictionary<string, Dictionary<string, CounterEventArgs>>(_counterValues);
        }

        public ReadOnlyDictionary<string/*provider*/, Dictionary<string/*counter-name*/, CounterEventArgs/*counter*/>> CounterValues { get; private set; }

        public static DotNetCoreCounterListener Instance { get; set; }


        public void Start()
        {
            if (_counterMonitor != null) return;

            _counterMonitor = new CounterMonitor(_pid, GetProviders());
            _counterMonitor.CounterUpdate += OnCounterUpdate;

            Task monitorTask = new Task(() =>
            {
                try
                {
                    _counterMonitor.Start();
                }
                catch (Exception x)
                {
                    //   Environment.FailFast("Error while listening to counters", x);

                }
            });
            monitorTask.Start();
        }

        private void OnCounterUpdate(CounterEventArgs args)
        {

            if (!_counterValues.ContainsKey(args.Provider))
            {
                _counterValues.Add(args.Provider, new Dictionary<string, CounterEventArgs>());
            }

            _counterValues[args.Provider][args.Counter] = args;

            // TODO: Can we queue up a change and only send after couple of seconds?
            _hubContext.Clients.Group(HomeDashboardHub.GROUP_NAME_CLR_COUNTERS).SendAsync("clrCounterUpdate", _counterValues);
            // if (!_counterValues[args.Provider].ContainsKey(args.Counter))
            // {
            //     _counterValues
            // }

            //_counterValues[args.Provider]
            //_countersValue.Add((args.DisplayName, args.Value));

            // we "know" that the last CLR counter is "assembly-count"
            // NOTE: this is a flaky way to detect the last counter event:
            //       -> could get the list of counters the first time they are received
            // if (args.Counter == "assembly-count")
            // {
            //     SaveLine();
            //     _countersValue.Clear();
            // }
        }

        bool isHeaderSaved = false;
        private void SaveLine()
        {

            // if (!isHeaderSaved)
            // {
            //     File.AppendAllText(_filename, GetHeaderLine());
            //     isHeaderSaved = true;
            // }

            // File.AppendAllText(_filename, GetCurrentLine());
        }

        // private string GetHeaderLine()
        // {
        //     StringBuilder buffer = new StringBuilder();
        //     foreach (var counter in _countersValue)
        //     {
        //         buffer.AppendFormat("{0}\t", counter.name);
        //     }

        //     // remove last tab
        //     buffer.Remove(buffer.Length - 1, 1);

        //     // add Windows-like new line because will be used in Excel
        //     buffer.Append("\r\n");

        //     return buffer.ToString();
        // }

        // private string GetCurrentLine()
        // {
        //     StringBuilder buffer = new StringBuilder();
        //     foreach (var counter in _countersValue)
        //     {
        //         buffer.AppendFormat("{0}\t", counter.value.ToString());
        //     }

        //     // remove last tab
        //     buffer.Remove(buffer.Length - 1, 1);

        //     // add Windows-like new line because will be used in Excel
        //     buffer.Append("\r\n");

        //     return buffer.ToString();
        // }

        public void Stop()
        {
            if (_counterMonitor != null)
            {
                _counterMonitor.Stop();
                _counterMonitor = null;
            }

            _counterValues?.Clear();
        }

        private IReadOnlyCollection<Provider> GetProviders()
        {
            var providers = new List<Provider>();

            providers.Add(CounterHelpers.MakeProvider("System.Runtime", 5));
            providers.Add(CounterHelpers.MakeProvider("Microsoft.AspNetCore.Hosting", 5));
            providers.Add(CounterHelpers.MakeProvider("Microsoft.AspNetCore.Http.Connections", 5));

            return providers;
        }
    }
}