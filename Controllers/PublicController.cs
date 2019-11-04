using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using jsdal_server_core.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using jsdal_server_core.Changes;
using Endpoint = jsdal_server_core.Settings.ObjectModel.Endpoint;
using Newtonsoft.Json;


namespace jsdal_server_core.Controllers
{
    [AllowAnonymous]
    public class PublicController : Controller
    {
        [HttpGet("/api/jsdal/ping")]
        public string Ping()
        {
            return "1.0"; // TODO: Version?
        }

        [HttpGet("/api/jsdal/server-detail")]
        public IActionResult GetServerDetail()
        {
            try
            {
                if (SettingsInstance.Instance.ProjectList == null || SettingsInstance.Instance.ProjectList.Count == 0) return null;

                var currentETagHeader = this.Request.Headers["If-None-Match"].FirstOrDefault();


                var q = (from p in SettingsInstance.Instance.ProjectList
                         select new
                         {
                             Name = p.Name,
                             Apps = p.Applications.Select(app => new
                             {
                                 app.Name,
                                 Endpoints = app.Endpoints.Select(ep => new
                                 {

                                     ep.Name,
                                     Files = app.JsFiles.Select(f => f.Filename)
                                 })
                             })
                         })
                         .ToList();

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(q);

                using (var md5 = MD5.Create())
                {
                    var result = md5.ComputeHash(Encoding.ASCII.GetBytes(json));
                    var hash = Convert.ToBase64String(result);

                    if (currentETagHeader != null && currentETagHeader.Equals(hash))
                    {
                        this.Response.Headers.Clear();
                        return StatusCode(304);
                    }

                    this.Response.Headers["ETag"] = hash;
                    this.Response.Headers["Cache-Control"] = "max-age=0";
                    this.Response.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
                }

                return Ok(q);
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                throw;
            }
        }



