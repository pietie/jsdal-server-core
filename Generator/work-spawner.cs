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

        // public static void resetMaxRowDate(Application app)
        // {
        //     // TODO: Do not match on name alone? (Was .CacheKey before)
        //     var worker = WorkSpawner._workerList.FirstOrDefault(wl => wl.DBSource == app);

        //     if (worker != null) worker.ResetMaxRowDate();
        // }

        public static Worker GetWorker(string id)
        {
            return WorkSpawner._workerList.FirstOrDefault(wl => wl.ID.Equals(id, StringComparison.Ordinal));
        }
        public static Worker GetWorkerById(string id)
        {
            return WorkSpawner._workerList.FirstOrDefault(wl => wl.ID.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public static bool RestartWorker(Worker worker)
        {
            if (worker.IsRunning) return false;

            var winThread = new Thread(new ThreadStart(worker.Run));

            worker.SetWinThread(winThread);

            winThread.Start();

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

            lock(_workerList)
            {
                _workerList.ForEach(wl=>{
                    try
                    {
                        wl.Stop();
                    }
                    catch(ThreadAbortException)
                    {
                        // ignore TAEs
                    }
                });
            }
        }

        public static void Start()
        {
            try
            {
                var endpoints = SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications).SelectMany(app=>app.Endpoints).ToList();

                WorkSpawner.TEMPLATE_RoutineContainer = File.ReadAllText("./resources/RoutineContainerTemplate.txt");
                WorkSpawner.TEMPLATE_Routine = File.ReadAllText("./resources/RoutineTemplate.txt");
                WorkSpawner.TEMPLATE_TypescriptDefinitions = File.ReadAllText("./resources/TypeScriptDefinitionsContainer.d.ts");

                WorkSpawner._workerList = new List<Worker>();

                //dbSources = new DatabaseSource[] { dbSources.First() }.ToList(); //TEMP 

                // TODO: handle items (project/sources) that were deleted

                //async.each(dbSources, (source) => {
                endpoints.ForEach(endpoint =>
                {

                    try
                    {
                        var worker = new Worker(endpoint);

                        Console.WriteLine($"Spawning new worker for { endpoint.Pedigree }");

                        WorkSpawner._workerList.Add(worker);

                        var winThread = new Thread(new ThreadStart(worker.Run));

                        winThread.Start();
                    }
                    catch (Exception e)
                    {
                        ExceptionLogger.logException(e);
                        Console.WriteLine(e.ToString());
                    }
                });
            }
            catch (Exception e)
            {
                SessionLog.Exception(e);
            }

        } // Start

        public static void RemoveApplication(Application app)
        {
            try
            {
                //! TODO: fix up
                // var workers = _workerList.Where(w => w.App == app);

                // if (workers.Count() > 0)
                // {
                //     foreach (var w in workers)
                //     {
                //         w.Stop();
                //     }

                //     _workerList.RemoveAll(w => workers.Contains(w));

                //     Hubs.WorkerMonitor.Instance.NotifyObservers();
                // }

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

        public static void UpdateDatabaseSource(Application oldDbSource, Application newDbSource)
        {
            try
            {// TODO: !!!!
                // var existing = _workerList.FirstOrDefault(w => w.DBSource == dbSource);

                // if (existing == null)
                // {
                //     var worker = new Worker(dbSource);

                //     Console.WriteLine($"Spawning new worker for { dbSource.Name}");

                //     WorkSpawner._workerList.Add(worker);

                //     var winThread = new Thread(new ThreadStart(worker.Run));

                //     winThread.Start();

                //     Hubs.WorkerMonitor.Instance.NotifyObservers();
                // }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }


    }
}

