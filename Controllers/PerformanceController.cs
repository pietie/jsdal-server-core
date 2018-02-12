using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Performance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{
    public class PerformanceController : Controller
    {
        [Authorize(Roles = "admin")]
        [HttpGet]
        [Route("api/performance/tmp-executionlist")]
        public ApiResponse TmpGetRawExecutionList()
        {
            return ApiResponse.Payload(ExecTracker.ExecutionList);
        }
    }

}