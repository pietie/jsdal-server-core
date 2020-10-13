﻿using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using shortid;
using jsdal_server_core.PluginManagement;
using System.Text;

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

        public List<string> Plugins; // TODO: Need to build this out for Access-list functionality of some sort? -->Specifically for the Server methods and SignalR loops and such?
                                     //   public List<string> InlinePlugins;

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

        public void AfterDeserializationInit()
        {
            if (this.Endpoints != null)
            {
                this.Endpoints.ForEach(ep => ep.AfterDeserializationInit());
            }

            if (this.JsFiles != null)
            {
                this.JsFiles.ForEach(js => js.AfterDeserializationInit(this.Endpoints));
            }
        }

        public void UpdateParentReferences(Project project)
        {
            this.Project = project;

            if (this.Endpoints != null)
            {
                this.Endpoints.ForEach(ep => ep.UpdateParentReferences(this));
            }
        }

        public CommonReturnValue Update(string name, string jsNamespace, int? defaultRuleMode)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                return CommonReturnValue.UserError("Application names may not be empty or contain special characters. Valid characters include A to Z and 0 to 9.");
            }

            if (!defaultRuleMode.HasValue)
            {
                return CommonReturnValue.UserError("Please specify the default rule mode.");
            }


            this.Name = name;
            this.JsNamespace = jsNamespace;
            this.DefaultRuleMode = defaultRuleMode.Value;

            return CommonReturnValue.Success();
        }


        public CommonReturnValue AddEndpoint(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return CommonReturnValue.UserError("Please specify a valid endpoint name.");

            name = name.Trim();

            if (this.Endpoints.FirstOrDefault(ep => ep.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) != null)
            {
                return CommonReturnValue.UserError($"An endpoint with the name '{name}' already exists on the current data source.");
            }

            var newEP = new Endpoint() { Name = name };
            newEP.UpdateParentReferences(this);
            this.Endpoints.Add(newEP);

            return CommonReturnValue.Success();
        }
        public CommonReturnValue UpdateEndpoint(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return CommonReturnValue.UserError("Please specify a valid endpoint name.");

            var existing = this.Endpoints.FirstOrDefault(ep => ep.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));

            if (existing == null) return CommonReturnValue.UserError($"The endpoint '{oldName}' does not exists on the datasource '{this.Name}'");

            newName = newName.Trim();

            existing.Name = newName;

            return CommonReturnValue.Success();
        }

        public bool GetEndpoint(string name, out Endpoint endpoint, out CommonReturnValue resp) // TODO: Review use of CommonReturnValue here 
        {
            resp = null;
            endpoint = this.Endpoints.FirstOrDefault(ep => ep.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (endpoint == null) resp = CommonReturnValue.UserError($"The endpoint '{name}' does not exists on the datasource '{this.Name}'");
            else resp = CommonReturnValue.Success();

            return endpoint != null;
        }

        public CommonReturnValue DeleteEndpoint(string name)
        {
            var existing = this.Endpoints.FirstOrDefault(ep => ep.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (existing == null) return CommonReturnValue.UserError($"The endpoint '{name}' does not exists on the datasource '{this.Name}'");

            this.Endpoints.Remove(existing);

            return CommonReturnValue.Success();
        }

        public CommonReturnValue UpdatePluginList(dynamic pluginList)
        {
            this.Plugins = new List<string>();
            if (pluginList == null) return CommonReturnValue.Success();


            foreach (Newtonsoft.Json.Linq.JObject p in pluginList)
            {
                bool included = (bool)p["Included"];
                Guid g = (Guid)p["Guid"];
                if (included) this.Plugins.Add(g.ToString());
            };

            return CommonReturnValue.Success();
        }

        public bool IsPluginIncluded(string guid)
        {
            if (this.Plugins == null) return false;

            return this.Plugins.FirstOrDefault(g => g.Equals(guid, StringComparison.OrdinalIgnoreCase)) != null;
        }

        public CommonReturnValue AddJsFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                return CommonReturnValue.UserError("Filenames may not be empty or contain special characters. Valid characters include A to Z and 0 to 9.");
            }

            if (this.JsFiles == null) this.JsFiles = new List<JsFile>();

            if (!name.ToLower().Trim().EndsWith(".js")) name = name.Trim() + ".js";

            var existing = this.JsFiles.FirstOrDefault(f => f.Filename.ToLower() == name.ToLower());

            if (existing != null)
            {
                return CommonReturnValue.UserError($"The output file '{name}' already exists against this data source.");
            }

            var jsfile = new JsFile();

            jsfile.Filename = name;
            jsfile.Id = ShortId.Generate();

            this.JsFiles.Add(jsfile);

            return CommonReturnValue.Success();
        }

        public CommonReturnValue AddRule(RuleType ruleType, string txt)
        {
            BaseRule rule = null;

            switch (ruleType) // TODO: Check for duplicated rules?
            {
                case RuleType.Schema:
                    rule = new SchemaRule(txt);
                    break;
                case RuleType.Specific:
                    {
                        rule = SpecificRule.FromFullname(txt);
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
                            return CommonReturnValue.UserError("Invalid regex pattern: " + ex.ToString());
                        }
                    }
                    rule = new RegexRule(txt);
                    break;
                default:
                    throw new Exception($"Unsupported rule type:${ ruleType}");
            }

            rule.Id = ShortId.Generate(useNumbers: true, useSpecial: true, length: 6);

            this.Rules.Add(rule);

            return CommonReturnValue.Success();
        }

        public CommonReturnValue UpdateRule(string ruleId, string txt)
        {
            var existingRule = this.Rules.FirstOrDefault(r => r?.Id?.Equals(ruleId, StringComparison.Ordinal) ?? false);

            if (existingRule == null)
            {
                return CommonReturnValue.UserError("The specified rule was not found.");
            }

            existingRule.Update(txt);

            return CommonReturnValue.Success();
        }


        public CommonReturnValue DeleteRule(string ruleId)
        {
            var existingRule = this.Rules.FirstOrDefault(r => r?.Id?.Equals(ruleId, StringComparison.Ordinal) ?? false);

            if (existingRule == null)
            {
                return CommonReturnValue.UserError("The specified rule was not found.");
            }

            this.Rules.Remove(existingRule);

            return CommonReturnValue.Success();
        }


        public void ApplyDbLevelRules()
        {
            this.ApplyRules(JsFile.DBLevel);
        }

        public void ApplyRules(JsFile jsFileContext)
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

        public CommonReturnValue MayAccessDbSource(string referer, string jsDALApiKey)
        {
            if (jsDALApiKey != null)
            {
                // TODO: test against some list
                if (jsDALApiKey.Equals("C50AEA64-951C-45F4-AF8D-539929ACD9EF", StringComparison.OrdinalIgnoreCase))
                {
                    return CommonReturnValue.Success();
                }
            }

            if (referer != null && referer.Equals("$WEB SOCKETS$")) return CommonReturnValue.Success();

            if (this.WhitelistedDomainsCsv == null)
            {
                return CommonReturnValue.UserError("No access list exists.");
            }

            //var referer = req.Headers["Referer"].FirstOrDefault();

            var whitelistedIPs = this.WhitelistedDomainsCsv.Split(',');

            if (referer != null)
            {
                if (System.Uri.TryCreate(referer, UriKind.RelativeOrAbsolute, out var refererUri))
                {

                    foreach (string en in whitelistedIPs)
                    {
                        if (en.Equals(refererUri.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            return CommonReturnValue.Success();
                        }
                    }
                }
            }

            return CommonReturnValue.UserError($"The host ({ referer }) is not allowed to access this resource.");
        }

        // gets a distinct list of all routines across all endpoint cache's
        public List<CachedRoutine> GetUnifiedCacheList()
        {
            if (this.Endpoints == null) return null;

            var distinct = new Dictionary<string, CachedRoutine>();

            this.Endpoints.SelectMany(ep => ep.CachedRoutines ?? new List<CachedRoutine>())
                .ToList()
                .ForEach(routine =>
                    {
                        var fn = routine.FullName;

                        if (!distinct.ContainsKey(fn))
                        {
                            distinct.Add(fn, routine);
                        }

                    });

            return distinct.Values.ToList();
        }

        public JsFile GetJsFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var jsfile = this.JsFiles.FirstOrDefault(f => f.Filename.Equals(name, StringComparison.OrdinalIgnoreCase));

            return jsfile;
        }


        [JsonIgnore]
        public string ServerMethodJs { get; private set; }
        [JsonIgnore]
        public string ServerMethodTSD { get; private set; }

        [JsonIgnore]
        public string ServerMethodJsEtag { get; private set; }
        [JsonIgnore]
        public string ServerMethodTSDEtag { get; private set; }
        public void BuildAndCacheServerMethodJsAndTSD()
        {
            try
            {
                var registrations = ServerMethodManager.GetRegistrationsForApp(this);

                if (registrations.Count() > 0)
                {
                    this.GenerateX(registrations);
                }
                else
                {
                    this.ServerMethodJs = this.ServerMethodTSD = this.ServerMethodJsEtag = this.ServerMethodTSDEtag = null;
                }
            }
            catch (Exception ex)
            {
                SessionLog.Error($"Failed to generate ServerMethod output files for {this.Project.Name}/{this.Name}.See exception that follows.");
                SessionLog.Exception(ex);
            }
        }

        private void GenerateX(IEnumerable<ServerMethodPluginRegistration> registrations)
        {
            var combinedJS = new Dictionary<string/*Namespace*/, List<Definition>>();
            var combinedTSD = new Dictionary<string/*Namespace*/, List<Definition>>();
            var combinedConverterLookup = new List<string>();

            // combine outputs first
            foreach (var pluginReg in registrations)
            {
                pluginReg.ScriptGenerator.CombineOutput(this, ref combinedJS, ref combinedTSD, ref combinedConverterLookup);
            }

            var (js, tsd) = GenerateFullServerMethodCode(combinedJS, combinedTSD, combinedConverterLookup);

            //!?      var (js, tsd) = ServerMethodPluginRegistration.GenerateOutputFiles(this, registrations);

            this.ServerMethodJs = js;
            this.ServerMethodTSD = tsd;
            this.ServerMethodJsEtag = Controllers.PublicController.ComputeETag(System.Text.Encoding.UTF8.GetBytes(js));
            this.ServerMethodTSDEtag = Controllers.PublicController.ComputeETag(System.Text.Encoding.UTF8.GetBytes(tsd));
        }

        private (string/*js*/, string /*TSD*/) GenerateFullServerMethodCode(Dictionary<string/*Namespace*/, List<Definition>> appCombinedJS,
                Dictionary<string/*Namespace*/, List<Definition>> appCombinedTSD, List<string> appCombinedConverterLookup)
        {
            var sbJavascriptAll = new StringBuilder(ServerMethodManager.TEMPLATE_ServerMethodContainer);
            var sbTSDAll = new StringBuilder(ServerMethodManager.TEMPLATE_ServerMethodTypescriptDefinitionsContainer);

            var now = DateTime.Now;

            // JavaScript
            {
                var sbJS = new StringBuilder();

                foreach (var kv in appCombinedJS)
                {// kv.Key is the namespace
                    string objName = null;

                    if (kv.Key.Equals("ServerMethods", StringComparison.Ordinal))
                    {
                        objName = "var x = dal.ServerMethods";
                    }
                    else
                    {
                        objName = $"x.{kv.Key}";
                    }

                    sbJS.AppendLine($"\t{objName} = {{");

                    sbJS.Append(string.Join(",\r\n", kv.Value.Select(definition => "\t\t" + definition.Line).ToArray()));

                    sbJS.AppendLine("\r\n\t};\r\n");
                }

                // TODO: FOOTER/global stuff ->> move out to App level or something?

                var nsLookupArray = string.Join(',', appCombinedJS.Where(kv => kv.Key != "ServerMethods").Select(kv => $"\"{kv.Key}\"").ToArray());

                var converterLookupJS = string.Join(", ", appCombinedConverterLookup);

                sbJavascriptAll.Replace("<<DATE>>", now.ToString("dd MMM yyyy, HH:mm"))
                    .Replace("<<NAMESPACE_LOOKUP>>", nsLookupArray)
                    .Replace("<<CONVERTER_LOOKUP>>", converterLookupJS)
                    .Replace("<<ROUTINES>>", sbJS.ToString())
                    .Replace("<<FILE_VERSION>>", "001") // TODO: not sure if we need a fileversion here?
                    .Replace("<<SERVER_NAME>>", Environment.MachineName)
                    ;
            }

            // TSD
            {
                var sbTSD = new StringBuilder();
                var sbTypeDefs = new StringBuilder();
                var sbComplexTypeDefs = new StringBuilder();

                foreach (var kv in appCombinedTSD)
                {
                    var insideCustomNamespace = false;

                    if (!kv.Key.Equals("ServerMethods", StringComparison.Ordinal))
                    {
                        insideCustomNamespace = true;

                        sbTSD.AppendLine($"\t\tstatic {kv.Key}: {{");
                    }

                    sbTSD.AppendLine(string.Join("\r\n", kv.Value.Select(definition => (insideCustomNamespace ? "\t" : "") + "\t\t" + definition.Line).ToArray()));

                    var typeDefLines = kv.Value.SelectMany(def => def.TypesLines).Where(typeDef => typeDef != null).Select(l => "\t\t" + l).ToArray();

                    sbTypeDefs.AppendLine(string.Join("\r\n", typeDefLines));

                    if (insideCustomNamespace)
                    {
                        sbTSD.AppendLine("\t\t};");
                    }
                }

                // TODO: this should only be for those types that apply to this particular App plugin inclusion..currently we generate types for EVERYTHING found 
                // TSD: build types for Complex types we picked up
                foreach (var def in GlobalTypescriptTypeLookup.Definitions)
                {
                    sbComplexTypeDefs.AppendLine($"\t\ttype {def.TypeName} = {def.Definition};");
                }

                sbTypeDefs.Insert(0, sbComplexTypeDefs);

                // TODO: FOOTER/global stuff ->> move out to App level or something?
                sbTSDAll.Replace("<<DATE>>", now.ToString("dd MMM yyyy, HH:mm"))
                    .Replace("<<ResultAndParameterTypes>>", sbTypeDefs.ToString().TrimEnd(new char[] { '\r', '\n' }))
                    .Replace("<<MethodsStubs>>", sbTSD.ToString())
                    .Replace("<<FILE_VERSION>>", "001") // TODO: not sure if we need a fileversion here?
                    .Replace("<<SERVER_NAME>>", Environment.MachineName)
                    ;
            }

            return (sbJavascriptAll.ToString(), sbTSDAll.ToString());
        }


    }
}
