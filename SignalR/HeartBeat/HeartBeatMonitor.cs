// using System;
// using System.Linq;
// using System.Collections.Generic;
// using System.Threading;
// using jsdal_server_core;


// namespace jsdal_server_core.Hubs.HeartBeat
// {
//     public class HeartBeatMonitor : IObservable<int>
//     {
//         private static HeartBeatMonitor _singleton;
//         List<IObserver<int>> observers;

//         public static HeartBeatMonitor Instance { get { if (_singleton == null) _singleton = new HeartBeatMonitor(); return _singleton; } }

//         private HeartBeatMonitor()
//         {
//             observers = new List<IObserver<int>>();
//         }

//         private class Unsubscriber : IDisposable
//         {
//             private List<IObserver<int>> _observers;
//             private IObserver<int> _observer;

//             public Unsubscriber(List<IObserver<int>> observers, IObserver<int> observer)
//             {
//                 this._observers = observers;
//                 this._observer = observer;
//             }

//             public void Dispose()
//             {
//                 if (!(_observer == null)) _observers.Remove(_observer);
//             }
//         }

//         public IDisposable Subscribe(IObserver<int> observer)
//         {
//             if (!observers.Contains(observer))
//                 observers.Add(observer);

//             return new Unsubscriber(observers, observer);
//         }

//         public void NotifyObservers()
//         {
//             foreach (var observer in observers.ToArray())
//             {
//                 if (observer != null)
//                 {
//                     try
//                     {
//                         observer.OnNext(Environment.TickCount);
//                     }
//                     catch (System.OperationCanceledException)
//                     {
//                         // ignore the case where the Observer has been cancelled since
//                     }
//                 }
//             }
//         }
//     }


// }