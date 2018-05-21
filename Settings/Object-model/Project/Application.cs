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

    public class Application
    {

        public string Name;

        public string WhitelistedDomainsCsv;
        public bool WhitelistAllowAllPrivateIPs;
        public string JsNamespace;

        public int DefaultRuleMode;

        public List<string> Plugins;

        public List<JsFile> JsFiles;
        public List<BaseRule> Rules;

        public List<Endpoint> Endpoints;

        [JsonIgnore] public Project Project { get; private set; }

        public Application()
        {
            this.Endpoints = new List<Endpoint>();
            this.JsFiles = new List<JsFile>();
            this.Rules = new List<BaseRule>();
        }

        public void UpdateParentReferences(Project project)
        {
            this.Project = project;

            if (this.Endpoints != null)
            {
                this.Endpoints.ForEach(ep => ep.UpdateParentReferences(this));
            }
        }


        public CommonReturnValue AddEndpoint(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return CommonReturnValue.userError("Please specify a valid endpoint name.");

            name = name.Trim();

            if (this.Endpoints.FirstOrDefault(ep => ep.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) != null)
            {
                return CommonReturnValue.userError($"An endpoint with the name '{name}' already exists on the current data source.");
            }

            this.Endpoints.Add(new Endpoint() { Name = name });

            return CommonReturnValue.success();
        }

        public CommonReturnValue UpdateEndpoint(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return CommonReturnValue.userError("Please specify a valid endpoint name.");

            var existing = this.Endpoints.FirstOrDefault(ep => ep.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));

            if (existing == null) return CommonReturnValue.userError($"The endpoint '{oldName}' does not exists on the datasource '{this.Name}'");

            newName = newName.Trim();

            existing.Name = newName;

            return CommonReturnValue.success();
        }

        public bool GetEndpoint(string name, out Endpoint endpoint, out CommonReturnValue resp) // TODO: Review use of CommonReturnValue here 
        {
            resp = null;
            endpoint = this.Endpoints.FirstOrDefault(ep => ep.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (endpoint == null) resp = CommonReturnValue.userError($"The endpoint '{name}' does not exists on the datasource '{this.Name}'");
            else resp = CommonReturnValue.success();

            return endpoint != null;
        }

        public CommonReturnValue DeleteEndpoint(string name)
        {
            var existing = this.Endpoints.FirstOrDefault(ep => ep.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (existing == null) return CommonReturnValue.userError($"The endpoint '{name}' does not exists on the datasource '{this.Name}'");

            this.Endpoints.Remove(existing);

            return CommonReturnValue.success();
        }



        // // public CommonReturnValueWithDbSource addUpdateDatabaseConnection(bool isMetadataConnection, string dbConnectionGuid, string logicalName, string dataSource
        // //     , string catalog, string username, string password, int port, string instanceName)
        // // {
        // //     if (string.IsNullOrWhiteSpace(logicalName)) return CommonReturnValueWithDbSource.userError("Please provide a name for this data source.");

        // //     if (isMetadataConnection)
        // //     {
        // //         if (this.MetadataConnection == null) this.MetadataConnection = new Connection();

        // //         this.MetadataConnection.update(logicalName, dataSource, catalog, username, password, port, instanceName);
        // //     }
        // //     else
        // //     {
        // //         if (this.ExecutionConnections == null) this.ExecutionConnections = new List<Connection>();

        // //         if (dbConnectionGuid == null)
        // //         {
        // //             // add new
        // //             var connection = new Connection();

        // //             connection.update(logicalName, dataSource, catalog, username, password, port, instanceName);
        // //             connection.Guid = ShortId.Generate(); // TODO: Needs to move into constructor of Connection or something like Connection.create(..).

        // //             this.ExecutionConnections.Add(connection);
        // //         }
        // //         else
        // //         { // update

        // //             var existing = this.ExecutionConnections.FirstOrDefault(c => c.Guid == dbConnectionGuid);

        // //             if (existing == null)
        // //             {
        // //                 return CommonReturnValueWithDbSource.userError("The specified connection does not exist and cannot be updated.");
        // //             }

        // //             existing.update(logicalName, dataSource, catalog, username, password, port, instanceName);

        // //         }
        // //     }

        // //     return CommonReturnValueWithDbSource.success(null);
        // // }

        // // public CommonReturnValue deleteDatabaseConnection(string dbConnectionGuid)
        // // {
        // //     var existing = this.ExecutionConnections.FirstOrDefault(c => c.Guid == dbConnectionGuid);

        // //     if (existing == null)
        // //     {
        // //         return CommonReturnValue.userError("The specified connection does not exist and cannot be updated.");
        // //     }

        // //     this.ExecutionConnections.Remove(existing);

        // //     return CommonReturnValue.success();
        // // }

        // TODO: Cleanup. Remove items that have been moved down to Endpoint level.
        // // public string checkForMissingOrmPreRequisitesOnDatabase()
        // // {
        // //     var sqlScript = File.ReadAllText("./resources/check-pre-requisites.sql", System.Text.Encoding.UTF8);

        // //     using (SqlConnection con = new SqlConnection(this.MetadataConnection.ConnectionStringDecrypted))
        // //     {
        // //         SqlCommand cmd = new SqlCommand();

        // //         cmd.Connection = con;
        // //         cmd.CommandType = System.Data.CommandType.Text;
        // //         cmd.CommandText = sqlScript;
        // //         cmd.Parameters.Add("@err", System.Data.SqlDbType.VarChar, int.MaxValue).Direction = System.Data.ParameterDirection.Output;

        // //         con.Open();

        // //         cmd.ExecuteNonQuery();

        // //         if (cmd.Parameters["@err"].Value == DBNull.Value)
        // //         {
        // //             // TODO: Can the validity be cached somehow?
        // //             return null;
        // //         }

        // //         return (string)cmd.Parameters["@err"].Value;
        // //     }

        // // }
        // // public bool InstallOrm()
        // // {
        // //     var missing = checkForMissingOrmPreRequisitesOnDatabase();

        // //     if (string.IsNullOrEmpty(missing))
        // //     {
        // //         return true;
        // //     }

        // //     var installSqlScript = File.ReadAllText("./resources/install-orm.sql", System.Text.Encoding.UTF8);

        // //     using (var con = new SqlConnection())
        // //     {
        // //         con.ConnectionString = this.MetadataConnection.ConnectionStringDecrypted;
        // //         con.Open();

        // //         var cmd = new SqlCommand();

        // //         cmd.Connection = con;
        // //         cmd.CommandText = installSqlScript;
        // //         cmd.CommandTimeout = 120;

        // //         cmd.ExecuteNonQuery();

        // //         con.Close();
        // //     }

        // //     return true;

        // // }

        // // public bool UnInstallOrm()
        // // {
        // //     using (var con = new SqlConnection())
        // //     {
        // //         con.ConnectionString = this.MetadataConnection.ConnectionStringDecrypted;
        // //         con.Open();

        // //         var cmd = new SqlCommand();

        // //         cmd.Connection = con;
        // //         cmd.CommandText = "exec orm.Uninstall";
        // //         cmd.CommandTimeout = 120;

        // //         cmd.ExecuteNonQuery();

        // //         con.Close();
        // //     }

        // //     return true;

        // // }


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
            throw new NotImplementedException();
            //TODO: MOVE TO CORRECT LEVEL..END POINT??? dont think endpoint level but across files...ugh
            /*******
            if (this.CachedRoutineList == null) return;

            foreach (var routine in this.CachedRoutineList)
            {
                if (routine.RuleInstructions == null) continue;
                if (routine.RuleInstructions.Count == 1 && routine.RuleInstructions.First().Key == null)
                    continue; // PL: No idea why this happens but when no rules exist RuleInstructions contains a single KeyValue pair that are both null...this causes routine.RuleInstructions[jsFileContext] to hang 

                routine.RuleInstructions[jsFileContext] = null;

                if (routine.IsDeleted) continue;

                var instruction = routine.applyRules(this, jsFileContext);

                routine.RuleInstructions[jsFileContext] = instruction;

            };

            */
        }

        public CommonReturnValue mayAccessDbSource(Microsoft.AspNetCore.Http.HttpRequest req)
        {
            if (this.WhitelistedDomainsCsv == null)
            {
                return CommonReturnValue.userError("No access list exists.");
            }

            var referer = req.Headers["Referer"].FirstOrDefault();
            //var host = req.Host.Host;
            var whitelistedIPs = this.WhitelistedDomainsCsv.Split(',');

            if (referer != null)
            {
                if (System.Uri.TryCreate(referer, UriKind.RelativeOrAbsolute, out var refererUri))
                {
                    
                    foreach (string en in whitelistedIPs)
                    {
                        if (en.Equals(refererUri.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            return CommonReturnValue.success();
                        }
                    }
                }
            }

            return CommonReturnValue.userError($"The host ({ referer }) is not allowed to access this resource.");
        }



    }
}
