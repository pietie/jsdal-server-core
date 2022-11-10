using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class ApplicationController : Controller
    {

        [HttpPost]
        [Route("/api/app")]
        public ApiResponse CreateApplication([FromBody] string name, [FromQuery] string project, [FromQuery] string jsNamespace, [FromQuery] int? defaultRuleMode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return ApiResponse.ExclamationModal("Please provide a valid application name.");
                }

                if (!defaultRuleMode.HasValue)
                {
                    return ApiResponse.ExclamationModal("Please specify the default rule mode.");
                }

                if (!ControllerHelper.GetProject(project, out var proj, out var resp))
                {
                    return resp;
                }

                var existing = proj.GetApplication(name);

                if (existing != null)
                {
                    return ApiResponse.ExclamationModal($"The application \"{name}\" already exists on project \" {project}\".");
                }

                var ret = proj.AddApplication(name, jsNamespace, defaultRuleMode.Value);

                if (!ret.IsSuccess) return ApiResponse.ExclamationModal(ret.userErrorVal);

                SettingsInstance.SaveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpPut("/api/app/update")]
        public ApiResponse UpdateApplication([FromBody] string name, [FromQuery] string oldName,
          [FromQuery] string project, [FromQuery] string jsNamespace, [FromQuery] int? defaultRuleMode)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, oldName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var anotherExisting = proj.GetApplication(name);

                if (anotherExisting != null)
                {
                    return ApiResponse.ExclamationModal($"The application \"{name}\" already exists on project \" {project}\".");
                }

                var ret = app.Update(name, jsNamespace, defaultRuleMode);

                if (!ret.IsSuccess)
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }

                Hubs.WorkerMonitor.Instance.NotifyObservers();

                SettingsInstance.SaveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpDelete("/api/app/{name}")]
        public ApiResponse DeleteApplication([FromQuery] string project, string name)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                if (proj.DeleteApplication(app))
                {
                    SettingsInstance.SaveSettingsToFile();

                    //!WorkSpawner.RemoveApplication(cs); TODO: Move to endpoint
                }

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }


        // get list of Applications for a specific Project
        [HttpGet("/api/app")]
        public ApiResponse GetAllApplications([FromQuery] string project)
        {
            try
            {
                if (!ControllerHelper.GetProject(project, out var proj, out var resp))
                {
                    return resp;
                }


                if (proj.Applications == null) proj.Applications = new List<Application>();

                var apps = proj.Applications.Select(app =>
                                 new
                                 {
                                     app.Name,
                                     app.DefaultRuleMode,
                                     app.JsNamespace
                                 }).OrderBy(app => app.Name);

                return ApiResponse.Payload(apps);
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpGet("/api/app/{name}")]
        public ApiResponse GetSingleApplication([FromRoute] string name, [FromQuery] string project)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                return ApiResponse.Payload(new
                {
                    app.Name,
                    app.DefaultRuleMode,
                    app.JsNamespace
                });
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }


        [HttpGet("/api/app/{name}/plugins")]
        public ApiResponse GetPlugins([FromQuery] string project, [FromRoute] string name)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                if (app.Plugins == null) app.Plugins = new List<string>();

                var ret = PluginLoader.Instance.PluginAssemblies
                    .SelectMany(a => a.Plugins, (pa, plugin) => new
                    {
                        pa.IsInline,
                        Name = plugin.Name,
                        Description = plugin.Description,
                        Guid = plugin.Guid,
                        Included = app.IsPluginIncluded(plugin.Guid.ToString()),
                        plugin.Type,
                        SortOrder = 0

                    }).ToList();

                // var inlinePlugins = PluginLoader.Instance.PluginAssemblies
                //         .Where(pa => pa.IsInline)
                //         .SelectMany(pa => pa.Plugins)
                //         .Select(p => new
                //         {
                //             Name = p.Name,
                //             Description = p.Description,
                //             Guid = p.Guid,
                //             Included = app.IsPluginIncluded(p.Guid.ToString()),
                //             p.Type,
                //             SortOrder = 0
                //         });



                // ret.AddRange(inlinePlugins);

                return ApiResponse.Payload(ret);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);

            }

        }

        [HttpPost("/api/app/{name}/plugins")]
        public ApiResponse SavePluginConfig([FromQuery] string project, [FromRoute] string name, [FromBody] List<dynamic> pluginList)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var ret = app.UpdatePluginList(pluginList);

                if (ret.IsSuccess)
                {
                    SettingsInstance.SaveSettingsToFile();
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

        [HttpGet("/api/app/{name}/whitelist")]
        public ApiResponse GetWhitelistedDomains([FromQuery] string project, [FromRoute] string name)
        {
            if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
            {
                return resp;
            }

            return ApiResponse.Payload(new
            {
                AllowAllPrivate = app.WhitelistAllowAllPrivateIPs,
                Whitelist = app.WhitelistedDomainsCsv != null ? app.WhitelistedDomainsCsv.Split(',') : null
            });
        }

        [HttpPost("/api/app/{name}/whitelist")]
        [HttpPut("/api/app/{name}/whitelist")]
        public ApiResponse UpdateWhitelist([FromQuery] string project, [FromRoute] string name, [FromQuery] string whitelist, [FromQuery] bool allowAllPrivate)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                app.WhitelistAllowAllPrivateIPs = allowAllPrivate;

                if (whitelist != null)
                {
                    var ar = whitelist.Split('\n').Select(w => w.Trim()).Where(w => !string.IsNullOrEmpty(w));

                    if (ar.Count() > 0)
                    {
                        app.WhitelistedDomainsCsv = string.Join(",", ar);
                    }
                    else
                    {
                        app.WhitelistedDomainsCsv = null;
                    }

                }
                else
                {
                    app.WhitelistedDomainsCsv = null;
                }

                SettingsInstance.SaveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpGet("/api/app/{name}/exec-policy")]
        public ApiResponse GetExecutionPolicies([FromQuery] string project, [FromRoute] string name)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                return ApiResponse.Payload(app.ExecutionPolicies);
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpPost("/api/app/{name}/exec-policy")]
        [HttpPut("/api/app/{name}/exec-policy")]
        public ApiResponse AddUpdateExecutionPolicy([FromQuery] string project, [FromRoute] string name, [FromBody] ExecutionPolicy execPolicy)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var ret = app.AddUpdateExecutionPolicy(execPolicy);

                if (ret.IsSuccess)
                {
                     SettingsInstance.SaveSettingsToFile();
                     return ApiResponse.Success();
                }
                else
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpPost("/api/app/{name}/exec-policy/set-default")]
        [HttpPut("/api/app/{name}/exec-policy/set-default")]
        public ApiResponse SetExecutionPolicyDefault([FromQuery] string project, [FromRoute] string name, [FromQuery] string id)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var ret = app.SetDefaultExecutionPolicy(id);

                if (ret.IsSuccess)
                {
                     SettingsInstance.SaveSettingsToFile();
                     return ApiResponse.Success();
                }
                else
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpDelete("/api/app/{name}/exec-policy")]
        public ApiResponse DeleteExecutionPolicy([FromQuery] string project, [FromRoute] string name, [FromQuery] string id)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var ret = app.DeleteExecutionPolicy(id);

                if (ret.IsSuccess)
                {
                     SettingsInstance.SaveSettingsToFile();
                     return ApiResponse.Success();
                }
                else
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

    }
}