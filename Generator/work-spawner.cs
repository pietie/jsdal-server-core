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

        public static void resetMaxRowDate(DatabaseSource dbSource)
        {
            var worker = WorkSpawner._workerList.FirstOrDefault(wl => wl.DBSource.CacheKey == dbSource.CacheKey);

            if (worker != null) worker.ResetMaxRowDate();
        }

        public static Worker getWorker(string name)
        {
            return WorkSpawner._workerList.FirstOrDefault(wl => wl.DBSource.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        public static Worker getWorkerById(string id)
        {
            return WorkSpawner._workerList.FirstOrDefault(wl => wl.ID.Equals(id, StringComparison.OrdinalIgnoreCase));
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

        }

        public static void Start()
        {
            try
            {
                var dbSources = SettingsInstance.Instance.ProjectList.SelectMany(p => p.DatabaseSources).ToList();

                WorkSpawner.TEMPLATE_RoutineContainer = File.ReadAllText("./resources/RoutineContainerTemplate.txt");
                WorkSpawner.TEMPLATE_Routine = File.ReadAllText("./resources/RoutineTemplate.txt");
                WorkSpawner.TEMPLATE_TypescriptDefinitions = File.ReadAllText("./resources/TypeScriptDefinitionsContainer.d.ts");

                WorkSpawner._workerList = new List<Worker>();

                //dbSources = new DatabaseSource[] { dbSources.First() }.ToList(); //TEMP 

                // TODO: handle items (project/sources) that were deleted

                //async.each(dbSources, (source) => {
                dbSources.ForEach(source =>
                {

                    try
                    {
                        var worker = new Worker(source);

                        Console.WriteLine($"Spawning new worker for { source.Name}");

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

        public static void RemoveDatabaseSource(DatabaseSource dbSource)
        {
            try
            {
                var workers = _workerList.Where(w => w.DBSource == dbSource);

                if (workers.Count() > 0)
                {
                    foreach (var w in workers)
                    {
                        w.Stop();
                    }

                    _workerList.RemoveAll(w => workers.Contains(w));

                    Hubs.WorkerMonitor.Instance.NotifyObservers();
                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public static void AddDatabaseSource(DatabaseSource dbSource)
        {
            try
            {
                var existing = _workerList.FirstOrDefault(w => w.DBSource == dbSource);

                if (existing == null)
                {
                    var worker = new Worker(dbSource);

                    Console.WriteLine($"Spawning new worker for { dbSource.Name}");

                    WorkSpawner._workerList.Add(worker);

                    var winThread = new Thread(new ThreadStart(worker.Run));

                    winThread.Start();

                    Hubs.WorkerMonitor.Instance.NotifyObservers();
                }

            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public static void UpdateDatabaseSource(DatabaseSource oldDbSource, DatabaseSource newDbSource)
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

