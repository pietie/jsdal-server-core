using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class RulesController : Controller
    {

        [HttpPost("/api/rule")]
        public ApiResponse CreateRule([FromQuery] string projectName, [FromQuery(Name = "dbSource")] string dbSourceName, [FromQuery] string jsFilenameGuid, [FromQuery] string json)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

                var dbSource = proj.getDatabaseSource(dbSourceName);

                if (dbSource == null) return ApiResponse.ExclamationModal($"The data source \"{dbSourceName}\" does not exist.");

                var obj = JsonConvert.DeserializeAnonymousType(json, new { Type = RuleType.Regex, Text = "" });

                if (jsFilenameGuid == null)
                { // DB-level

                    var ret = dbSource.addRule(obj.Type, obj.Text);

                    if (ret.isSuccess)
                    {
                        SettingsInstance.saveSettingsToFile();

                        //!GeneratorThreadDispatcher.SetRulesDirty(cs);

                        return ApiResponse.Success();
                    }
                    else
                    {
                        return ApiResponse.ExclamationModal(ret.userErrorVal);
                    }
                }
                else
                {
                    var jsFile = dbSource.JsFiles.FirstOrDefault(js => js.Guid.Equals(jsFilenameGuid, StringComparison.OrdinalIgnoreCase));

                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");

                    var ret = jsFile.addRule(obj.Type, obj.Text);

                    if (ret.isSuccess)
                    {
                        SettingsInstance.saveSettingsToFile();

                        //!                    GeneratorThreadDispatcher.SetRulesDirty(cs);

                        return ApiResponse.Success();
                    }
                    else
                    {
                        return ApiResponse.ExclamationModal(ret.userErrorVal);
                    }

                }

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


        [HttpDelete("/api/rule")]
        public ApiResponse DeleteRule([FromQuery] string projectName, [FromQuery(Name = "dbSource")] string dbSourceName, [FromQuery] string jsFilenameGuid, [FromQuery] string ruleGuid)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

                var dbSource = proj.getDatabaseSource(dbSourceName);

                if (dbSource == null) return ApiResponse.ExclamationModal($"The data source \"{dbSourceName}\" does not exist.");


                if (jsFilenameGuid == null)
                { // DB level
                    var ret = dbSource.deleteRule(ruleGuid);

                    if (ret.isSuccess)
                    {
                        //!GeneratorThreadDispatcher.SetRulesDirty(cs);
                        SettingsInstance.saveSettingsToFile();
                        return ApiResponse.Success();
                    }
                    else
                    {
                        return ApiResponse.ExclamationModal(ret.userErrorVal);
                    }
                }
                else
                {
                    var jsFile = dbSource.JsFiles.FirstOrDefault(js => js.Guid.Equals(jsFilenameGuid, StringComparison.OrdinalIgnoreCase));

                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");

                    var ret = jsFile.deleteRule(ruleGuid);

                    if (ret.isSuccess)
                    {
                        //!GeneratorThreadDispatcher.SetRulesDirty(cs);
                        SettingsInstance.saveSettingsToFile();
                        return ApiResponse.Success();
                    }
                    else return ApiResponse.ExclamationModal(ret.userErrorVal);
                }

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);

            }
        }

        [HttpGet("/api/rule/routineList")]
        public ApiResponse GetRoutineList([FromQuery] string projectName, [FromQuery] string dbSource, [FromQuery] string endpoint, [FromQuery] string jsFilenameGuid)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(projectName, dbSource, out var proj, out var dbs, out var resp))
                {
                    return resp;
                }

                if (!dbs.GetEndpoint(endpoint, out var ep, out var resp2))
                {
                    return ApiResponse.ExclamationModal(resp2.userErrorVal);
                }

                var cache = ep.cache;

                if (cache == null)
                {
                    return ApiResponse.ExclamationModal("Routine cache does not exist. Make sure the project thread is running and that it is able to access the database.");
                }

                JsFile jsFile = null;

                if (jsFilenameGuid != null)
                {

                    jsFile = dbs.JsFiles.FirstOrDefault(js => js.Guid.Equals(jsFilenameGuid, StringComparison.OrdinalIgnoreCase));

                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");
                }

                if (jsFile == null)
                {
                    dbs.applyDbLevelRules();

                    var dbLevel = JsFile.DBLevel;

                    var ret = cache.Where(row => !row.IsDeleted).OrderBy(a => a.FullName).Select(row =>
                      {

                          var ruleIns = row.RuleInstructions[dbLevel];

                          return new
                          {
                              RoutineFullName = row.FullName,
                              Included = ruleIns.Included ?? false,
                              Excluded = ruleIns.Excluded ?? false,
                              Reason = ruleIns.Reason,
                              Source = ruleIns.Source
                          };
                      });

                    return ApiResponse.Payload(ret);
                }
                else
                {
                    dbs.applyRules(jsFile);

                    var ret = cache.Where(row => !row.IsDeleted).OrderBy(a => a.FullName).Select(row =>
                      {

                          var ruleIns = row.RuleInstructions[jsFile];

                          return new
                          {
                              RoutineFullName = row.FullName,
                              Included = ruleIns.Included ?? false,
                              Excluded = ruleIns.Excluded ?? false,
                              Reason = ruleIns.Reason,
                              Source = ruleIns.Source
                          };
                      });

                    return ApiResponse.Payload(ret);
                }
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


        [HttpGet("/api/rule/ruleList")]
        public ApiResponse GetRuleList([FromQuery] string projectName, [FromQuery] string dbSource, [FromQuery] string endpoint, [FromQuery] string jsFilenameGuid)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(projectName, dbSource, out var proj, out var dbs, out var resp))
                {
                    return resp;
                }

                if (!dbs.GetEndpoint(endpoint, out var ep, out var resp2))
                {
                    return ApiResponse.ExclamationModal(resp2.userErrorVal);
                }

                var cachedRoutines = ep.cache;

                if (jsFilenameGuid == null)
                { // DB-level

                    dbs.applyDbLevelRules();

                    var ruleLookup = cachedRoutines?.GroupBy(cr => cr.RuleInstructions[JsFile.DBLevel]?.Rule).Select(g => new { Rule = g.Key, Count = g.Count() }).Where(g => g.Rule != null).ToDictionary(k => k.Rule);

                    var q = (from r in dbs.Rules
                             select new
                             {
                                 Ix = dbs.Rules.IndexOf(r) + 1,
                                 Type = (int)r.Type,
                                 Description = r.ToString(),
                                 r.Guid,
                                 IsDataSourceRule = true,
                                 DBLevelOnly = true,
                                 AffectedCount = (ruleLookup.ContainsKey(r) ? ruleLookup[r].Count : 0)
                             }).ToList();

                    return ApiResponse.Payload(q);
                }
                else
                { //  JsFile-level
                    var jsFile = dbs.JsFiles.FirstOrDefault(js => js.Guid.Equals(jsFilenameGuid, StringComparison.OrdinalIgnoreCase));

                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");

                    dbs.applyRules(jsFile);

                    var ruleLookup = cachedRoutines?.GroupBy(cr => cr.RuleInstructions[jsFile]?.Rule)
                                                    .Select(g => new { Rule = g.Key, Count = g.Count() })
                                                    .Where(g => g.Rule != null)
                                                    .ToDictionary(k => k.Rule);

                    var q = (from r in jsFile.Rules
                             select new
                             {
                                 Ix = jsFile.Rules.IndexOf(r) + 1,
                                 Type = (int)r.Type,
                                 Description = r.ToString(),
                                 r.Guid,
                                 IsDataSourceRule = false,
                                 DBLevelOnly = false,
                                 AffectedCount = (ruleLookup.ContainsKey(r) ? ruleLookup[r].Count : 0)
                             }).Union(
                        from r in dbs.Rules
                        select new
                        {
                            Ix = dbs.Rules.IndexOf(r) + 1,
                            Type = (int)r.Type,
                            Description = r.ToString(),
                            r.Guid,
                            IsDataSourceRule = true,
                            DBLevelOnly = false,
                            AffectedCount = (ruleLookup.ContainsKey(r) ? ruleLookup[r].Count : 0)
                        }
                        ).OrderByDescending(e => e.IsDataSourceRule).ThenBy(e => e.Ix)
                             .ToList();

                    return ApiResponse.Payload(q);
                }

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


    }
}