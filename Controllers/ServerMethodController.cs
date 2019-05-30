using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{
    public class ServerMethodController : Controller
    {
        [AllowAnonymous]
        [HttpGet("/api/serverMethod/{project}/{app}/{endpoint}/{methodName}")]
        [HttpPost("/api/serverMethod/{project}/{app}/{endpoint}/{methodName}")]
        public IActionResult Execute([FromRoute] string project, [FromRoute] string app, [FromRoute] string endpoint, [FromRoute] string methodName)
        {
            return null;
            //return exec(new ExecOptions() { project = project, application = app, endpoint = endpoint, schema = schema, routine = routine, type = ExecType.Scalar });
        }
    }
}