using System;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Settings.ObjectModel;
using jsdal_server_core.Settings;
using System.IO;
using System.Threading;

namespace jsdal_server_core
{
    public class WorkSpawner
    {
        private static List<Worker> _workerList;

        public static string TEMPLATE_RoutineContainer;
        public static string TEMPLATE_Routine;
        public static string TEMPLATE_TypescriptDefinitions;

        // public static memDetail(): any {
        // return {
        //     Cnt: WorkSpawner._workerList.length,
        //     //TotalMemBytes: sizeof(WorkSpawner._workerList), 
        //     Workers: WorkSpawner._workerList.map(w => w.memDetail())
        // };
        //}

        public static Worker GetWorker(string id)
        {
            return WorkSpawner._workerList.FirstOrDefault(wl => wl.ID.Equals(id, StringComparison.Ordinal));
        }
        public static Worker GetWorkerById(string id)
        {
            return WorkSpawner._workerList.FirstOrDefault(wl => wl.ID.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public static Worker GetWorkerByEndpoint(Endpoint ep)
        {
            return WorkSpawner._workerList.FirstOrDefault(w => w.Endpoint == ep);
        }

        public static bool RestartWorker(Endpoint ep)
        {
            var w = GetWorkerByEndpoint(ep);
            if (w == null) return false;
            return RestartWorker(w);
        }
        public static bool RestartWorker(Worker worker)
        {
            if (worker.IsRunning) return false;

            var winThread = new Thread(new ThreadStart(worker.Run));

            worker.SetWinThread(winThread);

            winThread.Start();

            Hubs.WorkerMonitor.Instance.NotifyObservers();

            return true;
        }

        public static List<Worker> workerList
        {
            get
            {
                return WorkSpawner._workerList;
            }
        }

        public static void Stop()
        {
            if (_workerList == null) return;

            lock (_workerList)
            {
                _workerList.ForEach(wl =>
                {
                    try
                    {
                        wl.Stop();
                    }
                    catch (ThreadAbortException)
                    {
                        // ignore TAEs
                    }
                });
            }

            Hubs.WorkerMonitor.Instance.NotifyObservers();
        }

        public static void Start()
        {
            try
            {
                var endpoints = SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications).SelectMany(app => app.Endpoints).ToList();

                WorkSpawner.TEMPLATE_RoutineContainer = File.ReadAllText("./resources/RoutineContainerTemplate.txt");
                WorkSpawner.TEMPLATE_Routine = File.ReadAllText("./resources/RoutineTemplate.txt");
                WorkSpawner.TEMPLATE_TypescriptDefinitions = File.ReadAllText("./resources/TypeScriptDefinitionsContainer.d.ts");

                WorkSpawner._workerList = new List<Worker>();

                //dbSources = new DatabaseSource[] { dbSources.First() }.ToList(); //TEMP 

                // TODO: handle items (project/sources) that were deleted


                //async.each(dbSources, (source) => {
                endpoints.ForEach(endpoint =>
                {
                    //TEST!!
                    //   if (endpoint.Name != "DEV" || endpoint.Application.Name != "PWAs") return;

                    try
                    {
                        CreateNewWorker(endpoint);
                    }
                    catch (Exception e)
                    {
                        ExceptionLogger.LogException(e);
                        Console.WriteLine(e.ToString());
                    }
                });
            }
            catch (Exception e)
            {
                SessionLog.Exception(e);
            }

        } // Start

        public static void CreateNewWorker(Endpoint endpoint)
        {
            lock (_workerList)
            {
                if (GetWorkerByEndpoint(endpoint) != null) return;

                var worker = new Worker(endpoint);

                Console.WriteLine($"Spawning new worker for { endpoint.Pedigree }");

                WorkSpawner._workerList.Add(worker);

                var winThread = new Thread(new ThreadStart(worker.Run));

                winThread.Start();
            }

            Hubs.WorkerMonitor.Instance.NotifyObservers();
        }

        public static void RemoveEndpoint(Endpoint endpoint)
        {
            try
            {
                lock (_workerList)
                {
                    var worker = GetWorkerByEndpoint(endpoint);

                    if (worker != null)
                    {
                        worker.Stop();
                        _workerList.Remove(worker);
                        Hubs.WorkerMonitor.Instance.NotifyObservers();
                    }
                }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        // public static void AddDatabaseSource(Application app)
        // {
        //     try
        //     {
        //         var existing = _workerList.FirstOrDefault(w => w.App == app);

        //         if (existing == null)
        //         {
        //             var worker = new Worker(app);

        //             Console.WriteLine($"Spawning new worker for { app.Name}");

        //             WorkSpawner._workerList.Add(worker);

        //             var winThread = new Thread(new ThreadStart(worker.Run));

        //             winThread.Start();

        //             Hubs.WorkerMonitor.Instance.NotifyObservers();
        //         }

        //     }
        //     catch (Exception ex)
        //     {
        //         SessionLog.Exception(ex);
        //     }
        // }

        // public static void UpdateDatabaseSource(Application oldDbSource, Application newDbSource)
        // {
        //     try
        //     {// TODO: !!!!
        //         // var existing = _workerList.FirstOrDefault(w => w.DBSource == dbSource);

        //         // if (existing == null)
        //         // {
        //         //     var worker = new Worker(dbSource);

        //         //     Console.WriteLine($"Spawning new worker for { dbSource.Name}");

        //         //     WorkSpawner._workerList.Add(worker);

        //         //     var winThread = new Thread(new ThreadStart(worker.Run));

        //         //     winThread.Start();

        //         //     Hubs.WorkerMonitor.Instance.NotifyObservers();
        //         // }

        //     }
        //     catch (Exception ex)
        //     {
        //         SessionLog.Exception(ex);
        //     }
        // }

        public static void ResetMaxRowDate(Endpoint endpoint)
        {
            var worker = GetWorkerByEndpoint(endpoint);

            if (worker != null)
            {
                worker.ResetMaxRowDate();
            }
        }

        public static void HandleOrmInstalled(Endpoint endpoint)
        {
            var worker = GetWorkerByEndpoint(endpoint);

            if (worker != null)
            {
                if (worker.IsRunning)
                {
                    worker.ResetMaxRowDate();
                    endpoint.IsOrmInstalled = true;
                }
                else
                {
                    RemoveEndpoint(endpoint);
                    worker = null;
                }
            }

            if (worker == null)
            {
                CreateNewWorker(endpoint);
            }
        }

        public static void SetRulesDirty(Application app)
        {
            // regen all jsfiles on app
            var workers = WorkSpawner._workerList.Where(w => w.Endpoint.Application == app).ToList();

            foreach(var w in workers)
            {
                w.QueueInstruction(new WorkerInstruction() { Type = WorkerInstructionType.RegenAllFiles });
            }
        }

        public static void SetRulesDirty(Application app, JsFile jsFile)
        {
            // regen only affected jsFile
            var worker = WorkSpawner._workerList.FirstOrDefault(w => w.Endpoint.Application == app && app.JsFiles.Contains(jsFile));

            if (worker != null)
            {
                worker.QueueInstruction(new WorkerInstruction() { Type = WorkerInstructionType.RegenSpecificFile, JsFile = jsFile });
            }
        }
    }
}

