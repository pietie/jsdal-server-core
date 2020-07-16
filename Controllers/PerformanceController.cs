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


        [HttpGet("/api/performance/top")]
        public ApiResponse GetTopResources()
        {
            int topN = 20;

            if (topN > 30) topN = 30; // constraint max to something reasonable

            return ApiResponse.Payload(PerformanceAggregator.GetTopN(topN));
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
        public ApiResponse GetStatsTotalCounts()
        {
            try
            {
                int tick = Environment.TickCount;
                var payload = ApiResponse.Payload(from t in StatsDB.GetTotalCountsCollection()
                                                  select new
                                                  {
                                                      //   Endpoint = Settings.SettingsInstance.Instance
                                                      //                 .ProjectList
                                                      //                 .SelectMany(p => p.Applications.SelectMany(a => a.Endpoints))
                                                      //                 .FirstOrDefault(ep => ep.Id.Equals(t.EndpointId, StringComparison.OrdinalIgnoreCase))?.Pedigree ?? null,
                                                      Endpoint = GetEndpointDescription(t.EndpointId),
                                                      t.RoutineFullName,
                                                      t.ExecutionCount,
                                                      TotalDurationSec = (decimal)t.TotalDuration / 1000.0M,
                                                      t.TotalRows
                                                  }
                );

                tick = Environment.TickCount - tick;

                return payload;
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpGet("/api/performance/stats/entriescount")]
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

        [HttpGet("/api/performance/stats/test")]
        public IActionResult Test()
        {
            try
            {
                return Ok($"Test: {Environment.TickCount}");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }
        }
    }

}