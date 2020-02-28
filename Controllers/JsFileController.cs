using System;
using IO = System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

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

                if (ret.IsSuccess)
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

        [HttpDelete("/api/app/{name}/file/{file}")]
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


        [AllowAnonymous]
        [HttpGet("/api/js/{project}/{app}/{ep}/{filename}")]
        public ActionResult ServeFile([FromRoute(Name = "project")] string projectRoute, [FromRoute(Name = "app")] string appRoute, [FromRoute(Name = "ep")] string endpointRoute, [FromRoute] string filename)
        {
            try
            {
                if (!ControllerHelper.GetProjectAndAppAndEndpoint(projectRoute, appRoute, endpointRoute, out var project, out var app, out var endpoint, out var resp))
                {
                    return NotFound($"{projectRoute}/{appRoute}/{endpointRoute} not found");
                }

                var referer = this.Request.Headers["Referer"].FirstOrDefault();

                string jsDALApiKey = null;

                if (this.Request.Headers.ContainsKey("api-key"))
                {
                    jsDALApiKey = this.Request.Headers["api-key"];
                }

                var mayAccess = app.MayAccessDbSource(referer, jsDALApiKey);

                if (!mayAccess.IsSuccess)
                {
                    return Unauthorized($"Host {referer} not authorised");
                }

                var jsFile = app.GetJsFile(filename);

                if (jsFile == null)
                {
                    return NotFound($"The file '${filename}' does not exist on the specified app");
                }

                var jsFilePath = endpoint.OutputFilePath(jsFile);

                var fi = new IO.FileInfo(jsFilePath);

                if (fi.Exists)
                {
                    var etag = fi.ToETag();
                    var currentETagHeader = this.Request.Headers["If-None-Match"].FirstOrDefault();

                    if (currentETagHeader != null && currentETagHeader.Equals(etag.Tag.Value))
                    {
                        this.Response.Headers.Clear();
                        return StatusCode(304);
                    }

                    var ret = PhysicalFile(jsFilePath, "application/javascript", true/*EnableRangeProcessing*/);

                    ret.FileDownloadName = jsFile.Filename;
                    ret.LastModified = new DateTimeOffset(DateTime.UtcNow);
                    ret.EntityTag = etag;

                    return ret;
                }
                else
                {
                    return NotFound($"File does not exist on server. It could be that the jsdal-server still needs to generate it. Check the worker threads.");
                }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                ExceptionLogger.LogException(ex);
                return BadRequest("Application error occurred. See log for more detail");
            }
        }
    }
}