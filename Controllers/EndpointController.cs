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
    public class EndpointController : Controller
    {
        // TODO: Move to new controller
        [HttpGet("/api/exec-tester/endpoints")]
        public ApiResponse GetAllEndpointsForSpecificApp()
        {
            try
            {
                return ApiResponse.Payload(SettingsInstance.Instance.ProjectList
                    .SelectMany(p =>
                            p.Applications.SelectMany(app => app.Endpoints.Select(ep => new
                            {
                                Project = p.Name,
                                App = app.Name,
                                Endpoint = ep.Name
                            }))));
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpGet("/api/exec-tester/search-routine")]
        public IActionResult FindRoutine([FromQuery] string project, [FromQuery] string app, [FromQuery] string endpoint, [FromQuery] string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
                {
                    return BadRequest("You need to specify at least 3 characters.");
                }

                if (!ControllerHelper.GetProjectAndAppAndEndpoint(project, app, endpoint, out var proj, out var application, out var ep, out var resp))
                {
                    return NotFound($"The specified endpoint does not exist: {project}/{app}/{endpoint}");
                }

                var q = (from r in ep.cache
                         where r.Contains(query)
                         select r.FullName).ToList();

                return Ok(q);
            }
            catch (Exception e)
            {
                return BadRequest(e.ToString());
            }
        }
        
         [HttpGet("/api/exec-tester/routine-metadata")]
        public IActionResult GetRoutineMetadata([FromQuery] string project, [FromQuery] string app, [FromQuery] string endpoint, [FromQuery] string routine)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(routine))
                {
                    return BadRequest("Routine name not specified");
                }

                if (!ControllerHelper.GetProjectAndAppAndEndpoint(project, app, endpoint, out var proj, out var application, out var ep, out var resp))
                {
                    return NotFound($"The specified endpoint does not exist: {project}/{app}/{endpoint}");
                }

                var r = ep.cache.FirstOrDefault(rt=>rt.EqualsQuery(routine));

                if (r == null)
                {
                    return NotFound("Specified routine not found");
                }
                
                return Ok(new {
                        Parameters = r.Parameters,
                        ResultSets = r.ResultSetMetadata
                });
            }
            catch (Exception e)
            {
                return BadRequest(e.ToString());
            }
        }

        [HttpGet("/api/endpoint")]
        public ApiResponse GetAllEndpointsForSpecificApp([FromQuery] string project, [FromQuery(Name="dbSourceName")] string appName)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, appName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                return ApiResponse.Payload(app.Endpoints
                                                        .Select(ep => new { ep.Name, ep.IsOrmInstalled })
                                                        .OrderBy(ep => ep.Name));
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }

        }

        [HttpGet("/api/endpoint/{endpointName}")]
        public ApiResponse GetSingleEndpoint([FromRoute] string endpointName, [FromQuery] string project, [FromQuery] string dbSource)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, dbSource, out var proj, out var dbs, out var resp))
                {
                    return resp;
                }

                if (!dbs.GetEndpoint(endpointName, out var endpoint, out var resp2))
                {
                    return ApiResponse.ExclamationModal(resp2.userErrorVal);
                }

                return ApiResponse.Payload(new
                {
                    endpoint.Name,
                    BgTaskKey = endpoint.GetBgTaskKey(),
                    endpoint.IsOrmInstalled,
                    MetadataConnection = new
                    {
                        InitialCatalog = endpoint.MetadataConnection?.InitialCatalog,
                        DataSource = endpoint.MetadataConnection?.DataSource,
                        UserID = endpoint.MetadataConnection?.UserID,
                        IntegratedSecurity = endpoint.MetadataConnection?.IntegratedSecurity,
                        Port = endpoint.MetadataConnection?.Port
                    },
                    ExecutionConnection = new
                    {
                        InitialCatalog = endpoint.ExecutionConnection?.InitialCatalog,
                        DataSource = endpoint.ExecutionConnection?.DataSource,
                        UserID = endpoint.ExecutionConnection?.UserID,
                        IntegratedSecurity = endpoint.ExecutionConnection?.IntegratedSecurity,
                        Port = endpoint.ExecutionConnection?.Port
                    }
                });
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }

        }

        [HttpPost("/api/endpoint/{name}")]
        public ApiResponse Post([FromRoute] string name, [FromQuery] string project, [FromQuery] string dbSourceName)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, dbSourceName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var ret = app.AddEndpoint(name);

                if (ret.isSuccess)
                {
                    app.GetEndpoint(name, out var endpoint, out var _);
                    WorkSpawner.CreateNewWorker(endpoint);
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

        [HttpPut("/api/endpoint/{name}")]
        public ApiResponse UpdateEndpoint([FromRoute] string name, [FromBody] string newName, [FromQuery] string project, [FromQuery] string dbSourceName)
        {
            if (!ControllerHelper.GetProjectAndApp(project, dbSourceName, out var proj, out var dbSource, out var resp))
            {
                return resp;
            }

            var ret = dbSource.UpdateEndpoint(name, newName);

            if (ret.isSuccess)
            {
                SettingsInstance.SaveSettingsToFile();
                Hubs.WorkerMonitor.Instance.NotifyObservers();
                return ApiResponse.Success();
            }
            else
            {
                return ApiResponse.ExclamationModal(ret.userErrorVal);
            }
        }

        [HttpPost("/api/endpoint/{name}/installOrm")]
        public ApiResponse InstallOrm([FromRoute] string name, [FromQuery] string projectName, [FromQuery] string dbSourceName)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(projectName, dbSourceName, out var proj, out var dbSource, out var resp))
                {
                    return resp;
                }

                if (!dbSource.GetEndpoint(name, out var endpoint, out var resp2))
                {
                    return ApiResponse.ExclamationModal(resp2.userErrorVal);
                }

                var bw = endpoint.InstallOrm();

                if (bw != null)
                {
                    if (bw == null) return ApiResponse.Payload(new { Success = true });
                    else return ApiResponse.Payload(new { Success = true, BgTaskKey = bw.Key });
                }
                else return ApiResponse.ExclamationModal("Failed to install ORM");
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        [HttpDelete("/api/endpoint/{name}")]
        public ApiResponse DeleteEndpoint([FromRoute] string name, [FromQuery] string project, [FromQuery] string dbSourceName)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, dbSourceName, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                if (!app.GetEndpoint(name, out var endpoint, out var resp2))
                {
                    return ApiResponse.ExclamationModal($"The endpoint '{name}' not found.");
                }
                var ret = app.DeleteEndpoint(name);

                if (ret.isSuccess)
                {
                    WorkSpawner.RemoveEndpoint(endpoint);
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

        [HttpGet]
        [Route("/api/endpoint/{name}/checkOrm")]
        public ApiResponse IsOrmInstalled([FromRoute] string name, [FromQuery] string projectName, [FromQuery] string dbSourceName, [FromQuery] bool forceRecheck)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(projectName, dbSourceName, out var proj, out var dbSource, out var resp))
                {
                    return resp;
                }

                if (!dbSource.GetEndpoint(name, out var endpoint, out var resp2))
                {
                    return ApiResponse.ExclamationModal(resp2.userErrorVal);
                }

                if (!forceRecheck && endpoint.IsOrmInstalled) return ApiResponse.Payload(null);

                var missingDeps = endpoint.CheckForMissingOrmPreRequisitesOnDatabase();

                endpoint.IsOrmInstalled = missingDeps == null;

                SettingsInstance.SaveSettingsToFile();

                return ApiResponse.Payload(missingDeps);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }


        }



        [HttpPost]
        [Route("api/endpoint/{name}/uninstallOrm")]
        public ApiResponse UninstallOrm([FromRoute] string name, [FromQuery] string projectName, [FromQuery] string dbSourceName)
        {

            try
            {
                if (!ControllerHelper.GetProjectAndApp(projectName, dbSourceName, out var proj, out var dbSource, out var resp))
                {
                    return resp;
                }

                if (!dbSource.GetEndpoint(name, out var endpoint, out var resp2))
                {
                    return ApiResponse.ExclamationModal(resp2.userErrorVal);
                }

                var success = endpoint.UnInstallOrm();

                if (success)
                {
                    endpoint.IsOrmInstalled = false;
                    SettingsInstance.SaveSettingsToFile();

                    return ApiResponse.Success();
                }
                else
                {
                    return ApiResponse.ExclamationModal("Failed to uninstall ORM");
                }
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        // Not needed as it is retrieved through the main /endpoint GET
        // [HttpGet("api/endpoint/{endpoint}/metadata-connection")]
        // public ApiResponse GetMetadataConnection([FromRoute] string endpoint, [FromQuery] string projectName, [FromQuery] string dbSourceName)
        // {
        //     if (!ControllerHelper.GetProjectAndApp(projectName, dbSourceName, out var proj, out var dbSource, out var resp))
        //     {
        //         return resp;
        //     }

        //     if (!dbSource.GetEndpoint(endpoint, out var ep, out var resp2))
        //     {
        //         return ApiResponse.ExclamationModal(resp2.userErrorVal);
        //     }

        //     var mdc = ep.MetadataConnection;
        //     return ApiResponse.Payload(new
        //     {
        //         InitialCatalog = mdc.initialCatalog,
        //         DataSource = mdc.dataSource,
        //         UserID = mdc.userID,
        //         IntegratedSecurity = mdc.integratedSecurity,
        //         port = mdc.port
        //     });
        // }

        [HttpPost("api/endpoint/{endpoint}/connection")]
        public ApiResponse AddUpdateConnection([FromRoute] string endpoint, [FromQuery] string projectName, [FromQuery] string dbSourceName,
            [FromBody] Newtonsoft.Json.Linq.JObject json
        )
        {
            try
            {
                bool isMetadata = (bool)json["isMetadata"].ToObject(typeof(bool));
                string dataSource = json["dataSource"].ToString();
                string catalog = json["catalog"].ToString();
                string username = json["username"].ToString();
                string password = json["password"].ToString();
                int? port = json["port"].ToObject(typeof(int?)) as int?;
                bool integratedSecurity = json["authType"].ToString() == "100";

                if (integratedSecurity)
                {
                    username = password = null;
                }

                if (!port.HasValue) port = 1433;

                if (!ControllerHelper.GetProjectAndApp(projectName, dbSourceName, out var proj, out var dbSource, out var resp))
                {
                    return resp;
                }

                if (!dbSource.GetEndpoint(endpoint, out var ep, out var resp2))
                {
                    return ApiResponse.ExclamationModal(resp2.userErrorVal);
                }

                CommonReturnValueWithApplication ret = null;

                if (isMetadata) ret = ep.UpdateMetadataConnection(dataSource, catalog, username, password, port.Value);
                else ret = ep.UpdateExecConnection(dataSource, catalog, username, password, port.Value);

                if (!ret.isSuccess)
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }

                WorkSpawner.RestartWorker(ep);
                SettingsInstance.SaveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpGet]
        [Route("/api/endpoint/{endpoint}/summary")]
        public ApiResponse GetMetadataSummary([FromRoute] string endpoint, [FromQuery] string projectName, [FromQuery] string dbSource)
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


                var routineCache = ep.cache;

                dynamic ormSummary = new System.Dynamic.ExpandoObject();

                if (routineCache != null)
                {
                    var groups = routineCache.GroupBy(r => r.Type).Select(kv => new { Type = kv.Key, Count = kv.Count() });

                    ormSummary.LastUpdated = ep.LastUpdateDate;
                    ormSummary.Groups = groups;
                    ormSummary.TotalCnt = routineCache.Count;
                }
                else
                {
                    ormSummary.TotalCnt = 0;
                }

                return ApiResponse.Payload(new
                {
                    Orm = ormSummary,
                    Rules = "TODO"
                });

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpPost]
        [Route("/api/endpoint/{endpoint}/clearcache")]
        public ApiResponse ClearCache([FromRoute] string endpoint, [FromQuery] string projectName, [FromQuery] string dbSource)
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


                if (ep.ClearCache())
                {
                    SettingsInstance.SaveSettingsToFile();

                    return ApiResponse.Success();
                }
                else
                {
                    return ApiResponse.ExclamationModal("Failed to clear cache. Check session log for errors.");
                }
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);

            }

        }

        [HttpGet]
        [Route("/api/endpoint/{endpoint}/cachedroutines")]
        public ApiResponse GetCachedRoutines([FromRoute] string endpoint, [FromQuery] string project, [FromQuery] string dbSource, [FromQuery] string q, [FromQuery] string type
                , [FromQuery] string status, [FromQuery] bool? hasMeta, [FromQuery] bool? isDeleted)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, dbSource, out var proj, out var dbs, out var resp))
                {
                    return resp;
                }


                if (!dbs.GetEndpoint(endpoint, out var ep, out var resp2))
                {
                    return ApiResponse.ExclamationModal(resp2.userErrorVal);
                }

                var routineCache = ep.cache;
                IEnumerable<CachedRoutine> results = routineCache;

                if (!string.IsNullOrWhiteSpace(q))
                {
                    q = q.ToLower();
                    results = results.Where(r => r.FullName.ToLower().IndexOf(q) >= 0);
                }

                if (type != "0"/*All*/)
                {
                    results = results.Where(r => r.Type.ToLower() == type.ToLower());
                }

                if (status == "1"/*Has error*/)
                {
                    results = results.Where(r => r.ResultSetError != null && r.ResultSetError.Trim() != "");
                }
                else if (status == "2"/*No error*/)
                {
                    results = results.Where(r => r.ResultSetError == null || r.ResultSetError.Trim() == "");
                }

                if (hasMeta ?? false)
                {
                    results = results.Where(r => r.jsDALMetadata != null && r.jsDALMetadata.jsDAL != null);
                }

                if (isDeleted ?? false)
                {
                    results = results.Where(r => r.IsDeleted);
                }

                return ApiResponse.Payload(new
                {
                    Results = results.OrderBy(a => a.FullName),
                    TotalCount = routineCache.Count
                });
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }


    }



}