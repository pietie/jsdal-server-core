using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;
using jsdal_server_core.Changes;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Newtonsoft.Json;
using shortid;

namespace jsdal_server_core
{
    public class Worker
    {
        public string ID { get; private set; }

        private MemoryLog log;

        public Endpoint Endpoint { get; private set; }

        public string Description
        {
            get { if (this.Endpoint == null) return null; return $"{ this.Endpoint.MetadataConnection?.DataSource}; { this.Endpoint.MetadataConnection?.InitialCatalog} "; }
        }

        private Thread winThread;

        //public Thread Thread { get { return winThread; }}

        public bool IsRunning
        {
            get;
            private set;
        }

        public bool IsRulesDirty { get; set; }
        public bool IsOutputFilesDirty { get; set; }
        public long? MaxRowDate { get; private set; }

        private bool isIterationDirty = false; // if true then the Worker Monitor (SignalR) gets to update

        private string _status;
        public string Status
        {
            get { return _status; }
            set { _status = value; isIterationDirty = true; }
        }

        public List<LogEntry> LogEntries
        {
            get
            {
                return log?.Entries;
            }
        }

        private DateTime? last0Cnt;


        public Worker(Endpoint endpoint)
        {
            this.ID = ShortId.Generate(useNumbers: false, useSpecial: false, length: 6);
            this.Endpoint = endpoint;
            this.log = new MemoryLog();
        }

        public void Stop()
        {
            this.IsRunning = false;

            if (this.winThread != null && this.winThread.ThreadState == ThreadState.Running)
            {
                // give the thread 10 seconds to finish gracefully
                if (!this.winThread.Join(TimeSpan.FromSeconds(10)))
                {
                    this.winThread.Abort();
                }
                this.winThread = null;
            }

            Hubs.WorkerMonitor.Instance.NotifyObservers();
        }

        public void SetWinThread(Thread thread)
        {
            this.winThread = thread;
        }

        public void ResetMaxRowDate()
        {
            this.MaxRowDate = 0;
        }

