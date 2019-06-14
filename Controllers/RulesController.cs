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

        [HttpGet("/api/rule")]
        public ApiResponse GetRules([FromQuery] string project, [FromQuery(Name = "app")] string appName, [FromQuery] string file)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, appName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                if (!app.GetUnifiedCacheListWithApiResponse(out var allRoutines, out resp))
                {
                    return resp;
                }

                if (file == null)
                { // DB-level

                    var lookup = allRoutines.Where(row => !row.IsDeleted)
                                              .Select(routine =>
                                                    {
                                                        var instruction = jsdal_server_core.Settings
                                                                                .ObjectModel
                                                                                .RoutineIncludeExcludeInstruction.Create(routine, app.Rules, (DefaultRuleMode)app.DefaultRuleMode);


                                                        if (instruction.Reason != null)
                                                        {

                                                            return new
                                                            {
                                                                RoutineFullName = routine.FullName,
                                                                Rule = instruction.Rule
                                                            };
                                                        }
                                                        else return null;
                                                    }).Where(r => r.Rule != null).ToList();

                    var q = (from r in app.Rules
                             select new
                             {
                                 Ix = app.Rules.IndexOf(r) + 1,
                                 Type = (int)r.Type,
                                 Description = r.ToString(),
                                 r.Id,
                                 IsAppRule = true,
                                 AppLevelOnly = true,
                                 AffectedCount = lookup.Count(l => l.Rule == r)
                             }).ToList();

                    return ApiResponse.Payload(q);
                }
                else
                { //  JsFile-level
                    var jsFile = app.GetJsFile(file);

                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");

                    var lookup = allRoutines.Where(row => !row.IsDeleted)
                                        .Select(routine =>
                                              {
                                                  var instruction = jsdal_server_core.Settings
                                                                          .ObjectModel
                                                                          .RoutineIncludeExcludeInstruction.Create(routine, app.Rules, (DefaultRuleMode)app.DefaultRuleMode, jsFile.Rules);


                                                  if (instruction.Reason != null)
                                                  {

                                                      return new
                                                      {
                                                          RoutineFullName = routine.FullName,
                                                          Rule = instruction.Rule
                                                      };
                                                  }
                                                  else return null;
                                              }).Where(r => r.Rule != null).ToList();


                    var q = (from r in jsFile.Rules
                             select new
                             {
                                 Ix = jsFile.Rules.IndexOf(r) + 1,
                                 Type = (int)r.Type,
                                 Description = r.ToString(),
                                 r.Id,
                                 IsAppRule = false,
                                 AppLevelOnly = false,
                                 AffectedCount = lookup.Count(l => l.Rule == r)
                             }).Union(
                        from r in app.Rules
                        select new
                        {
                            Ix = app.Rules.IndexOf(r) + 1,
                            Type = (int)r.Type,
                            Description = r.ToString(),
                            r.Id,
                            IsAppRule = true,
                            AppLevelOnly = false,
                            AffectedCount = lookup.Count(l => l.Rule == r)
                        }
                        ).OrderByDescending(e => e.IsAppRule).ThenBy(e => e.Ix)
                             .ToList();

                    return ApiResponse.Payload(q);
                }

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


        [HttpPost("/api/rule")]
        public ApiResponse CreateRule([FromQuery] string project, [FromQuery(Name = "app")] string appName, [FromQuery] string file, [FromBody] Newtonsoft.Json.Linq.JObject json)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, appName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var type = (RuleType)int.Parse(json["Type"].ToString());
                string value = json["Value"].ToString();


                if (file == null)
                { // DB-level
                    var ret = app.AddRule(type, value);

                    if (ret.IsSuccess)
                    {
                        SettingsInstance.SaveSettingsToFile();

                        WorkSpawner.SetRulesDirty(app);

                        return ApiResponse.Success();
                    }
                    else
                    {
                        return ApiResponse.ExclamationModal(ret.userErrorVal);
                    }
                }
                else
                {
                    var jsFile = app.GetJsFile(file);

                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");

                    var ret = jsFile.AddRule(type, value);

                    if (ret.IsSuccess)
                    {
                        SettingsInstance.SaveSettingsToFile();

                        WorkSpawner.SetRulesDirty(app, jsFile);

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

        [HttpPut("/api/rule/{ruleId}")]
        public ApiResponse UpdateRule([FromQuery] string project, [FromQuery(Name = "app")] string appName, [FromQuery] string file, [FromRoute] string ruleId, [FromBody] Newtonsoft.Json.Linq.JObject json)
        {
            try
            {                             
                if (!ControllerHelper.GetProjectAndApp(project, appName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var type = (RuleType)int.Parse(json["Type"].ToString());
                string value = json["Value"].ToString();

                CommonReturnValue ret;
                JsFile jsFile = null;

                if (file == null)
                { // DB-level
                    ret = app.UpdateRule(ruleId, value);
                }
                else
                {
                    jsFile = app.GetJsFile(file);

                    // TODO: Move check and error message down to App api?
                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");

                    ret = jsFile.UpdateRule(ruleId, value);
                }

                if (ret.IsSuccess)
                {
                    SettingsInstance.SaveSettingsToFile();

                    if (jsFile == null) WorkSpawner.SetRulesDirty(app);
                    else WorkSpawner.SetRulesDirty(app, jsFile);

                    return ApiResponse.Success();
                }
                else
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }



        [HttpDelete("/api/rule/{ruleId}")]
        public ApiResponse DeleteRule([FromQuery] string project, [FromQuery(Name = "app")] string appName, [FromQuery] string file, [FromRoute] string ruleId)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, appName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                if (file == null)
                { // DB level
                    var ret = app.DeleteRule(ruleId);

                    if (ret.IsSuccess)
                    {
                        WorkSpawner.SetRulesDirty(app);
                        SettingsInstance.SaveSettingsToFile();
                        return ApiResponse.Success();
                    }
                    else
                    {
                        return ApiResponse.ExclamationModal(ret.userErrorVal);
                    }
                }
                else
                {
                    var jsFile = app.GetJsFile(file);

                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");

                    var ret = jsFile.DeleteRule(ruleId);

                    if (ret.IsSuccess)
                    {
                        WorkSpawner.SetRulesDirty(app, jsFile);
                        SettingsInstance.SaveSettingsToFile();
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

        [HttpGet("/api/rule/routine-list")]
        public ApiResponse GetAllRoutines([FromQuery] string project, [FromQuery(Name = "app")] string appName, [FromQuery] string file)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, appName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                if (!app.GetUnifiedCacheListWithApiResponse(out var allRoutines, out resp))
                {
                    return resp;
                }

                JsFile jsFile = null;

                if (file != null)
                {
                    jsFile = app.GetJsFile(file);

                    if (jsFile == null) return ApiResponse.ExclamationModal("The specified output file was not found.");
                }

                if (jsFile == null)
                {
                    var ret = allRoutines.Where(row => !row.IsDeleted)
                                            .OrderBy(a => a.FullName)
                                            .Select(routine =>
                                                    {
                                                        var instruction = jsdal_server_core.Settings
                                                                                .ObjectModel
                                                                                .RoutineIncludeExcludeInstruction.Create(routine, app.Rules, (DefaultRuleMode)app.DefaultRuleMode);


                                                        return new
                                                        {
                                                            RoutineFullName = routine.FullName,
                                                            Included = instruction.Included ?? false,
                                                            Excluded = instruction.Excluded ?? false,
                                                            Reason = instruction.Reason,
                                                            Source = instruction.Source
                                                        };
                                                    });

                    return ApiResponse.Payload(new { Routines = ret, app.DefaultRuleMode });
                }
                else
                {

                    var ret = allRoutines.Where(row => !row.IsDeleted)
                                        .OrderBy(a => a.FullName)
                                        .Select(routine =>
                                                {
                                                    var instruction = jsdal_server_core.Settings
                                                                            .ObjectModel
                                                                            .RoutineIncludeExcludeInstruction.Create(routine, app.Rules, (DefaultRuleMode)app.DefaultRuleMode, jsFile.Rules);


                                                    return new
                                                    {
                                                        RoutineFullName = routine.FullName,
                                                        Included = instruction.Included ?? false,
                                                        Excluded = instruction.Excluded ?? false,
                                                        Reason = instruction.Reason,
                                                        Source = instruction.Source
                                                    };
                                                });


                    return ApiResponse.Payload(new { Routines = ret, app.DefaultRuleMode });
                }
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


    }
}