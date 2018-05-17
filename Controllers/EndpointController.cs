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
        [HttpGet("/api/endpoint")]
        public ApiResponse GetAllEndpoints([FromQuery] string project, [FromQuery] string dbSourceName)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, dbSourceName, out var proj, out var dbSource, out var resp))
                {
                    return resp;
                }

                return ApiResponse.Payload(dbSource.Endpoints
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
                    endpoint.IsOrmInstalled,
                    MetadataConnection = new
                    {
                        InitialCatalog = endpoint.MetadataConnection?.initialCatalog,
                        DataSource = endpoint.MetadataConnection?.dataSource,
                        UserID = endpoint.MetadataConnection?.userID,
                        IntegratedSecurity = endpoint.MetadataConnection?.integratedSecurity,
                        Port = endpoint.MetadataConnection?.port
                    },
                    ExecutionConnection = new
                    {
                        InitialCatalog = endpoint.ExecutionConnection?.initialCatalog,
                        DataSource = endpoint.ExecutionConnection?.dataSource,
                        UserID = endpoint.ExecutionConnection?.userID,
                        IntegratedSecurity = endpoint.ExecutionConnection?.integratedSecurity,
                        Port = endpoint.ExecutionConnection?.port
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
                if (!ControllerHelper.GetProjectAndApp(project, dbSourceName, out var proj, out var dbSource, out var resp))
                {
                    return resp;
                }

                var ret = dbSource.AddEndpoint(name);

                if (ret.isSuccess)
                {
                    //!WorkSpawner.UpdateDatabaseSource(dbSource, ret.dbSource); TODO: Spin up a new worker
                    SettingsInstance.saveSettingsToFile();
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
                // TODO: Update existing worker?
                SettingsInstance.saveSettingsToFile();
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

                var installed = endpoint.InstallOrm();

                if (installed)
                {
                    endpoint.IsOrmInstalled = true;

                    //!WorkSpawner.resetMaxRowDate(cs);

                    SettingsInstance.saveSettingsToFile();

                    return ApiResponse.Success();
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
                if (!ControllerHelper.GetProjectAndApp(project, dbSourceName, out var proj, out var dbSource, out var resp))
                {
                    return resp;
                }

                var ret = dbSource.DeleteEndpoint(name);

                if (ret.isSuccess)
                {
                    // TODO: Stop existing worker
                    SettingsInstance.saveSettingsToFile();
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

                SettingsInstance.saveSettingsToFile();

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
                    SettingsInstance.saveSettingsToFile();

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

                CommonReturnValueWithDbSource ret = null;

                if (isMetadata) ret = ep.UpdateMetadataConnection(dataSource, catalog, username, password, port.Value);
                else ret = ep.UpdateExecConnection(dataSource, catalog, username, password, port.Value);

                if (!ret.isSuccess)
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }

                // TODO: Update worker thread appropriately?!

                SettingsInstance.saveSettingsToFile();

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


                ep.ClearCache();
                SettingsInstance.saveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);

            }

        }

        [HttpGet]
        [Route("/api/endpoint/{endpoint}/cachedroutines")]
        public ApiResponse GetCachedRoutines([FromRoute] string endpoint, [FromQuery] string projectName, [FromQuery] string dbSource, [FromQuery] string q, [FromQuery] string type
                , [FromQuery] string status, [FromQuery] bool? hasMeta, [FromQuery] bool? isDeleted)
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