        public void Run()
        {
            try
            {
                Thread.CurrentThread.Name = "WorkerThread " + this.Endpoint.Pedigree;

                this.Status = "Started";

                this.IsRunning = true;
                this.IsRulesDirty = false;
                this.IsOutputFilesDirty = false;

                DateTime lastSavedDate = DateTime.Now;

                var cache = this.Endpoint.cache;

                if (cache != null && cache.Count > 0)
                {
                    this.MaxRowDate = cache.Max(c => c.RowVer);
                }

                int connectionOpenErrorCnt = 0;


                if (this.Endpoint?.MetadataConnection?.ConnectionStringDecrypted == null)
                {
                    this.IsRunning = false;
                    this.Status = $"Endpoint '{this.Endpoint?.Name ?? "(null)"}' does not have valid connection configured.";
                    this.log.Error(this.Status);
                    SessionLog.Error(this.Status);
                    return;
                }

                var exceptionThrottler = new SortedList<DateTime, Exception>();

                while (this.IsRunning)
                {
                    isIterationDirty = false;

                    string connectionStringRef = null;

                    try
                    {
                        if (!Endpoint.IsOrmInstalled)
                        {
                            // try again in 3 seconds
                            this.Status = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm")} - Waiting for ORM to be installed";
                            Thread.Sleep(3000);
                            continue;
                        }

                        var csb = new SqlConnectionStringBuilder(this.Endpoint.MetadataConnection.ConnectionStringDecrypted);
                        connectionStringRef = $"Data Source={csb.DataSource}; UserId={csb.UserID}; Catalog={csb.InitialCatalog}";

                        using (var con = new SqlConnection(this.Endpoint.MetadataConnection.ConnectionStringDecrypted))
                        {
                            try
                            {
                                con.Open();
                                connectionOpenErrorCnt = 0;
                            }
                            catch (Exception oex)
                            {
                                this.Status = "Failed to open connection to database: " + oex.Message;
                                this.log.Exception(oex, connectionStringRef);
                                SessionLog.Exception(oex, connectionStringRef);
                                connectionOpenErrorCnt++;

                                int waitMS = Math.Min(3000 + (connectionOpenErrorCnt * 3000), 300000/*Max 5mins between tries*/);

                                this.Status = $"Attempt: #{connectionOpenErrorCnt + 1} (waiting for {waitMS}ms). " + this.Status;

                                Hubs.WorkerMonitor.Instance.NotifyObservers();

                                Thread.Sleep(waitMS);
                                continue;
                            }

                            Process(con, this.Endpoint.MetadataConnection.ConnectionStringDecrypted);
                        } // using connection

                        if (isIterationDirty)
                        {
                            Hubs.WorkerMonitor.Instance.NotifyObservers();
                        }

                        Thread.Sleep(SettingsInstance.Instance.Settings.DbSource_CheckForChangesInMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        this.log.Exception(ex);
                        SessionLog.Exception(ex);

                        exceptionThrottler.Add(DateTime.Now, ex);

                        // TODO: make configurable
                        var thresholdDate = DateTime.Now.AddSeconds(-60);

                        var beforeThreshold = exceptionThrottler.Where(kv => kv.Key < thresholdDate);

                        // remove items outside of threshold checking window
                        foreach (var kv in beforeThreshold) { exceptionThrottler.Remove(kv.Key); }

                        // TODO: make threshold count configurable
                        if (exceptionThrottler.Count() >= 6)
                        {
                            exceptionThrottler.Clear();
                            this.Status = $"{ DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - Too many exceptions, shutting down thread for now. Last exception: {ex.Message}";
                            Hubs.WorkerMonitor.Instance.NotifyObservers();
                            break; // break out of main while loop
                        }


                        this.Status = $"{ DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - {ex.Message}";
                        Hubs.WorkerMonitor.Instance.NotifyObservers();

                        var sleepTimeMS = 2000 + (exceptionThrottler.Count() * 400);

                        // cap at 8 secs
                        sleepTimeMS = sleepTimeMS > 8000 ? 8000 : sleepTimeMS;

                        Thread.Sleep(sleepTimeMS);


                    }

                } // while IsRunning
            }
            catch (ThreadAbortException)
            {
                // ignore TAEs
            }
            catch (Exception ex)
            {
                this.log.Exception(ex);
                SessionLog.Exception(ex);
            }
            finally
            {
                this.IsRunning = false;
                Hubs.WorkerMonitor.Instance.NotifyObservers();
            }
        } // Run

        private void Process(SqlConnection con, string connectionString)
        {
            var changesCount = OrmDAL.GetRoutineListCnt(con, this.MaxRowDate);

            if (changesCount > 0)
            {
                last0Cnt = null;

                SessionLog.Info($"{ Endpoint.Pedigree }\t{ changesCount} change(s) found using row date { this.MaxRowDate}");
                this.log.Info($"{ changesCount} change(s) found using row date { this.MaxRowDate}");
                this.Status = $"{ DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - { changesCount} change(s) found using rowdate { this.MaxRowDate}";

                GetAndProcessRoutineChanges(con, connectionString, changesCount, out var changesList);

                {
                    // call save for final changes 
                    this.Endpoint.SaveCache();
                    this.GenerateOutputFiles(this.Endpoint, changesList);
                    // save "settings" to persist JsFile version changes
                    SettingsInstance.SaveSettingsToFile(); 
                }

            }// if changeCount > 0
            else
            {
                if (last0Cnt == null) last0Cnt = DateTime.Now;

                // only update status if we've been receiving 0 changes for a while
                if (DateTime.Now.Subtract(last0Cnt.Value).TotalSeconds > 30)
                {
                    last0Cnt = DateTime.Now;
                    this.Status = $"{ DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - no changes found";
                }

                // handle the case where the output files no longer exist but we have also not seen any changes on the DB again
                // TODO: !!!
                // dbSource.JsFiles.forEach(jsFile =>
                // {
                //     let path = dbSource.outputFilePath(jsFile);

                //     if (!fs.existsSync(path))
                //     {
                //         this.progress('Generating ' + jsFile.Filename);
                //         JsFileGenerator.generateJsFile(dbSource, jsFile);

                //         //!this.IsRulesDirty = false;
                //         //!this.IsOutputFilesDirty = false;
                //         dbSource.LastUpdateDate = new Date();
                //     }

                // });
            }

        } // Process

        private void GetAndProcessRoutineChanges(SqlConnection con, string connectionString, int changesCount, out Dictionary<string, ChangeDescriptor> changesList)
        {
            var cmdGetRoutineList = new SqlCommand();

            cmdGetRoutineList.Connection = con;
            cmdGetRoutineList.CommandType = System.Data.CommandType.StoredProcedure;
            cmdGetRoutineList.CommandText = "ormv2.GetRoutineList";
            cmdGetRoutineList.Parameters.Add("maxRowver", System.Data.SqlDbType.BigInt).Value = MaxRowDate ?? 0;

            changesList = new Dictionary<string, ChangeDescriptor>();

            using (var reader = cmdGetRoutineList.ExecuteReader())
            {
                if (!this.IsRunning) return;

                var columns = new string[] { "Id", "CatalogName", "SchemaName", "RoutineName", "RoutineType", "rowver", "IsDeleted", "ParametersXml", "ObjectId", "JsonMetadata", "LastUpdateByHostName" };

                // maps column ordinals to proper names 
                var ix = columns.Select(s => new { s, Value = reader.GetOrdinal(s) }).ToDictionary(p => p.s, p => p.Value);

                var curRow = 0;

                DateTime lastSavedDate = DateTime.Now;

                while (reader.Read())
                {
                    if (!this.IsRunning) break;

                    if (changesCount == 1)
                    {
                        //!this._log.info(`(single change) ${ dbSource.Name}\t[${ row.SchemaName}].[${row.RoutineName}]`); 
                    }

                    var newCachedRoutine = new CachedRoutine()
                    {
                        Routine = reader.GetString(ix["RoutineName"]),
                        Schema = reader.GetString(ix["SchemaName"]),
                        Type = reader.GetString(ix["RoutineType"]),
                        IsDeleted = reader.GetBoolean(ix["IsDeleted"]),
                        Parameters = new List<RoutineParameterV2>(),
                        RowVer = reader.GetInt64(ix["rowver"]),
                    };

                    
                    var lastUpdateByHostName = reader.GetString(ix["LastUpdateByHostName"]);

                    string jsonMetadata = null;

                    if (!reader.IsDBNull(ix["JsonMetadata"])) jsonMetadata = reader.GetString(ix["JsonMetadata"]);

                    if (!string.IsNullOrWhiteSpace(jsonMetadata))
                    {
                        try
                        {
                            newCachedRoutine.jsDALMetadata = JsonConvert.DeserializeObject<jsDALMetadata>(jsonMetadata);
                        }
                        catch (Exception ex)
                        {
                            newCachedRoutine.jsDALMetadata = new jsDALMetadata() { Error = ex.ToString() };
                        }
                    }

                    string parametersXml = null;

                    if (!reader.IsDBNull(ix["ParametersXml"]))
                    {
                        parametersXml = reader.GetString(ix["ParametersXml"]);

                        var parameterList = ExtractParameters(parametersXml);

                        newCachedRoutine.Parameters = parameterList;
                    }

                    curRow++;
                    var perc = ((double)curRow / (double)changesCount) * 100.0;

                    this.Status = $"{ DateTime.Now.ToString("yyyy-MM-dd, HH:mm:ss")} - Overall progress: {curRow} of { changesCount } ({ perc.ToString("##0.00")}%) - [{ newCachedRoutine.Schema }].[{ newCachedRoutine.Routine }]";

                    if (curRow % 10 == 0)
                    {
                        Hubs.WorkerMonitor.Instance.NotifyObservers();
                    }

                    if (!newCachedRoutine.IsDeleted)
                    {
                        /*
                            Resultset METADATA
                        */
                        if (newCachedRoutine.ResultSetRowver >= newCachedRoutine.RowVer)
                        {
                            Console.WriteLine("Result set metadata up to date");
                        }
                        else
                        {
                            //logLine.Append("Generating result set metadata");
                            //console.log("Generating result set metadata");

                            try
                            {
                                string resultSetError = null;
                                // get schema details of all result sets
                                var resultSets = OrmDAL.RoutineGetFmtOnlyResults(connectionString, newCachedRoutine.Schema, newCachedRoutine.Routine
                                , newCachedRoutine.Parameters, out resultSetError);

                                if (resultSets != null)
                                {
                                    Dictionary<string, dynamic> resultSetsDictionary = new Dictionary<string, dynamic>();

                                    foreach (DataTable dt in resultSets.Tables)
                                    {
                                        List<dynamic> lst = new List<dynamic>();

                                        for (var rowIx = 0; rowIx < dt.Rows.Count; rowIx++)
                                        {
                                            DataRow row = dt.Rows[rowIx];

                                            var schemaRow = new
                                            {
                                                ColumnName = row["ColumnName"],
                                                DataType = GetCSharpType(row),
                                                DbDataType = row["DataTypeName"],
                                                ColumnSize = Convert.ToInt32(row["ColumnSize"]),
                                                NumericalPrecision = Convert.ToUInt16(row["NumericPrecision"]),
                                                NumericalScale = Convert.ToUInt16(row["NumericScale"])
                                            };

                                            lst.Add(schemaRow);
                                        }

                                        resultSetsDictionary.Add(dt.TableName, lst);
                                    }

                                    newCachedRoutine.ResultSetMetadata = resultSetsDictionary;
                                    //!newCachedRoutine.ResultSetMetadata = resultSets;
                                    newCachedRoutine.ResultSetRowver = reader.GetInt64(ix["rowver"]);
                                    newCachedRoutine.ResultSetError = resultSetError;
                                }
                            }
                            catch (Exception e)
                            {
                                newCachedRoutine.ResultSetRowver = reader.GetInt64(ix["rowver"]);
                                newCachedRoutine.ResultSetError = e.ToString();
                            }
                        }
                    } // !IsDeleted

                    Endpoint.AddToCache(newCachedRoutine.RowVer, newCachedRoutine, lastUpdateByHostName, out var changesDesc);

                    if (changesDesc != null)
                    {
                        if (!changesList.ContainsKey(newCachedRoutine.FullName.ToLower()))
                        {
                            changesList.Add(newCachedRoutine.FullName.ToLower(), changesDesc);
                        }
                    }

                    // TODO: Make saving gap configurable?
                    if (DateTime.Now.Subtract(lastSavedDate).TotalSeconds >= 20)
                    {
                        lastSavedDate = DateTime.Now;
                        Endpoint.SaveCache();
                    }

                    if (!this.MaxRowDate.HasValue || newCachedRoutine.RowVer > this.MaxRowDate.Value)
                    {
                        this.MaxRowDate = newCachedRoutine.RowVer;
                    }

                } // while reader.Read
            }
        }

        private string GetCSharpType(DataRow row)
        {
            var rowDataType = row["DataType"];
            var rowDataTypeName = (string)row["DataTypeName"];

            if (rowDataType == DBNull.Value)
            {
                if (rowDataTypeName.ToLower().EndsWith(".sys.geography"))
                {
                    return (string)row["UdtAssemblyQualifiedName"];

                    //return typeof(System.Dynamic.DynamicObject).FullName;
                }
                else throw new NotImplementedException("Add support for DataType: " + rowDataTypeName);
            }
            else
            {
                return ((Type)row["DataType"]).FullName;
            }
        }

        private List<RoutineParameterV2> ExtractParameters(string parametersXml)
        {
            if (parametersXml == null) return null;

            var xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(RoutineParameterContainerV2));

            parametersXml = $"<Routine>{parametersXml}</Routine>";

            using (var sr = new StringReader(parametersXml))
            {
                var val = (xmlSerializer.Deserialize(sr) as RoutineParameterContainerV2);
                return val.Parameters;
            }
        }

