using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Data.SqlClient;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using shortid;

namespace jsdal_server_core.Settings.ObjectModel
{

    public enum DefaultRuleMode
    {
        IncludeAll = 0,
        ExcludeAll = 1
    }

    public class DatabaseSource
    {

        //?        private SqlConnectionStringBuilder _connectionStringBuilder;

        public string Name;
        public string CacheKey;
        public string WhitelistedDomainsCsv;
        public bool WhitelistAllowAllPrivateIPs;
        public string JsNamespace;
        public bool IsOrmInstalled;
        public int DefaultRuleMode;
        public DateTime LastUpdateDate;
        public List<string> Plugins;

        public List<JsFile> JsFiles;
        public List<BaseRule> Rules;

        public Connection MetadataConnection;
        public List<Connection> ExecutionConnections;

        [JsonIgnore] private List<CachedRoutine> CachedRoutineList;


        public DatabaseSource()
        {
            this.JsFiles = new List<JsFile>();
            this.Rules = new List<BaseRule>();
            this.ExecutionConnections = new List<Connection>();

            this.CachedRoutineList = new List<CachedRoutine>();
        }

        [JsonIgnore]
        public string userID
        {
            get
            {
                return this.MetadataConnection.userID;
            }
        }

        [JsonIgnore]
        public string password
        {
            get
            {
                return this.MetadataConnection.password;
            }
        }

        [JsonIgnore]
        public string dataSource
        {
            get
            {
                return this.MetadataConnection.dataSource;
            }
        }

        [JsonIgnore]
        public string initialCatalog
        {
            get
            {
                return this.MetadataConnection.initialCatalog;
            }
        }

        [JsonIgnore]
        public bool integratedSecurity
        {
            get
            {
                return this.MetadataConnection.integratedSecurity;
            }
        }

        [JsonIgnore]
        public int port
        {
            get
            {
                return this.MetadataConnection.port;
            }
        }

        [JsonIgnore]
        public string instanceName
        {
            get
            {
                return this.MetadataConnection.instanceName;
            }
        }

