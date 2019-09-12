using Microsoft.Diagnostics.Tools.RuntimeClient;
//using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;


namespace jsdal_server_core.Performance.dotnet
{

    public class EventPipeEventSource
    {

        public EventPipeEventSource(object o) { }
        public EventPipeEventSourceDyn Dynamic;

        public void Process() { }


    }  // TEMP!!!

    public class EventPipeEventSourceDyn
    {
        public delegate void WhatWhatEvent(TraceEvent data);
        public event WhatWhatEvent All;
    }// TEMP

    public class TraceEvent
    {
        public string ProviderName;
        public string EventName;
        public dynamic PayloadValue(int n) { return null; }

    } // TEMP!!!


    public class CounterMonitor
    {
        private const ulong EmptySession = 0xffffffff;
        private readonly int _pid;
        private readonly IReadOnlyCollection<Provider> _providers;

        private ulong _sessionId = EmptySession;

        public event Action<CounterEventArgs> CounterUpdate;

        public CounterMonitor(int pid, IReadOnlyCollection<Provider> providers)
        {
            _pid = pid;
            _providers = providers;
        }

        public void Start()
        {
            // single exe publishing not happy with trace package -- wait on https://github.com/dotnet/sdk/issues/3510
            return;

            var configuration = new SessionConfiguration(
                circularBufferSizeMB: 1000,
                format: EventPipeSerializationFormat.NetTrace,
                providers: _providers
                );

            var binaryReader = EventPipeClient.CollectTracing(_pid, configuration, out _sessionId);
            EventPipeEventSource source = new EventPipeEventSource(binaryReader);

            source.Dynamic.All += ProcessEvents;

            // this is a blocking call
            source.Process();
        }

        public void Stop()
        {
            if (_sessionId == EmptySession)
                throw new InvalidOperationException("Start() must be called to start the session");

            EventPipeClient.StopTracing(_pid, _sessionId);
        }

        private void ProcessEvents(TraceEvent data)
        {
            if (data.EventName.Equals("EventCounters"))
            {
                IDictionary<string, object> countersPayload = (IDictionary<string, object>)(data.PayloadValue(0));
                IDictionary<string, object> kvPairs = (IDictionary<string, object>)(countersPayload["Payload"]);
                // the TraceEvent implementation throws not implemented exception if you try
                // to get the list of the dictionary keys: it is needed to iterate on the dictionary
                // and get each key/value pair.



                var name = string.Intern(kvPairs["Name"].ToString());
                var displayName = string.Intern(kvPairs["DisplayName"].ToString());

                var counterType = kvPairs["CounterType"];
                if (counterType.Equals("Sum"))
                {
                    OnSumCounter(data.ProviderName, name, displayName, kvPairs);
                }
                else
                if (counterType.Equals("Mean"))
                {
                    OnMeanCounter(data.ProviderName, name, displayName, kvPairs);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported counter type '{counterType}'");
                }
            }
        }

        private void OnSumCounter(string providerName, string name, string displayName, IDictionary<string, object> kvPairs)
        {
            double value = double.Parse(kvPairs["Increment"].ToString());

            // send the information to your metrics pipeline
            CounterUpdate(new CounterEventArgs(providerName, name, displayName, CounterType.Sum, value));
        }

        private void OnMeanCounter(string providerName, string name, string displayName, IDictionary<string, object> kvPairs)
        {
            double value = double.Parse(kvPairs["Mean"].ToString());

            // send the information to your metrics pipeline
            CounterUpdate(new CounterEventArgs(providerName, name, displayName, CounterType.Mean, value));
        }


        //Name = cpu-usage
        //DisplayName = CPU Usage
        //Mean = 0
        //StandardDeviation = 0
        //Count = 1
        //Min = 0
        //Max = 0
        //IntervalSec = 3.24E-05
        //Series = Interval=1000
        //CounterType = Mean
        //Metadata =

        //Name = gen-0-gc-count
        //DisplayName = Gen 0 GC Count
        //DisplayRateTimeScale = 00:01:00
        //Increment = 0
        //IntervalSec = 1.88E-05
        //Metadata =
        //Series = Interval=1000
        //CounterType = Sum

    }

    public class CounterEventArgs : EventArgs
    {
        internal CounterEventArgs(string provider, string name, string displayName, CounterType type, double value)
        {
            Provider = provider;
            Counter = name;
            DisplayName = displayName;
            Type = type;
            Value = value;
        }
        public string Provider { get; set; }
        public string Counter { get; set; }
        public string DisplayName { get; set; }
        public CounterType Type { get; set; }
        public double Value { get; set; }
    }

    public enum CounterType
    {
        Sum = 0,
        Mean = 1,
    }
}