        [HttpPost("/api/jsdal/subscription/watch")]
        public IActionResult WatchSubscription([FromQuery] string project, [FromQuery] string app, [FromQuery] string endpoint, [FromQuery] string file)
        {
            try
            {
                if (SettingsInstance.Instance.ProjectList == null) return NotFound();

                if (!ControllerHelper.GetProjectAndAppAndEndpoint(project, app, endpoint, out var proj, out var application, out var ep, out var resp))
                {
                    return NotFound();
                }

                var jsFile = application.GetJsFile(file);

                if (jsFile == null) return NotFound();

                string json;
                string jsEtag = null;
                string smEtag = null;

                using (var sr = new System.IO.StreamReader(this.Request.Body))
                {
                    json = sr.ReadToEnd();

                    var watch = JsonConvert.DeserializeObject<dynamic>(json);

                    if (watch["JsEtag"] != null)
                    {
                        jsEtag = watch["JsEtag"].Value;
                    }

                    if (watch["SMEtag"] != null)
                    {
                        smEtag = watch["SMEtag"].Value;
                    }
                }

                bool hasJsChanges = false;
                bool hasSMChanges = false;
                int watchForSeconds = 20;
                int tickCountEnd = Environment.TickCount + (watchForSeconds * 1000);

                while (!hasJsChanges && !hasSMChanges && Environment.TickCount <= tickCountEnd)
                {
                    hasJsChanges = jsFile.ETag != jsEtag;

                    //hasSMChanges = jsFile.ETag != jsEtag;
                    System.Threading.Thread.Sleep(500);
                }

                return Ok(new { HasJsChanges = hasJsChanges, HasSMChanges = hasSMChanges });
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("/api/jsdal/file")]
        public IActionResult GetFile([FromQuery] string project, [FromQuery] string app, [FromQuery] string endpoint, [FromQuery] string file, [FromQuery] long v = 0, [FromQuery] bool min = false, [FromQuery] bool tsd = false)
        {
            try
            {
                if (SettingsInstance.Instance.ProjectList == null) return NotFound();

                if (!ControllerHelper.GetProjectAndAppAndEndpoint(project, app, endpoint, out var proj, out var application, out var ep, out var resp))
                {
                    return NotFound();
                }

                var jsFile = application.GetJsFile(file);

                if (jsFile == null) return NotFound();

                var jsFileDescriptor = $"{project}/{app}/{endpoint}/{file}";

                if (tsd)
                {
                    return ServeTypescriptDefinition(ep, jsFile);
                }

                var path = min ? ep.MinifiedOutputFilePath(jsFile) : ep.OutputFilePath(jsFile);

                if (!System.IO.File.Exists(path))
                {
                    Console.WriteLine($"412: {jsFileDescriptor}");
                    return StatusCode(StatusCodes.Status412PreconditionFailed, $"The requested file ({jsFileDescriptor}) is not valid or has not been generated yet");
                }

                byte[] jsFileData;

                using (var fs = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                {
                    jsFileData = new byte[fs.Length];
                    fs.Read(jsFileData, 0, jsFileData.Length);
                }

                var etagForLatestFile = ComputeETag(jsFileData);

                var etagFromRequest = this.Request.Headers["If-None-Match"];

                // worker-state
                {
                    string workerStateHeaderValue = null;
                    var worker = WorkSpawner.GetWorkerByEndpoint(ep);

                    if (worker != null)
                    {
                        if (worker.IsRunning)
                        {
                            workerStateHeaderValue = "running - " + worker.Status;
                        }
                        else
                        {
                            workerStateHeaderValue = "stopped - " + worker.Status;
                        }
                    }
                    else
                    {
                        workerStateHeaderValue = "no-worker";
                    }

                    this.Response.Headers.Add("w-state", workerStateHeaderValue);
                }

                if (!string.IsNullOrWhiteSpace(etagFromRequest) && !string.IsNullOrWhiteSpace(etagForLatestFile))
                {
                    if (etagForLatestFile == etagFromRequest) return StatusCode(StatusCodes.Status304NotModified);
                }

                var ret = File(jsFileData, "text/javascript");

                ret.EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(etagForLatestFile);

                this.Response.Headers.Add("jsfver", jsFile.Version.ToString());

                string filterJson = null;

                if (this.Request.Headers.ContainsKey("changes-filter"))
                {
                    filterJson = this.Request.Headers["changes-filter"];
                }

                var changeCnt = JsFileChangesTracker.Instance.CountChanges(ep, jsFile, (int)v, jsFile.Version, filterJson);

                this.Response.Headers.Add("change-cnt", changeCnt.ToString());

                return ret;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("/api/jsdal/file/changes")]
        public IActionResult GetFileChanges([FromQuery] string project, [FromQuery] string app, [FromQuery] string endpoint, [FromQuery] string file, [FromQuery] int from = 0, [FromQuery] int to = 0)
        {
            try
            {
                if (SettingsInstance.Instance.ProjectList == null) return NotFound();

                if (!ControllerHelper.GetProjectAndAppAndEndpoint(project, app, endpoint, out var proj, out var application, out var ep, out var resp))
                {
                    return NotFound();
                }

                var jsFile = application.GetJsFile(file);

                if (jsFile == null) return NotFound();

                var jsFileDescriptor = $"{project}/{app}/{endpoint}/{file}";

                if (to == -1) to = jsFile.Version;

                var changes = JsFileChangesTracker.Instance.BuildChangeList(ep, jsFile, from, to);

                return Ok(changes);
            }
            catch (Exception ex)
            {

                SessionLog.Exception(ex);
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("/api/tsd/common")]
        public IActionResult ServeCommonTSD()
        {
            try
            {
                var typescriptDefinitionsCommon = System.IO.File.ReadAllText("./resources/TypeScriptDefinitionsCommon.d.ts");

                return Ok(typescriptDefinitionsCommon);
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return BadRequest(ex.Message);
            }
        }

        // we use the same .js & .tsd for each endpoint on an App so we just need up to App level in the query string
        [HttpGet("/api/servermethods")]
        public IActionResult ServeServerMethods([FromQuery] string project, [FromQuery] string app, [FromQuery] long v = 0, [FromQuery] bool min = false, [FromQuery] bool tsd = false)
        {
            try
            {
                if (SettingsInstance.Instance.ProjectList == null) return NotFound();

                if (!ControllerHelper.GetProjectAndApp(project, app, out var proj, out var application, out var resp))
                {
                    return NotFound();
                }

                FileResult ret = null;
                string content = tsd ? application.ServerMethodTSD : application.ServerMethodJs;

                if (string.IsNullOrWhiteSpace(content))
                {
                    return NotFound();
                }

                var etagFromRequest = this.Request.Headers["If-None-Match"];
                string etag = tsd ? application.ServerMethodTSDEtag : application.ServerMethodJsEtag;

                if (!string.IsNullOrWhiteSpace(etagFromRequest) && !string.IsNullOrWhiteSpace(etag))
                {
                    if (etag == etagFromRequest)
                    {
                        return StatusCode(StatusCodes.Status304NotModified);
                    }
                }

                ret = File(System.Text.Encoding.UTF8.GetBytes(content), "text/javascript");
                ret.EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(etag);

                return ret;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return BadRequest(ex.Message);
            }
        }


        [HttpGet("/api/jsdal/projects")]
        public List<dynamic> ListProjects()
        {// TODO: Handle No Projects exist
            try
            {
                var q = (from p in SettingsInstance.Instance.ProjectList
                         select new { Name = p.Name, Guid = (string)null }).ToList<dynamic>();

                return q;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                throw;
            }
        }

        [HttpGet("api/jsdal/files")]
        public IActionResult GetOutputFiles([FromQuery] string dbSourceGuid)
        {
            // TODO: Review all of this
            var dbSource = SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications).FirstOrDefault(db => db.Name.Equals(dbSourceGuid, StringComparison.OrdinalIgnoreCase));

            if (dbSource == null)
            {
                return NotFound($"The DB Source {dbSourceGuid} does not exist.");
            }

            var jsFiles = dbSource.JsFiles.Select(f => new { f.Filename, f.Id, f.Version }).ToList<dynamic>();

            return Ok(jsFiles);
        }

        [HttpGet("/api/thread/{dbSourceGuid}/status")]
        public IActionResult GetThreadStatus(Guid dbSourceGuid)
        {
            try
            {
                dynamic workThread = null;//!JsFileGenerator.GetThread(dbSourceGuid);

                if (workThread == null)
                {
                    return Content($"The specified DB source {dbSourceGuid} is invalid or does not have a thread running.");
                    /* 
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent($"The specified DB source {dbSourceGuid} is invalid or does not have a thread running.")
                    };*/
                }

                //var ret = new HttpResponseMessage(HttpStatusCode.OK);

                var obj = new
                {
                    workThread.CreateDate,
                    workThread.IsRunning,
                    workThread.Status
                };


                //ret.Content = new StringContent(JsonConvert.SerializeObject(obj));
                //ret.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                return Ok(obj);
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return BadRequest(ex.Message);
            }
        }


        public static string ComputeETag(byte[] data)
        {
            byte[] md5data = ((System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.CryptoConfig.CreateFromName("MD5")).ComputeHash(data);

            return "\"" + BitConverter.ToString(md5data).Replace("-", "").ToLower() + "\"";
        }


        [HttpGet]
        [Route("/api/hostname")]
        public string HostName()
        {
            var he = System.Net.Dns.GetHostEntry("localhost");
            return he?.HostName;
        }

        private IActionResult ServeTypescriptDefinition(Endpoint endpoint, JsFile jsFile)
        {
            // if (jsFile == null && app != null)
            // {
            //     // TODO: Get server ip/dns name???
            //     var refs = app.JsFiles.Select(f => $"/// <reference path=\"./api/tsd/{f.Id}\" />").ToArray();
            //     string content = "";

            //     if (refs.Length > 0)
            //     {
            //         content = string.Join("\r\n", refs);
            //     }

            //     return Ok(content);
            // }


            var tsdFilePath = endpoint.OutputTypeScriptTypingsFilePath(jsFile);

            if (!System.IO.File.Exists(tsdFilePath)) return NotFound();

            byte[] tsdData;

            using (var fs = System.IO.File.Open(tsdFilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            {
                tsdData = new byte[fs.Length];
                fs.Read(tsdData, 0, tsdData.Length);
            }

            Response.Headers.Add("jsfver", jsFile.Version.ToString());

            return Content(System.Text.Encoding.UTF8.GetString(tsdData));
        }


        // [HttpGet("/api/jsdal/outputs")]
        // public List<dynamic> GetOutputDetail([FromQuery] string projectGuid)
        // {
        //     var project = SettingsInstance.Instance.ProjectList.FirstOrDefault(p => p.Guid.Equals(projectGuid, StringComparison.OrdinalIgnoreCase));
        //     // TODO: throw if project does not exist
        //     var ret = project.Applications.Select(db => new { db.Name, Files = db.JsFiles.Select(f => new { f.Filename, f.Id, f.Version }) }).ToList<dynamic>();

        //     return ret;
        // }

        // [HttpGet("/api/jsdal/dbsources")]
        // public IActionResult GetDbSources([FromQuery] string projectGuid)
        // {
        //     var project = SettingsInstance.Instance.ProjectList.FirstOrDefault(p => p.Guid.Equals(projectGuid, StringComparison.OrdinalIgnoreCase));

        //     if (project == null)
        //     {
        //         return NotFound($"The Project {projectGuid} does not exist.");
        //     }

        //     var dbSources = project.Applications.Select(db => new { db.Name }).ToList<dynamic>();

        //     return Ok(dbSources);
        // }



        /*  15/05/2018, PL: Commented out..now sure where this is used..if at all... TODO: check jsdal-cli
                [HttpGet("api/meta")]
                public List<dynamic> GetMetadataUpdates([FromQuery] string dbSourceGuid, [FromQuery] long maxRowver)
                {
                    try
                    {
                        var db = SettingsInstance.Instance.ProjectList.SelectMany(p => p.DatabaseSources).FirstOrDefault(d => d.CacheKey.Equals(dbSourceGuid, StringComparison.OrdinalIgnoreCase));

                        if (db == null) throw new Exception(string.Format("The DB source {0} was not found", dbSourceGuid));

                        var cache = db.cache;

                        if (cache == null) return null;

                        var q = (from c in cache
                                 where c.RowVer > maxRowver //&& !c.IsDeleted --> PL: We need to servce IsDeleted as well so that the subscribers can act on the operation
                                 select new
                                 {
                                     Catalog = db.MetadataConnection.initialCatalog,
                                     Name = c.Routine,
                                     c.Schema,
                                     c.Parameters,
                                     c.ResultSetError,
                                     c.ResultSetMetadata,
                                     c.jsDALMetadata?.jsDAL,
                                     c.RowVer,
                                     c.IsDeleted
                                 }


                                 ).ToList<dynamic>();


                        return q;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        SessionLog.Exception(ex);
                        return null;
                    }
                }
                */

        // [HttpGet("/api/js/{fileGuid}")] // TODO: support multiple ways api/js/quickRef .... api/js/projName/dbSourceName/fileName (e.g. api/js/vZero/IceV0_Audit/General.js)
        // public IActionResult ServeFile(string fileGuid, [FromQuery] long v = 0, [FromQuery] bool min = false, [FromQuery] bool tsd = false)
        // {
        //     try
        //     {
        //         if (SettingsInstance.Instance.ProjectList == null) return NotFound();

        //         var jsFile = SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications).SelectMany(db => db.JsFiles).FirstOrDefault(f => f.Id.Equals(fileGuid, StringComparison.OrdinalIgnoreCase));

        //         if (jsFile == null) return NotFound();

        //         var dbSource = SettingsInstance.Instance.ProjectList.SelectMany(p => p.Applications).First(db => db.JsFiles.Contains(jsFile));

        //         if (tsd) // typescript definition
        //         {
        //             return ServeTypescriptDefinition(jsFile, dbSource);
        //         }

        //         throw new NotImplementedException();
        //         Endpoint endpoint = null; // TODO: !!!!!

        //         var path = min ? endpoint.MinifiedOutputFilePath(jsFile) : endpoint.OutputFilePath(jsFile);

        //         if (!System.IO.File.Exists(path))
        //         {
        //             Console.WriteLine("412: " + dbSource.Name + " -- " + jsFile.Filename);
        //             //   return new HttpResponseMessage(HttpStatusCode.PreconditionFailed) { Content = new StringContent("The requested file is not valid or has not been generated yet") };
        //             return StatusCode(StatusCodes.Status412PreconditionFailed, "The requested file is not valid or has not been generated yet");
        //         }

        //         byte[] jsFileData;

        //         using (var fs = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
        //         {
        //             jsFileData = new byte[fs.Length];
        //             fs.Read(jsFileData, 0, jsFileData.Length);
        //         }


        //         var etagForLatestFile = ComputeETag(jsFileData);

        //         var etagFromRequest = this.Request.Headers["If-None-Match"];

        //         if (!string.IsNullOrWhiteSpace(etagFromRequest) && !string.IsNullOrWhiteSpace(etagForLatestFile))
        //         {
        //             if (etagForLatestFile == etagFromRequest) return StatusCode(StatusCodes.Status304NotModified);
        //         }


        //         var ret = File(jsFileData, "text/javascript");

        //         ret.EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(etagForLatestFile);

        //         this.Response.Headers.Add("jsfver", jsFile.Version.ToString());

        //         //!ret.Headers.Add("jsfver", jsFile.Version.ToString());

        //         return ret;
        //     }
        //     catch (Exception ex)
        //     {
        //         SessionLog.Exception(ex);
        //         return BadRequest(ex.Message);
        //     }
        // }



//         [AllowAnonymous]
//         [HttpGet("/api/webmanifest")]
//         public IActionResult ServeWebManifest()
//         {
//             try
//             {
//                 string manifest = System.IO.File.ReadAllText("C:\\00-Work\\00 - Projects\\10 - EA\\10 - PWAs\\10 - Member app\\MemberHtmlApp\\MemberHtmlApp\\dist\\whitelabel\\aon\\manifest.json");

//                 var d = JsonConvert.DeserializeObject<dynamic>(manifest);

// d["name"] = "name" + Environment.TickCount;
// d["short_name"] = "short" + Environment.TickCount;

//                 d["super-secret"] = "sec" + Environment.TickCount;

// //d["start_url"] = "https://servicedev.europassistance.co.za:4302/test-start";
//                 //d["start_url"] = "https://servicedev.europassistance.co.za:4302/test-start";
//                 d["start_url"] = "https://servicetest4.europassistance.co.za/test-start";

//                 manifest = JsonConvert.SerializeObject(d);


//                 var data = System.Text.Encoding.UTF8.GetBytes(manifest);

//                 return new FileContentResult(data, "application/json");
//             }
//             catch (Exception ex)
//             {
//                 return BadRequest(ex.Message);
//             }
//         }

    }
}