        public void addToCache(long maxRowDate, CachedRoutine newCachedRoutine)
        {
            if (this.CacheKey == null) this.CacheKey = ShortId.Generate();

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

        public void loadCache()
        {
            try
            {
                string cachePath = "./cache";
                string cacheFilePath = Path.Combine(cachePath, $"{this.CacheKey}.json");

                if (!File.Exists(cacheFilePath)) return;

                this.CachedRoutineList = new List<CachedRoutine>();

                var data = File.ReadAllText(cacheFilePath, System.Text.Encoding.UTF8);

                var allCacheEntries = JsonConvert.DeserializeObject<List<CachedRoutine>>(data);

                //this.CachedRoutineList.AddRange(allCacheEntries);
                this.CachedRoutineList = allCacheEntries;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        public void saveCache()
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
                        // TODO: Log
                    }
                }

                var cacheFilePath = Path.Combine(cachePath, $"{this.CacheKey}.json");
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

        public CommonReturnValueWithDbSource addUpdateDatabaseConnection(bool isMetadataConnection, string dbConnectionGuid, string logicalName, string dataSource
            , string catalog, string username, string password, int port, string instanceName)
        {
            if (string.IsNullOrWhiteSpace(logicalName)) return CommonReturnValueWithDbSource.userError("Please provide a name for this data source.");

            if (isMetadataConnection)
            {
                if (this.MetadataConnection == null) this.MetadataConnection = new Connection();

                this.MetadataConnection.update(logicalName, dataSource, catalog, username, password, port, instanceName);
            }
            else
            {
                if (this.ExecutionConnections == null) this.ExecutionConnections = new List<Connection>();

                if (dbConnectionGuid == null)
                {
                    // add new
                    var connection = new Connection();

                    connection.update(logicalName, dataSource, catalog, username, password, port, instanceName);
                    connection.Guid = ShortId.Generate(); // TODO: Needs to move into constructor of Connection or something like Connection.create(..).

                    this.ExecutionConnections.Add(connection);
                }
                else
                { // update

                    var existing = this.ExecutionConnections.FirstOrDefault(c => c.Guid == dbConnectionGuid);

                    if (existing == null)
                    {
                        return CommonReturnValueWithDbSource.userError("The specified connection does not exist and cannot be updated.");
                    }

                    existing.update(logicalName, dataSource, catalog, username, password, port, instanceName);

                }
            }

            return CommonReturnValueWithDbSource.success(null);
        }

        public CommonReturnValue deleteDatabaseConnection(string dbConnectionGuid)
        {
            var existing = this.ExecutionConnections.FirstOrDefault(c => c.Guid == dbConnectionGuid);

            if (existing == null)
            {
                return CommonReturnValue.userError("The specified connection does not exist and cannot be updated.");
            }

            this.ExecutionConnections.Remove(existing);

            return CommonReturnValue.success();
        }

        public string checkForMissingOrmPreRequisitesOnDatabase()
        {
            var sqlScript = File.ReadAllText("./resources/check-pre-requisites.sql", System.Text.Encoding.UTF8);

            using (SqlConnection con = new SqlConnection(this.MetadataConnection.ConnectionStringDecrypted))
            {
                SqlCommand cmd = new SqlCommand();

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
            var missing = checkForMissingOrmPreRequisitesOnDatabase();

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

        public void clearCache()
        {

            try
            {
                var cachePath = "./cache";

                if (!Directory.Exists(cachePath)) return;

                var cacheFilePath = Path.Combine(cachePath, $"{this.CacheKey}.json");

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

        public CommonReturnValue updatePluginList(dynamic pluginList)
        {
            this.Plugins = new List<string>();
            if (pluginList == null) return CommonReturnValue.success();


            foreach (Newtonsoft.Json.Linq.JObject p in pluginList)
            {
                bool included = (bool)p["Included"];
                Guid g = (Guid)p["Guid"];
                if (included) this.Plugins.Add(g.ToString());
            };

            return CommonReturnValue.success();
        }

        public bool isPluginIncluded(string guid)
        {
            if (this.Plugins == null) return false;

            return this.Plugins.FirstOrDefault(g => g.Equals(guid, StringComparison.OrdinalIgnoreCase)) != null;
        }

        public CommonReturnValue addJsFile(string name)
        {
            if (this.JsFiles == null) this.JsFiles = new List<JsFile>();

            var existing = this.JsFiles.FirstOrDefault(f => f.Filename.ToLower() == name.ToLower());

            if (existing != null)
            {
                return CommonReturnValue.userError($"The output file '{name}' already exists against this data source.");
            }

            var jsfile = new JsFile();

            jsfile.Filename = name;
            jsfile.Guid = ShortId.Generate();

            this.JsFiles.Add(jsfile);

            return CommonReturnValue.success();
        }

        public CommonReturnValue addRule(RuleType ruleType, string txt)
        {
            BaseRule rule = null;

            switch (ruleType) // TODO: Check for duplicated rules?
            {
                case RuleType.Schema:
                    rule = new SchemaRule(txt);
                    break;
                case RuleType.Specific:
                    {
                        var parts = txt.Split('.');
                        var schema = "dbo";
                        var name = txt;

                        if (parts.Length > 1)
                        {
                            schema = parts[0];
                            name = parts[1];
                        }

                        rule = new SpecificRule(schema, name);
                    }
                    break;
                case RuleType.Regex:
                    {
                        try
                        {
                            var regexTest = new Regex(txt);
                        }
                        catch (Exception ex)
                        {
                            return CommonReturnValue.userError("Invalid regex pattern: " + ex.ToString());
                        }
                    }
                    rule = new RegexRule(txt);
                    break;
                default:
                    throw new Exception($"Unsupported rule type:${ ruleType}");
            }

            rule.Guid = ShortId.Generate();

            this.Rules.Add(rule);

            return CommonReturnValue.success();
        }

        public CommonReturnValue deleteRule(string ruleGuid)
        {
            var existingRule = this.Rules.FirstOrDefault(r =>/*r!=null &&*/ r.Guid == ruleGuid);

            if (existingRule == null)
            {
                return CommonReturnValue.userError("The specified rule was not found.");
            }

            //!this.Rules.splice(this.Rules.indexOf(existingRule), 1);
            this.Rules.Remove(existingRule);

            return CommonReturnValue.success();
        }

        public void applyDbLevelRules()
        {
            this.applyRules(JsFile.DBLevel);
        }

        public void applyRules(JsFile jsFileContext)
        {
            if (this.CachedRoutineList == null) return;

            foreach (var routine in this.CachedRoutineList)
            {
                if (routine.RuleInstructions == null) continue;
                if (routine.RuleInstructions.Count == 1  && routine.RuleInstructions.First().Key == null)
                         continue; // PL: No idea why this happens but when no rules exist RuleInstructions contains a single KeyValue pair that are both null...this causes routine.RuleInstructions[jsFileContext] to hang 

                routine.RuleInstructions[jsFileContext] = null;

                if (routine.IsDeleted) continue;

                var instruction = routine.applyRules(this, jsFileContext);

                routine.RuleInstructions[jsFileContext] = instruction;

            };
        }

        public CommonReturnValue mayAccessDbSource(Microsoft.AspNetCore.Http.HttpRequest req)
        {
            if (this.WhitelistedDomainsCsv == null)
            {
                return CommonReturnValue.userError("No access list exists.");
            }

            var referer = req.Headers["Referer"];
            var host = req.Host.Host;
            var whitelistedIPs = this.WhitelistedDomainsCsv.Split(',');

            foreach (string en in whitelistedIPs)
            {
                if (en.Equals(host, StringComparison.OrdinalIgnoreCase))
                {
                    return CommonReturnValue.success();
                }
            };

            return CommonReturnValue.userError($"The host({ host}) is not allowed to access this resource.");
        }

        public Connection getSqlConnection(string dbConnectionGuid)
        {
            Connection decryptedConnection;

            if (string.IsNullOrWhiteSpace(dbConnectionGuid))
            {
                decryptedConnection = this.MetadataConnection;
            }
            else
            {

                var dbConnection = this.ExecutionConnections.FirstOrDefault(con => con.Guid == dbConnectionGuid);

                if (dbConnection != null)
                {
                    decryptedConnection = dbConnection;
                }
                else
                {
                    SessionLog.Error($"The execution connection '{dbConnectionGuid}' not found in specified DB Source '{this.Name}'({ this.CacheKey}). Reverting to metadata connection.");
                    decryptedConnection = this.MetadataConnection;
                }
            }

            return decryptedConnection;
            // return new
            // {
            //     user = decryptedConnection.userID,
            //     password = decryptedConnection.password,
            //     server = decryptedConnection.dataSource,
            //     database = decryptedConnection.initialCatalog,
            //     port = decryptedConnection.port,
            //     instanceName = decryptedConnection.instanceName
            // };
        }


        public string outputDir
        {
            get
            {
                return Path.GetFullPath($"./generated/{ this.CacheKey}");
            }
        }

        public string outputFilePath(JsFile jsFile)
        {
            return Path.Combine(this.outputDir, jsFile.Filename);
        }

        public string outputTypeScriptTypingsFilePath(JsFile jsFile)
        {
            return Path.Combine(this.outputDir, jsFile.Filename.Substring(0, jsFile.Filename.LastIndexOf('.')) + ".d.ts");
        }

        public string minifiedOutputFilePath(JsFile jsFile)
        {
            return Path.Combine(this.outputDir, jsFile.Filename.Substring(0, jsFile.Filename.Length - 3) + ".min.js");
        }
    }
}
