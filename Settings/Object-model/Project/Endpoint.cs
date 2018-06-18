using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System.Data.SqlClient;
using shortid;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class Endpoint
    {
        public string Name { get; set; }
        public string Id;

        public DateTime LastUpdateDate;
        public Connection MetadataConnection;

        public Connection ExecutionConnection;

        public bool IsOrmInstalled;

        [JsonIgnore] private List<CachedRoutine> CachedRoutineList;

        [JsonIgnore] public Application Application { get; private set; }


        public Endpoint()
        {
            this.CachedRoutineList = new List<CachedRoutine>();
        }

        public void UpdateParentReferences(Application app)
        {
            this.Application = app;
        }

        public string CheckForMissingOrmPreRequisitesOnDatabase()
        {
            var sqlScript = File.ReadAllText("./resources/check-pre-requisites.sql", System.Text.Encoding.UTF8);

            using (var con = new SqlConnection(this.MetadataConnection.ConnectionStringDecrypted))
            {
                var cmd = new SqlCommand();

                cmd.Connection = con;
                cmd.CommandType = System.Data.CommandType.Text;
                cmd.CommandText = sqlScript;
                cmd.Parameters.Add("@err", System.Data.SqlDbType.VarChar, int.MaxValue).Direction = System.Data.ParameterDirection.Output;

                con.Open();

                cmd.ExecuteNonQuery();

                if (cmd.Parameters["@err"].Value == DBNull.Value)
                {
                    // TODO: Can the validity be cached somehow?
                    return null;
                }

                return (string)cmd.Parameters["@err"].Value;
            }

        }
        public bool InstallOrm()
        {
            var missing = CheckForMissingOrmPreRequisitesOnDatabase();

            if (string.IsNullOrEmpty(missing))
            {
                return true;
            }

            var installSqlScript = File.ReadAllText("./resources/install-orm.sql", System.Text.Encoding.UTF8);

            using (var con = new SqlConnection())
            {
                con.ConnectionString = this.MetadataConnection.ConnectionStringDecrypted;
                con.Open();

                var cmd = new SqlCommand();

                cmd.Connection = con;
                cmd.CommandText = installSqlScript;
                cmd.CommandTimeout = 120;

                cmd.ExecuteNonQuery();

                con.Close();
            }

            return true;

        }

        public bool UnInstallOrm()
        {
            using (var con = new SqlConnection())
            {
                con.ConnectionString = this.MetadataConnection.ConnectionStringDecrypted;
                con.Open();

                var cmd = new SqlCommand();

                cmd.Connection = con;
                cmd.CommandText = "exec orm.Uninstall";
                cmd.CommandTimeout = 120;

                cmd.ExecuteNonQuery();

                con.Close();
            }

            return true;

        }

        public CommonReturnValueWithApplication UpdateMetadataConnection(string dataSource, string catalog, string username, string password, int port)
        {
            if (this.MetadataConnection == null) this.MetadataConnection = new Connection();

            this.MetadataConnection.update(dataSource, catalog, username, password, port, null);

            return CommonReturnValueWithApplication.success(null);


        }
        public CommonReturnValueWithApplication UpdateExecConnection(string dataSource, string catalog, string username, string password, int port)
        {
            if (this.ExecutionConnection == null) this.ExecutionConnection = new Connection();

            this.ExecutionConnection.update(dataSource, catalog, username, password, port, null);

            return CommonReturnValueWithApplication.success(null);


        }

        public void AddToCache(long maxRowDate, CachedRoutine newCachedRoutine)
        {
            if (this.Id == null) this.Id = ShortId.Generate();

            if (this.CachedRoutineList == null)
            {
                this.CachedRoutineList = new List<CachedRoutine>();
            }

            lock (CachedRoutineList)
            {
                // get those items that are existing and have been changed (Updated or Deleted)
                var changed = this.CachedRoutineList.Where(e => newCachedRoutine.equals(e)).ToList();

                if (changed.Count > 0)
                {
                    // remove existing cached version as it will just be added again below
                    this.CachedRoutineList.RemoveAll(i => changed.Contains(i));
                }

                this.CachedRoutineList.Add(newCachedRoutine);
            }
        }

        private string CacheFilename
        {
            get
            {
                return $"{this.Application.Project.Name}.{this.Application.Name}.{this.Name}.json";
            }
        }

        public void LoadCache()
        {
            try
            {
                string cachePath = "./cache";
                string cacheFilePath = Path.Combine(cachePath, this.CacheFilename);

                if (!File.Exists(cacheFilePath)) return;

                this.CachedRoutineList = new List<CachedRoutine>();

                var data = File.ReadAllText(cacheFilePath, System.Text.Encoding.UTF8);

                var allCacheEntries = JsonConvert.DeserializeObject<List<CachedRoutine>>(data);

                this.CachedRoutineList = allCacheEntries;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public void SaveCache()
        {
            try
            {
                string cachePath = "./cache";

                if (!Directory.Exists(cachePath))
                {
                    try
                    {
                        Directory.CreateDirectory(cachePath);
                    }
                    catch (Exception e)
                    {
                        SessionLog.Exception(e);
                    }
                }

                var cacheFilePath = Path.Combine(cachePath, this.CacheFilename);
                var json = JsonConvert.SerializeObject(this.CachedRoutineList);

                File.WriteAllText(cacheFilePath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {

                throw ex; // TODO: handle
            }
        }

        [JsonIgnore]
        public List<CachedRoutine> cache { get { return this.CachedRoutineList; } }

        public void ClearCache()
        {
            try
            {
                var cachePath = "./cache";

                if (!Directory.Exists(cachePath)) return;

                var cacheFilePath = Path.Combine(cachePath, $"{this.Id}.json");

                if (!File.Exists(cacheFilePath)) return;

                this.CachedRoutineList = new List<CachedRoutine>();

                File.Delete(cacheFilePath);

                //!!!!                WorkSpawner.resetMaxRowDate(this);
                this.LastUpdateDate = DateTime.Now;
            }
            catch (Exception ex)
            {
                //SessionLog.Exception(ex);
                throw ex;// TODO: HANDLE!!!!
            }

        }

        public Connection GetSqlConnection()
        {
            if (this.ExecutionConnection == null)
            {
                SessionLog.Error($"Execution connection not found on endpoint '{this.Name}'({ this.Id }).");
            }


            return this.ExecutionConnection;
        }

        [JsonIgnore] public string OutputDir
        {
            get
            {
                return Path.GetFullPath($"./generated/{ this.Application.Project.Name }/{ this.Application.Name }/{this.Name}");
            }
        }

        public string OutputFilePath(JsFile jsFile)
        {
            return Path.Combine(this.OutputDir, jsFile.Filename);
        }

        public string OutputTypeScriptTypingsFilePath(JsFile jsFile)
        {
            return Path.Combine(this.OutputDir, jsFile.Filename.Substring(0, jsFile.Filename.LastIndexOf('.')) + ".d.ts");
        }

        public string MinifiedOutputFilePath(JsFile jsFile)
        {
            return Path.Combine(this.OutputDir, jsFile.Filename.Substring(0, jsFile.Filename.Length - 3) + ".min.js");
        }

        public void ApplyRules(JsFile jsFileContext)
        {
            if (this.CachedRoutineList == null) return;

            foreach (var routine in this.CachedRoutineList)
            {
                if (routine.RuleInstructions == null) continue;
                if (routine.RuleInstructions.Count == 1 && routine.RuleInstructions.First().Key == null)
                    continue; // PL: No idea why this happens but when no rules exist RuleInstructions contains a single KeyValue pair that are both null...this causes routine.RuleInstructions[jsFileContext] to hang 

                routine.RuleInstructions[jsFileContext] = null;

                if (routine.IsDeleted) continue;

                var instruction = routine.ApplyRules(this.Application, jsFileContext);

                routine.RuleInstructions[jsFileContext] = instruction;

            };
        }

        [JsonIgnore] public string Pedigree
        {
            get
            {
                return (this.Application?.Project?.Name + "/" ?? "") + (this.Application?.Name + "/" ?? "") + this.Name;
            }
        }

    }

}