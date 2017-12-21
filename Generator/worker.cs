using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;
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

        public DatabaseSource DBSource { get; private set; }
        public string Description
        {

            get { if (this.DBSource == null) return null; return $"{ this.DBSource.dataSource}; { this.DBSource.initialCatalog} "; }
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
            get {
                return log?.Entries;
            }
        }

        private DateTime? last0Cnt;


        public Worker(DatabaseSource dbSource)
        {
            this.ID = ShortId.Generate();
            this.DBSource = dbSource;
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
                Thread.CurrentThread.Name = "WorkerThread " + this.DBSource.Name;

                this.Status = "Started";

                this.IsRunning = true;
                this.IsRulesDirty = false;
                this.IsOutputFilesDirty = false;

                DateTime lastSavedDate = DateTime.Now;

                var cache = this.DBSource.cache;

                if (cache != null && cache.Count > 0)
                {
                    this.MaxRowDate = cache.Max(c => c.RowVer);
                }

                int connectionErrorCnt = 0;


                if (this.DBSource?.MetadataConnection.ConnectionStringDecrypted == null)
                {
                    this.IsRunning = false;
                    this.Status = $"Data source '{this.DBSource?.Name ?? "(null)"}' does not have valid connection configured.";
                    this.log.Error(this.Status);
                    SessionLog.Error(this.Status);
                    return;
                }

                while (this.IsRunning)
                {
                    isIterationDirty = false;

                    string connectionStringRef = null;

                    try
                    {
                        if (!DBSource.IsOrmInstalled)
                        {
                            // try again in 3 seconds
                            this.Status = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm")} - Waiting for ORM to be installed";
                            Thread.Sleep(3000);
                            continue;
                        }

                        var csb = new SqlConnectionStringBuilder(this.DBSource.MetadataConnection.ConnectionStringDecrypted);
                        connectionStringRef = $"Data Source={csb.DataSource}; UserId={csb.UserID}; Catalog={csb.InitialCatalog}";

                        using (var con = new SqlConnection(this.DBSource.MetadataConnection.ConnectionStringDecrypted))
                        {
                            try
                            {
                                con.Open();
                                connectionErrorCnt = 0;
                            }
                            catch (Exception oex)
                            {
                                this.Status = "Failed to open connection to database: " + oex.Message;
                                this.log.Exception(oex, con.ConnectionString);
                                SessionLog.Exception(oex, con.ConnectionString);
                                connectionErrorCnt++;

                                int waitMS = Math.Min(3000 + (connectionErrorCnt * 3000), 300000/*Max 5mins between tries*/);

                                this.Status = $"Attempt: #{connectionErrorCnt + 1} (waiting for {waitMS}ms). " + this.Status;

                                 Hubs.WorkerMonitor.Instance.NotifyObservers();

                                Thread.Sleep(waitMS);
                                continue;
                            }

                            Process(con, this.DBSource.MetadataConnection.ConnectionStringDecrypted);
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
                        // TODO: Decide what to do with an exception here
                    }

                } // while IsRunning
            }
            catch(ThreadAbortException)
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
            }
        } // Run

        private void Process(SqlConnection con, string connectionString)
        {
            var changesCount = OrmDAL.SprocGenGetRoutineListCnt(con, this.MaxRowDate);

            if (changesCount > 0)
            {
                last0Cnt = null;

                SessionLog.Info($"{ DBSource.Name}\t{ changesCount} change(s) found using row date { this.MaxRowDate}");
                //!this._log.info($"{ DBSource.Name}\t{ changesCount} change(s) found using row date ${ this.MaxRowDate}");
                this.Status = $"{ DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - { changesCount} change(s) found using rowdate { this.MaxRowDate}";

                GetAndProcessRoutineChanges(con, connectionString, changesCount);
                //await ProcessChanges...;


                {
                    // call save for final changes 
                    DBSource.saveCache();
                    this.generateOutputFiles(DBSource);
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

        private void GetAndProcessRoutineChanges(SqlConnection con, string connectionString, int changesCount)
        {
            var cmdSprocGenGetRoutineList = new SqlCommand();

            cmdSprocGenGetRoutineList.Connection = con;
            cmdSprocGenGetRoutineList.CommandType = System.Data.CommandType.StoredProcedure;
            cmdSprocGenGetRoutineList.CommandText = "orm.SprocGenGetRoutineList";
            cmdSprocGenGetRoutineList.Parameters.Add("maxRowver", System.Data.SqlDbType.BigInt).Value = MaxRowDate ?? 0;

            using (var reader = cmdSprocGenGetRoutineList.ExecuteReader())
            {
                var columns = new string[] { "Id", "CatalogName", "SchemaName", "RoutineName", "RoutineType", "rowver", "IsDeleted", "ParametersXml", "ParameterCount", "ObjectId", "JsonMetadata" };

                // maps column ordinals to proper names 
                var ix = columns.Select(s => new { s, Value = reader.GetOrdinal(s) }).ToDictionary(p => p.s, p => p.Value);

                var curRow = 0;

                DateTime lastSavedDate = DateTime.Now;

                while (reader.Read())
                {
                    //!this.progress("genGetRoutineListStream row...");

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
                        Parameters = new List<RoutineParameter>(),
                        RowVer = reader.GetInt64(ix["rowver"]),
                    };

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

                    this.Status = $"{ DateTime.Now.ToString("yyyy-MM-dd, HH:mm:ss")} - { DBSource.Name } - Overall progress: {curRow} of { changesCount } ({ perc.ToString("##0.00")}%) - [{ newCachedRoutine.Schema }].[{ newCachedRoutine.Routine }]";

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
                                                DataType = ((Type)row["DataType"]).FullName,
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

                    DBSource.addToCache(newCachedRoutine.RowVer, newCachedRoutine);

                    // TODO: Make saving gap configurable?
                    if (DateTime.Now.Subtract(lastSavedDate).TotalSeconds >= 20)
                    {
                        lastSavedDate = DateTime.Now;
                        DBSource.saveCache();
                    }

                    if (!this.MaxRowDate.HasValue || newCachedRoutine.RowVer > this.MaxRowDate.Value)
                    {
                        this.MaxRowDate = newCachedRoutine.RowVer;
                    }

                } // while reader.Read
            }
        }


        private List<RoutineParameter> ExtractParameters(string parametersXml)
        {
            if (parametersXml == null) return null;

            var xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(RoutineParameterContainer));

            using (var sr = new StringReader(parametersXml))
            {
                return (xmlSerializer.Deserialize(sr) as RoutineParameterContainer).Parameters;
            }
        }

        [Serializable]
        [XmlRoot("Routine")]
        public class RoutineParameterContainer
        {
            public RoutineParameterContainer()
            {
                this.Parameters = new List<RoutineParameter>();
            }

            public string Catalog { get; set; }
            public string Schema { get; set; }
            public string RoutineName { get; set; }

            [XmlElement("Parameter")]
            public List<RoutineParameter> Parameters { get; set; }
        }

        private void generateOutputFiles(DatabaseSource dbSource)
        {
            try
            {
                dbSource.JsFiles.ForEach(jsFile =>
                {
                    JsFileGenerator.generateJsFile(dbSource, jsFile);

                    this.IsRulesDirty = false;
                    this.IsOutputFilesDirty = false;
                    dbSource.LastUpdateDate = DateTime.Now;
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