        private void GenerateOutputFiles(Endpoint endpoint, Dictionary<string, ChangeDescriptor> fullChangeSet)
        {
            try
            {
                // TODO: changesList contains absolute of changes..does not necessarily apply to all files!!!!
                endpoint.Application.JsFiles.ForEach(jsFile =>
                {
                    JsFileGenerator.GenerateJsFile(endpoint, jsFile, fullChangeSet);

                    this.IsRulesDirty = false;
                    this.IsOutputFilesDirty = false;

                    endpoint.LastUpdateDate = DateTime.Now;

                });
            }
            catch (Exception ex)
            {
                this.log.Exception(ex);
                SessionLog.Exception(ex);
            }
        }

        // [Serializable]
        // public class RoutineParameter
        // {
        //     public string ParameterMode { get; set; }
        //     public string IsResult { get; set; }
        //     public string ParameterName { get; set; }
        //     public string DataType { get; set; }
        //     public int? Length { get; set; }

        //     public string DefaultValue { get; set; }
        //     //!public Microsoft.SqlServer.TransactSql.ScriptDom.LiteralType? DefaultValueType { get; set; }

        //     [JsonIgnore]
        //     public bool HasDefault { get { return !string.IsNullOrEmpty(this.DefaultValue); } }
        // }


    }

}