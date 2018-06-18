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

        [HttpGet("/api/app/{name}/file")]
        public ApiResponse GetJsFiles([FromQuery] string project, [FromRoute] string name)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var q = app.JsFiles.Select(j => { return new { Filename = j.Filename, Id = j.Id }; }).OrderBy(j => j.Filename);

                return ApiResponse.Payload(q);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpPost("/api/app/{name}/file")]
        public ApiResponse AddJsFile([FromQuery] string project, [FromRoute] string name, [FromQuery] string jsFileName) // TODO: Change from jsFilename to jsFileGuid?
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var ret = app.AddJsFile(jsFileName);

                if (ret.isSuccess)
                {
                    SettingsInstance.SaveSettingsToFile();
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


        [HttpPut("/api/app/{name}/file")]
        public ApiResponse UpdateJsFile([FromQuery] string project, [FromRoute] string name, [FromQuery] string oldName, [FromQuery] string newName)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                //!if (!newName.ToLower().EndsWith(".js")) newName += ".js";

                // TODO: All validation needs to be move OM API
                var existing = app.JsFiles.FirstOrDefault(js => js.Filename.Equals(oldName, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    return ApiResponse.ExclamationModal($"The output file \"{oldName}\" does not exist in \"{project}/{name}\"");
                }

                var existingNewName = app.JsFiles.FirstOrDefault(js => js.Filename.Equals(newName, StringComparison.OrdinalIgnoreCase));

                if (existingNewName != null)
                {
                    return ApiResponse.ExclamationModal($"The output file \"{newName}\" already exists in \"{project}/{name}\"");
                }

                existing.Filename = newName;
                SettingsInstance.SaveSettingsToFile();

                //!GeneratorThreadDispatcher.SetOutputFilesDirty(cs);

                return ApiResponse.Success();
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);

            }
        }

        [HttpDelete("/api/app/{name}/file/{id}")]
        public ApiResponse DeleteJsFile([FromQuery] string project, [FromRoute] string name, [FromRoute] string file)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndApp(project, name, out var proj, out var app, out var resp))
                {
                    return resp;
                }

                var existing = app.GetJsFile(file);

                if (existing == null)
                {
                    return ApiResponse.ExclamationModal($"The output file \"{file}\" does not exist in \"{project}/{name}\"");
                }

                app.JsFiles.Remove(existing);

                SettingsInstance.SaveSettingsToFile();

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