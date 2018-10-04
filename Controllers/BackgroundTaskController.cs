using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using jsdal_server_core;

namespace jsdal_server_core.Controllers
{

    [Authorize(Roles = "admin")]
    public class BackgroundTaskController : Controller
    {
        [HttpGet("/api/bgtask")]
        public ApiResponse GetAllBackgroundTasks()
        {
            try
            {
                return ApiResponse.Payload(BackgroundTask
                        .Workers
                        .Select(t=> new { CreatedEpoch = t.Created.ToEpochMS(), t.IsDone, t.Name, Error = (string)null }).OrderBy(t=>t.CreatedEpoch));
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }

        }
    }
}