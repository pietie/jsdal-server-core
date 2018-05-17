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
    public class DataSourceController : Controller
    {
        // get list of DB Sources for a specific Project
        [HttpGet]
        [Route("api/database")]
        public ApiResponse Get([FromQuery] string project)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(project);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{project}\" does not exist.");

                if (proj.Applications == null) proj.Applications = new List<Application>();

                var sources = proj.Applications.Select(dbs =>
                                 new
                                 {
                                     Name = dbs.Name,
                                     DefaultRuleMode = dbs.DefaultRuleMode
                                 }).OrderBy(cs => cs.Name);

                return ApiResponse.Payload(sources);
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpGet("/api/dbs/{projectName}/{dbSourceName}")]
        public ApiResponse GetSingle([FromRoute] string projectName, [FromRoute] string dbSourceName)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

                var dbSource = proj.getDatabaseSource(dbSourceName);

                if (dbSource == null)
                {
                    return ApiResponse.ExclamationModal($"The database source entry \"{dbSourceName}\" does not exist.");
                }

                return ApiResponse.Payload(new
                {
                    Name = dbSource.Name,
                    DefaultRuleMode = dbSource.DefaultRuleMode
                });
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpDelete]
        [Route("api/database/{name}")]
        public ApiResponse DeleteDbSource([FromQuery] string projectName, string name)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"${projectName}\" does not exist.");

                var cs = proj.getDatabaseSource(name);

                if (cs == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"{name}\"");
                }

                proj.removeConnectionString(cs);

                //!WorkSpawner.RemoveApplication(cs); TODO: Move to endpoint

                SettingsInstance.saveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpPost]
        [Route("/api/database")]
        public ApiResponse Post([FromBody] string logicalName, [FromQuery] string project, [FromQuery] string dataSource, [FromQuery] string catalog,
                [FromQuery] string username, [FromQuery] string password, [FromQuery] string jsNamespace, [FromQuery] int defaultRoleMode, [FromQuery] int? port,
                [FromQuery] string instanceName)
        {
            try
            {
                if (!port.HasValue) port = 1433;
                if (string.IsNullOrEmpty(instanceName)) instanceName = null;

                if (string.IsNullOrWhiteSpace(logicalName))
                {
                    return ApiResponse.ExclamationModal("Please provide a valid database source name.");
                }

                var proj = SettingsInstance.Instance.getProject(project);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{project}\" does not exist.");

                var existing = proj.getDatabaseSource(logicalName);

                if (existing != null)
                {
                    return ApiResponse.ExclamationModal($"The database source entry \"{ logicalName}\" already exists.");
                }

                // var ret = proj.addMetadataConnectionString(logicalName, dataSource, catalog, username, password, jsNamespace, defaultRoleMode
                //                                             , port.Value, instanceName);

               // if (!ret.isSuccess) return ApiResponse.ExclamationModal(ret.userErrorVal);

           //!?!     WorkSpawner.AddDatabaseSource(ret.dbSource);

                SettingsInstance.saveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }


        // // [HttpGet]
        // // [Route("/api/dbconnections")]
        // // public ApiResponse GetDatabaseConnections([FromQuery] string projectName, [FromQuery] string dbSourceName)
        // // {
        // //     try
        // //     {
        // //         var proj = SettingsInstance.Instance.getProject(projectName);

        // //         if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

        // //         var dbSource = proj.getDatabaseSource(dbSourceName);

        // //         if (dbSource == null) return ApiResponse.ExclamationModal($"The data source \"{dbSourceName}\" does not exist.");

        // //         var dbConnections = dbSource.ExecutionConnections;

        // //         if (dbConnections == null) return ApiResponse.Payload(null);

        // //         return ApiResponse.Payload(dbConnections.Select(con =>
        // //         {
        // //             return new
        // //             {
        // //                 Guid = con.Guid,
        // //                 Name = con.Name,
        // //                 InitialCatalog = con.initialCatalog,
        // //                 DataSource = con.dataSource,
        // //                 UserID = con.userID,
        // //                 IntegratedSecurity = con.integratedSecurity,
        // //                 port = con.port,
        // //                 instanceName = con.instanceName
        // //             };
        // //         }).OrderBy(c => c.Name));
        // //     }
        // //     catch (Exception e)
        // //     {
        // //         return ApiResponse.Exception(e);
        // //     }
        // // }

        // // [HttpPost]
        // // [HttpPut]
        // // [Route("/api/dbconnection")]
        // // public ApiResponse AddUpdateDatabaseConnection([FromQuery] string dbSourceName, [FromQuery] string logicalName, [FromQuery] string dbConnectionGuid, [FromQuery] string projectName,
        // //         [FromQuery] string dataSource, [FromQuery] string catalog, [FromQuery] string username, [FromQuery] string password, [FromQuery] int? port, [FromQuery] string instanceName)
        // // {
        // //     try
        // //     {
        // //         // TODO: Validate parameters - mandatory and also things like logicalName(no special chars etc?)

        // //         if (!port.HasValue) port = 1433;
        // //         if (string.IsNullOrWhiteSpace(instanceName)) instanceName = null;

        // //         if (string.IsNullOrWhiteSpace(logicalName))
        // //         {
        // //             return ApiResponse.ExclamationModal("Please provide a valid database source name.");
        // //         }

        // //         var proj = SettingsInstance.Instance.getProject(projectName);

        // //         if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

        // //         var dbSource = proj.getDatabaseSource(dbSourceName);

        // //         if (dbSource == null) return ApiResponse.ExclamationModal($"The data source \"{dbSourceName}\" does not exist.");

        // //         var ret = dbSource.addUpdateDatabaseConnection(false, dbConnectionGuid, logicalName, dataSource, catalog, username, password, port.Value, instanceName);

        // //         if (ret.isSuccess)
        // //         {
        // //             WorkSpawner.UpdateDatabaseSource(dbSource, ret.dbSource);
        // //             SettingsInstance.saveSettingsToFile();
        // //             return ApiResponse.Success();
        // //         }
        // //         else
        // //         {
        // //             return ApiResponse.ExclamationModal(ret.userErrorVal);
        // //         }
        // //     }
        // //     catch (Exception e)
        // //     {
        // //         return ApiResponse.Exception(e);
        // //     }
        // // }


        // // // 04/07/2016, PL: Created.
        // // [HttpDelete]
        // // [Route("/api/dbconnection")]
        // // public ApiResponse DeleteDatabaseConnection([FromQuery] string dbConnectionGuid, [FromQuery] string projectName, [FromQuery] string dbSourceName)
        // // {
        // //     try
        // //     {
        // //         var proj = SettingsInstance.Instance.getProject(projectName);

        // //         if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

        // //         var dbSource = proj.getDatabaseSource(dbSourceName);

        // //         if (dbSource == null) return ApiResponse.ExclamationModal($"The data source \"{dbSourceName}\" does not exist.");

        // //         var ret = dbSource.deleteDatabaseConnection(dbConnectionGuid);

        // //         if (ret.isSuccess)
        // //         {
        // //             SettingsInstance.saveSettingsToFile();
        // //             return ApiResponse.Success();
        // //         }
        // //         else
        // //         {
        // //             return ApiResponse.ExclamationModal(ret.userErrorVal);
        // //         }
        // //     }
        // //     catch (Exception e)
        // //     {
        // //         return ApiResponse.Exception(e);
        // //     }
        // // }

        [HttpPut("/api/database/update")]
        public ApiResponse UpdateDatabaseSource([FromBody] string logicalName, [FromQuery] string oldName, [FromQuery] string project, [FromQuery] string dataSource
            , [FromQuery] string catalog, [FromQuery] string username, [FromQuery] string password, [FromQuery] string jsNamespace, [FromQuery] int defaultRoleMode
            , [FromQuery] int? port, [FromQuery] string instanceName
        )
        {
            try
            {
                if (!port.HasValue) port = 1433;
                if (string.IsNullOrWhiteSpace(instanceName)) instanceName = null;

                var proj = SettingsInstance.Instance.getProject(project);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{project}\" does not exist.");

                var existing = proj.getDatabaseSource(oldName);

                if (existing == null)
                {
                    return ApiResponse.ExclamationModal($"The database source entry \"{logicalName}\" does not exist and the update operation cannot continue.");
                }

                existing.Name = logicalName;

//?!?
                // var ret = existing.addUpdateDatabaseConnection(true/*isMetadataConnection*/, null, logicalName, dataSource
                //                     , catalog, username, password, port.Value, instanceName);

                // if (!ret.isSuccess)
                // {
                //     return ApiResponse.ExclamationModal(ret.userErrorVal);
                // }

                existing.JsNamespace = jsNamespace;
                existing.DefaultRuleMode = defaultRoleMode;

                SettingsInstance.saveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpGet]
        [Route("/api/database/plugins")]
        public ApiResponse GetPlugins([FromQuery] string projectName, [FromQuery] string dbSource)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");
                }

                var cs = proj.getDatabaseSource(dbSource);

                if (cs == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"{dbSource}\"");
                }

                if (cs.Plugins == null) cs.Plugins = new List<string>();

                var ret = Program.PluginAssemblies.SelectMany(p => p.Value).Select(p =>
                  {
                      return new
                      {
                          Name = p.Name,
                          Description = p.Description,
                          Guid = p.Guid,
                          Included = cs.isPluginIncluded(p.Guid.ToString()),
                          SortOrder = 0

                      };
                  });

                return ApiResponse.Payload(ret);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);

            }

        }

        [HttpPost]
        [Route("/api/database/plugins")]
        public ApiResponse SavePluginConfig([FromQuery] string projectName, [FromQuery] string dbSource, [FromBody] List<dynamic> pluginList)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");
                }

                var cs = proj.getDatabaseSource(dbSource);

                if (cs == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"{dbSource}\"");
                }

                var ret = cs.updatePluginList(pluginList);

                if (ret.isSuccess)
                {
                    SettingsInstance.saveSettingsToFile();
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




        [HttpGet]
        [Route("/api/database/whitelist")]
        public ApiResponse GetWhitelistedDomains([FromQuery] string projectName, [FromQuery] string dbSourceName)
        {
            var proj = SettingsInstance.Instance.getProject(projectName);

            if (proj == null)
            {
                return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");
            }

            var cs = proj.getDatabaseSource(dbSourceName);

            if (cs == null)
            {
                return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"{dbSourceName}\"");
            }

            return ApiResponse.Payload(new
            {
                AllowAllPrivate = cs.WhitelistAllowAllPrivateIPs,
                Whitelist = cs.WhitelistedDomainsCsv != null ? cs.WhitelistedDomainsCsv.Split(',') : null
            });
        }

        [HttpPost]
        [HttpPut]
        [Route("/api/database/whitelist")]
        public ApiResponse UpdateWhitelist([FromQuery] string projectName, [FromQuery] string dbSourceName, [FromQuery] string whitelist, [FromQuery] bool allowAllPrivate)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");
                }

                var cs = proj.getDatabaseSource(dbSourceName);

                if (cs == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"{dbSourceName}\"");
                }

                cs.WhitelistAllowAllPrivateIPs = allowAllPrivate;

                if (whitelist != null)
                {
                    var ar = whitelist.Split('\n').Select(w => w.Trim()).Where(w => !string.IsNullOrEmpty(w));

                    if (ar.Count() > 0)
                    {
                        cs.WhitelistedDomainsCsv = string.Join(",", ar);
                    }
                    else
                    {
                        cs.WhitelistedDomainsCsv = null;
                    }

                }
                else
                {
                    cs.WhitelistedDomainsCsv = null;
                }

                SettingsInstance.saveSettingsToFile();

                return ApiResponse.Success();
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }




    }
}