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
    [Authorize(Roles = "admin")]
    public class PerformanceController : Controller
    {

        [HttpGet("/api/performance/tmp-executionlist")]
        public ApiResponse TmpGetRawExecutionList()
        {
            return null;
            // return ApiResponse.Payload(ExecTracker.ExecutionList);
        }


        private string GetEndpointDescription(string endpointId)
        {
            // var allEps = Settings.SettingsInstance.Instance
            //             .ProjectList
            //             .SelectMany(p => p.Applications.SelectMany(a => a.Endpoints)).ToList();

            var ep = Settings.SettingsInstance.Instance
                        .ProjectList
                        .SelectMany(p => p.Applications.SelectMany(a => a.Endpoints))
                        .FirstOrDefault(ep => ep.Id != null && ep.Id.Equals(endpointId, StringComparison.OrdinalIgnoreCase));

            if (ep != null) return ep.Pedigree;
            return endpointId;
        }

        [HttpGet("/api/performance/stats/totalcounts")]
        public ApiResponse GetStatsTotalCounts([FromQuery] int? top)
        {
            try
            {
                if (!top.HasValue || top.Value < 0) top = 20;

                int tick = Environment.TickCount;

                var payload = (from t in StatsDB.GetTotalCountsTopN(top.Value)
                               select new
                               {
                                   Endpoint = GetEndpointDescription(t.EndpointId),
                                   t.RoutineFullName,
                                   t.ExecutionCount,
                                   TotalDurationSec = (decimal)t.TotalDuration / 1000.0M,
                                   t.TotalRows
                               }).ToList();

                tick = Environment.TickCount - tick;

                return ApiResponse.Payload(new
                {
                    Payload = payload,
                    FetchTimeInMs = tick
                });
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpGet("/api/performance/stats/totalcounts/numofentries")]
        public IActionResult GetStatsEntryCount()
        {
            try
            {
                return Ok(StatsDB.GetTotalUniqueExecutionsCount());
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }
        }
    }

}