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
    public class JsFileController : Controller
    {
        [HttpGet]
        [Route("/api/database/jsFiles")]
        public ApiResponse GetJsFiles([FromQuery] string projectName, [FromQuery] string dbSource)
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
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"'{dbSource}\"");
                }

                var q = cs.JsFiles.Select(j => { return new { Filename = j.Filename, Guid = j.Guid }; }).OrderBy(j => j.Filename);

                return ApiResponse.Payload(q);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }



        [HttpPost]
        [Route("/api/database/addJsfile")]
        public ApiResponse AddJsFile([FromQuery] string projectName, [FromQuery] string dbSource, [FromQuery] string jsFileName) // TODO: Change from jsFilename to jsFileGuid?
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jsFileName))
                {
                    return ApiResponse.ExclamationModal("Please provide a valid file name.");
                }

                if (!jsFileName.ToLower().EndsWith(".js")) jsFileName += ".js";

                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

                var cs = proj.getDatabaseSource(dbSource);

                if (cs == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"'{dbSource}\"");
                }

                var ret = cs.addJsFile(jsFileName);

                if (ret.isSuccess)
                {
                    SettingsInstance.saveSettingsToFile();
                    //?!GeneratorThreadDispatcher.SetOutputFilesDirty(cs);
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


        [HttpPut]
        [Route("/api/database/updateJsFile")]
        public ApiResponse UpdateJsFile([FromQuery] string projectName, [FromQuery] string dbSource, [FromQuery] string oldName, [FromQuery] string newName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newName))
                {
                    return ApiResponse.ExclamationModal("Please provide a valid file name.");
                }

                if (!newName.ToLower().EndsWith(".js")) newName += ".js";

                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

                var cs = proj.getDatabaseSource(dbSource);

                if (cs == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"'{dbSource}\"");
                }

                var existing = cs.JsFiles.FirstOrDefault(js => js.Filename.Equals(oldName, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    return ApiResponse.ExclamationModal($"The output file \"{oldName}\" does not exist in \"{projectName}/{dbSource}\"");
                }

                var existingNewName = cs.JsFiles.FirstOrDefault(js => js.Filename.Equals(newName, StringComparison.OrdinalIgnoreCase));

                if (existingNewName != null)
                {
                    return ApiResponse.ExclamationModal($"The output file \"{newName}\" already exists in \"{projectName}/{dbSource}\"");
                }

                existing.Filename = newName;
                SettingsInstance.saveSettingsToFile();

                //!GeneratorThreadDispatcher.SetOutputFilesDirty(cs);

                return ApiResponse.Success();
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);

            }
        }

        [HttpDelete("/api/jsfile/{jsFilenameGuid}")]
        public ApiResponse DeleteJsFile([FromQuery] string projectName, [FromQuery] string dbSource, [FromRoute] string jsFilenameGuid)
        {
            try
            {
                var proj = SettingsInstance.Instance.getProject(projectName);

                if (proj == null) return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");

                var cs = proj.getDatabaseSource(dbSource);

                if (cs == null)
                {
                    return ApiResponse.ExclamationModal($"The project \"{projectName}\" does not contain a datasource called \"'{dbSource}\"");
                }

                var existing = cs.JsFiles.FirstOrDefault(js => js.Guid.Equals(jsFilenameGuid, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    return ApiResponse.ExclamationModal($"The output file \"{jsFilenameGuid}\" does not exist in \"{projectName}/{dbSource}\"");
                }

                //cs.JsFiles.splice(cs.JsFiles.indexOf(existing));
                cs.JsFiles.Remove(existing);

                SettingsInstance.saveSettingsToFile();

                //!GeneratorThreadDispatcher.SetOutputFilesDirty(cs);

                return ApiResponse.Success();
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);

            }
        }


    }
}