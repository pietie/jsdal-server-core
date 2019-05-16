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
        [HttpGet("/api/performance/tmp-executionlist")]
        public ApiResponse TmpGetRawExecutionList()
        {
            return null;
           // return ApiResponse.Payload(ExecTracker.ExecutionList);
        }

        [Authorize(Roles = "admin")]
        [HttpGet("/api/performance/top")]
        public ApiResponse GetTopResources()
        {
            int topN = 20;

            if (topN > 30) topN = 30; // constraint max to something reasonable

            return ApiResponse.Payload(PerformanceAggregator.GetTopN(topN));
        }
    }

}