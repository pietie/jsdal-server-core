using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using shortid;
using System.Text.RegularExpressions;
using jsdal_server_core.Changes;
using System.Text;
using jsdal_server_core.PluginManagement;
using System.Reflection;
using plugin = jsdal_plugin;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;

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

        public bool DisableMetadataCapturing;

        public string PullMetadataFromEndpointId { get; set; }

        [Newtonsoft.Json.JsonIgnore] private List<CachedRoutine> CachedRoutineList;

        [Newtonsoft.Json.JsonIgnore] public Application Application { get; private set; }

        public bool CaptureConnectionStats = true; // TODO: Default false? Provide way to turon on/off 

        [Newtonsoft.Json.JsonIgnore]
        public ConcurrentDictionary<string, string> CustomTypeLookupWithTypeScriptDef
        {
            get; private set;
        }

        public Endpoint()
        {
            this.CachedRoutineList = new List<CachedRoutine>();
            this.CustomTypeLookupWithTypeScriptDef = new ConcurrentDictionary<string, string>();
        }

        public void ShareMetadaFrom(Endpoint srcEndpoint)
        {
            if (srcEndpoint == null)
            {
                this.PullMetadataFromEndpointId = null;

                this.CachedRoutineList = new List<CachedRoutine>();
                WorkSpawner.RestartWorker(this);
            }
            else
            {
                PullMetadataFromEndpointId = srcEndpoint.Id;

                this.CachedRoutineList = srcEndpoint.CachedRoutineList;
            }
        }

        public void UpdateParentReferences(Application app)
        {
            this.Application = app;
        }

        public string GetBgTaskKey()
        {
            if (this.MetadataConnection == null) return null;
            var bgTaskKey = $"{this.MetadataConnection.DataSource.ToLower()}/{this.MetadataConnection.Port}/{this.MetadataConnection.InitialCatalog.ToLower()}.ORM_INSTALL";

            using (var md5 = ((System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.CryptoConfig.CreateFromName("MD5")))
            {
                return Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(bgTaskKey))).TrimEnd('=');
            }
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
                    return null;
                }

                return (string)cmd.Parameters["@err"].Value;
            }

        }
        public BackgroundWorker InstallOrm()
        {
            var missing = CheckForMissingOrmPreRequisitesOnDatabase();

            if (string.IsNullOrEmpty(missing))
            {
                return null;
            }

            var sqlScriptPath = Path.GetFullPath("./resources/install-orm.sql");
            var installSqlScript = File.ReadAllText(sqlScriptPath, System.Text.Encoding.UTF8);

            //https://stackoverflow.com/a/18597052
            var statements = Regex.Split(installSqlScript, @"^[\s]*GO[\s]*\d*[\s]*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase);

            var statementsToExec = statements.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim(' ', '\r', '\n'));

            using (var con = new SqlConnection())
            {
                con.ConnectionString = this.MetadataConnection.ConnectionStringDecrypted;
                con.Open();

                var trans = con.BeginTransaction();

                try
                {
                    foreach (var st in statementsToExec)
                    {
                        var cmd = new SqlCommand();

                        cmd.Connection = con;
                        cmd.Transaction = trans;
                        cmd.CommandType = System.Data.CommandType.Text;
                        cmd.CommandText = st;
                        cmd.CommandTimeout = 80;

                        cmd.ExecuteNonQuery();
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    if (trans != null) trans.Rollback();
                    SessionLog.Exception(ex);
                }

                con.Close();

                BackgroundWorker backgroundWorker = null;

                backgroundWorker = BackgroundTask.Queue($"{GetBgTaskKey()}.ORM_INIT", $"{Application.Project.Name}/{Application.Name}/{this.Name} ORM initilisation", () =>
                {
                    // try
                    // {
                    using (var conInit = new SqlConnection())
                    {
                        con.FireInfoMessageEventOnUserErrors = true;
                        conInit.ConnectionString = this.MetadataConnection.ConnectionStringDecrypted;
                        conInit.Open();

                        var cmdInit = new SqlCommand();

                        cmdInit.Connection = conInit;
                        cmdInit.CommandText = "ormv2.Init";
                        cmdInit.CommandTimeout = 600;

                        conInit.InfoMessage += (sender, e) =>
                        {
                            if (!backgroundWorker.IsDone && double.TryParse(e.Message, out var p))
                            {
                                backgroundWorker.Progress = p;
                                Hubs.BackgroundTaskMonitor.Instance.NotifyOfChange(backgroundWorker);
                            }
                        };

                        cmdInit.ExecuteScalar();


                        WorkSpawner.HandleOrmInstalled(this);
                        SettingsInstance.SaveSettingsToFile();

                        return true;
                    }
                    // }
                    // catch (Exception ex)
                    // {
                    //     return ex;
                    //     //return ex;
                    // }

                });

                return backgroundWorker;
            }

        }

        public bool UnInstallOrm()
        {
            using (var con = new SqlConnection())
            {
                con.ConnectionString = this.MetadataConnection.ConnectionStringDecrypted;
                con.Open();

                var cmd = new SqlCommand();

                cmd.Connection = con;
                cmd.CommandText = "exec ormV2.Uninstall";
                cmd.CommandTimeout = 120;

                cmd.ExecuteNonQuery();

                con.Close();
            }

            return true;
        }

        public CommonReturnValueWithApplication UpdateMetadataConnection(string dataSource, string catalog, string username, string password, int port, string applicationName, bool encrypt)
        {
            if (this.MetadataConnection == null) this.MetadataConnection = new Connection();

            this.MetadataConnection.Update(this, "metadata", dataSource, catalog, username, password, port, null/*instanceName*/, applicationName, encrypt);

            return CommonReturnValueWithApplication.success(null);
        }
        public CommonReturnValueWithApplication UpdateExecConnection(string dataSource, string catalog, string username, string password, int port, string applicationName, bool encrypt)
        {
            if (this.ExecutionConnection == null) this.ExecutionConnection = new Connection();

            this.ExecutionConnection.Update(this, "execution", dataSource, catalog, username, password, port, null/*instanceName*/, applicationName, encrypt);

            return CommonReturnValueWithApplication.success(null);
        }

        public void AddToCache(long maxRowDate, CachedRoutine newCachedRoutine, string lastUpdateByHostName, out ChangeDescriptor changeDescriptor)
        {
            changeDescriptor = null;

            if (this.Id == null) this.Id = ShortId.Generate();
            if (this.CachedRoutineList == null)
            {
                this.CachedRoutineList = new List<CachedRoutine>();
            }

            lock (CachedRoutineList)
            {
                // look for an existing item
                var existing = this.CachedRoutineList.Where(e => newCachedRoutine.Equals(e)).FirstOrDefault();

                if (existing != null)
                {
                    // if existing is not deleted but the update IS 
                    if (!existing.IsDeleted && newCachedRoutine.IsDeleted)
                    {
                        changeDescriptor = ChangeDescriptor.Create(lastUpdateByHostName, $"{newCachedRoutine.FullName} DROPPED");
                        this.CachedRoutineList.Remove(existing); // will be added again below
                    }
                    else if (existing.IsDeleted && newCachedRoutine.IsDeleted) // still deleted then nothing to do
                    {
                        return;
                    }
                    else if (existing.IsDeleted && !newCachedRoutine.IsDeleted)
                    {// "undeleted"
                        changeDescriptor = ChangeDescriptor.Create(lastUpdateByHostName, $"{newCachedRoutine.FullName} (RE)ADDED");
                        this.CachedRoutineList.Remove(existing); // will be added again below
                    }
                    else if (!newCachedRoutine.IsDeleted)
                    {
                        bool parametersUpdated = newCachedRoutine.ParametersHash != existing.ParametersHash;
                        bool resultSetsUpdated = newCachedRoutine.ResultSetHash != existing.ResultSetHash;
                        bool jsDALMetadataUpdated = false;

                        if (existing.jsDALMetadata == null && newCachedRoutine.jsDALMetadata != null)
                        {
                            jsDALMetadataUpdated = true;
                        }
                        else if (existing.jsDALMetadata != null && newCachedRoutine.jsDALMetadata == null)
                        {
                            jsDALMetadataUpdated = true;
                        }
                        else if (newCachedRoutine.jsDALMetadata != null)
                        {
                            var newMatchesExisting = newCachedRoutine.jsDALMetadata.Equals(existing.jsDALMetadata);
                            jsDALMetadataUpdated = newCachedRoutine.jsDALMetadata != null && !newMatchesExisting;
                        }

                        // no metadata related change
                        if (!parametersUpdated && !resultSetsUpdated && !jsDALMetadataUpdated) return;

                        this.CachedRoutineList.Remove(existing); // will be added again below

                        var applicableChanges = new List<string>();

                        if (parametersUpdated) applicableChanges.Add("PARAMETERS");
                        if (resultSetsUpdated) applicableChanges.Add("RESULT SETS");
                        if (jsDALMetadataUpdated) applicableChanges.Add("jsDAL metadata");

                        changeDescriptor = ChangeDescriptor.Create(lastUpdateByHostName, $"{newCachedRoutine.FullName} UPDATED {string.Join(", ", applicableChanges.ToArray())}");
                    }
                }
                else
                {
                    changeDescriptor = ChangeDescriptor.Create(lastUpdateByHostName, $"{newCachedRoutine.FullName} ADDED");
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

        // called after Endpoint is deserialized from the Settings json 
        public async void AfterDeserializationInit()
        {
            if (!string.IsNullOrEmpty(this.PullMetadataFromEndpointId))
            {
                var srcEndPoint = Settings.SettingsInstance.Instance.FindEndpointById(this.PullMetadataFromEndpointId);

                if (srcEndPoint != null)
                {
                    var cachedRoutineList = await srcEndPoint._cachedRoutinesPromise.Task;
                    this.CachedRoutineList = cachedRoutineList;
                }
                else
                {
                    SessionLog.Error($"Endpoint {this.Pedigree} is configured to pull metadata from an endpoint with id {this.PullMetadataFromEndpointId} but that endpoint does not exist.");
                }
            }
            else
            {
                await this.LoadCacheAsync();
            }

            if (this.ExecutionConnection != null)
            {
                this.ExecutionConnection.Endpoint = this;
                this.ExecutionConnection.Type = "execution";
            }

            if (this.MetadataConnection != null)
            {
                this.MetadataConnection.Endpoint = this;
                this.MetadataConnection.Type = "metadata";
            }
        }

        private TaskCompletionSource<List<CachedRoutine>> _cachedRoutinesPromise = new TaskCompletionSource<List<CachedRoutine>>();

        public async Task LoadCacheAsync()
        {
            try
            {
                string cachePath = "./cache";
                string cacheFilePath = Path.Combine(cachePath, this.CacheFilename);

                if (!File.Exists(cacheFilePath)) return;

                this.CachedRoutineList = new List<CachedRoutine>();

                var data = await File.ReadAllTextAsync(cacheFilePath, System.Text.Encoding.UTF8);

                var allCacheEntries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CachedRoutine>>(data/*, new BoolJsonConverter()*/);

                //var options = new System.Text.Json.JsonSerializerOptions() { IncludeFields = true/*, Converters = { new InternedStringConverter() }*/ };
                //var allCacheEntries = System.Text.Json.JsonSerializer.Deserialize<List<CachedRoutine>>(data, options);

                this.CachedRoutineList = allCacheEntries;

                // pre-calc to build up initial CustomTypeLookupWithTypeScriptDef
                this.CachedRoutineList.AsParallel().ForAll(r =>
                {
                    try
                    {
                        r.PrecalculateJsGenerationValues(this);
                    }
                    catch (Exception ee)
                    {
                        r.PrecalcError = ee.ToString();
                    }
                });

                _cachedRoutinesPromise.TrySetResult(this.CachedRoutineList);
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
            }
        }

        //[Newtonsoft.Json.JsonIgnore]
        // public object _cacheLock = new object();
        public async Task SaveCacheAsync()
        {
            //lock (_cacheLock)
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
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(this.CachedRoutineList);

                await File.WriteAllTextAsync(cacheFilePath, json, System.Text.Encoding.UTF8);
            }
        }

        [Newtonsoft.Json.JsonIgnore]
        public List<CachedRoutine> CachedRoutines { get { return this.CachedRoutineList; } }

        public bool ClearCache()
        {
            try
            {
                var cachePath = "./cache";

                if (!Directory.Exists(cachePath)) return true;

                var cacheFilePath = Path.Combine(cachePath, this.CacheFilename);

                if (!File.Exists(cacheFilePath)) return true;

                this.CachedRoutineList.Clear();

                File.Delete(cacheFilePath);

                WorkSpawner.ResetMaxRowDate(this);
                this.LastUpdateDate = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return false;
            }

        }

        public Connection GetSqlConnection()
        {
            if (this.ExecutionConnection == null)
            {
                SessionLog.Error($"Execution connection not found on endpoint '{this.Name}'({this.Id}).");
            }

            return this.ExecutionConnection;
        }

        [Newtonsoft.Json.JsonIgnore]
        public string OutputDir
        {
            get
            {
                return Path.GetFullPath($"./generated/{this.Application.Project.Name}/{this.Application.Name}/{this.Name}");
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

        [Newtonsoft.Json.JsonIgnore]
        public string Pedigree
        {
            get
            {
                return (this.Application?.Project?.Name + "/" ?? "") + (this.Application?.Name + "/" ?? "") + this.Name;
            }
        }

        private Dictionary<string, plugin.ServerMethodPlugin> ServerMethodInstanceCache = new Dictionary<string, plugin.ServerMethodPlugin>();
        public (plugin.ServerMethodPlugin, ServerMethodRegistrationMethod/*matched Method*/, string/*error*/) GetServerMethodPluginInstance(string nameSpace,
            string methodName, Dictionary<string, string> inputParameters)
        {
            // find all registered ServerMethods for this app
            var registrations = ServerMethodManager.GetRegistrationsForApp(this.Application);

            // TODO: To support overloading we need to match name + best fit parameter list
            var methodCandidates = registrations.SelectMany(reg => reg.Methods)
                        .Where(m => ((nameSpace == null && m.Namespace == null) || (m.Namespace?.Equals(nameSpace, StringComparison.Ordinal) ?? false)) && m.Name.Equals(methodName, StringComparison.Ordinal))
                        .Select(m => m);

            if (methodCandidates.Count() == 0) return (null, null, "Method name not found.");

            var weightedMethodList = new List<(decimal/*weight*/, string/*error*/, ServerMethodRegistrationMethod)>();

            // find the best matching overload (if any)
            foreach (var regMethod in methodCandidates)
            {
                var methodParameters = regMethod.AssemblyMethodInfo.GetParameters();

                if (inputParameters.Count > methodParameters.Length)
                {
                    weightedMethodList.Add((1M, "Too many parameters specified", regMethod));
                    continue;
                }

                var joined = from methodParam in methodParameters
                             join inputParam in inputParameters on methodParam.Name equals inputParam.Key into grp
                             from parm in grp.DefaultIfEmpty()
                             select new { HasMatch = parm.Key != null, Param = methodParam };

                var matched = joined.Where(e => e.HasMatch);
                var notmatched = joined.Where(e => !e.HasMatch);


                var expectedCnt = methodParameters.Count();
                var matchedCnt = matched.Count();

                // out/ref/optional parameters are added as extra credit below (but does not contribute to actual weight)
                var outRefSum = (from p in joined
                                 where (p.Param.IsOut || p.Param.IsOptional || p.Param.ParameterType.IsByRef) && !p.HasMatch
                                 select 1.0M).Sum();

                // same with parameters that requested injection
                var toBeInjectedParameters = methodParameters.Where(p=>p.GetCustomAttributes(typeof(jsdal_plugin.InjectParamAttribute), false).Count() > 0).ToList();

                if (matchedCnt == expectedCnt || matchedCnt + outRefSum + toBeInjectedParameters.Count == expectedCnt)
                {
                    weightedMethodList.Add((matchedCnt, null, regMethod));
                }
                else
                {
                    //weightedMethodList.Add((matchedCnt, $"Following parameters not specified: {string.Join("\r\n", notmatched.Select(nm => nm.Param.Name))}", regMethod));
                    weightedMethodList.Add((matchedCnt, "Parameter mismatch", regMethod));
                }
            }

            var bestMatch = weightedMethodList.OrderByDescending(k => k.Item1).FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(bestMatch.Item2))
            {
                var parms = bestMatch.Item3.AssemblyMethodInfo.GetParameters();
                var parmDesc = "(no parameters)";
                if (parms.Length > 0)
                {
                    parmDesc = string.Join("\r\n", parms.Select(p => $"{p.Name} ({p.ParameterType.ToString()})")); // TODO: Provide "easy to read" description for type, e.g. nullabe Int32 can be something like 'int?' and 'List<string>' just 'string[]'
                }

                return (null, bestMatch.Item3, $"Failed to find suitable overload.\r\nError: {bestMatch.Item2}\r\nBest match requires parameters:\r\n{parmDesc}");
            }

            var matchedRegMethod = bestMatch.Item3;

            var cacheKey = $"{matchedRegMethod.Registration.PluginAssemblyInstanceId}; {matchedRegMethod.Registration.TypeInfo.FullName}";

            plugin.ServerMethodPlugin pluginInstance = null;

            lock (ServerMethodInstanceCache)
            {
                if (ServerMethodInstanceCache.ContainsKey(cacheKey))
                {
                    pluginInstance = ServerMethodInstanceCache[cacheKey];
                }
                else // instantiate a new instance
                {
                    try
                    {
                        pluginInstance = (plugin.ServerMethodPlugin)matchedRegMethod.Registration.Assembly.CreateInstance(matchedRegMethod.Registration.TypeInfo.FullName);
                        var initMethod = typeof(plugin.ServerMethodPlugin).GetMethod("InitSM", BindingFlags.Instance | BindingFlags.NonPublic);

                        if (initMethod != null)
                        {
                            initMethod.Invoke(pluginInstance, new object[] {
                                new Func<SqlConnection>(()=>{
                                    if (this.ExecutionConnection != null)
                                    {
                                        var con = new SqlConnection(this.ExecutionConnection.ConnectionStringDecrypted);

                                        con.Open();

                                        return con;
                                    }

                                    return new SqlConnection();
                                })});
                        }
                        else
                        {
                            SessionLog.Warning($"Failed to find InitSM method on plugin {matchedRegMethod.Registration.TypeInfo.FullName} from assembly {matchedRegMethod.Registration.Assembly.FullName}. Make sure the correct version of the jsdal plugin is used and that you derive from the correct base class (should be ServerMethodPlugin).");
                        }

                        var setGetServicesFuncMethod = typeof(plugin.PluginBase).GetMethod("SetGetServicesFunc", BindingFlags.Instance | BindingFlags.NonPublic);

                        if (setGetServicesFuncMethod != null)
                        {
                            setGetServicesFuncMethod.Invoke(pluginInstance, new object[] { new Func<Type, plugin.PluginService>(serviceType =>
                            {
                                if (serviceType == typeof(plugin.BlobStoreBase))
                                {
                                    return BlobStore.Instance;
                                }

                                return null;
                            })});
                        }

                        ServerMethodManager.RegisterInstanceUse(this, matchedRegMethod);
                        ServerMethodInstanceCache.Add(cacheKey, pluginInstance);
                    }
                    catch (Exception ex)
                    {
                        SessionLog.Error($"Failed to instantiate plugin {matchedRegMethod.Registration.TypeInfo.FullName} from assembly {matchedRegMethod.Registration.Assembly.FullName}. See exception that follows.");
                        SessionLog.Exception(ex);
                    }
                }
            } // lock

            return (pluginInstance, matchedRegMethod, null);
        }

        public void HandleAssemblyUpdated(string assemblyInstanceId)
        {
            var startsWith = $"{assemblyInstanceId}";

            lock (ServerMethodInstanceCache)
            {
                var keysToRemove = ServerMethodInstanceCache.Keys.Where(k => k.StartsWith(startsWith)).ToList();

                foreach (var k in keysToRemove)
                {
                    ServerMethodInstanceCache.Remove(k);
                }
            }
        }

    }

    public class InternedStringConverter : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override string Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            var s = reader.GetString();

            if (s != null) return string.Intern(s);
            else return null;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }


    public class BoolJsonConverter : Newtonsoft.Json.JsonConverter
    {
        public BoolJsonConverter()
        {
        }

        public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
        {
            throw new NotImplementedException("!!");
            // JToken t = JToken.FromObject(value);

            // if (t.Type != JTokenType.Object)
            // {
            //     t.WriteTo(writer);
            // }
            // else
            // {
            //     JObject o = (JObject)t;
            //     IList<string> propertyNames = o.Properties().Select(p => p.Name).ToList();

            //     o.AddFirst(new JProperty("Keys", new JArray(propertyNames)));

            //     o.WriteTo(writer);
            // }
        }

        public override object ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (objectType == typeof(bool)) { return (bool)existingValue; }
            else if (objectType == typeof(string))
            {
                var str = (string)existingValue;
                return str.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || str.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || str.Equals("okay", StringComparison.OrdinalIgnoreCase);
            }
            else return existingValue;
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(bool);
        }
    }


}