using System;
using jsdal_server_core.Performance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{

    [Authorize(Roles = "admin")]
    public class HealthController : Controller
    {
        [HttpGet("/api/health/latest")]
        public ActionResult GetLatest()
        {
            return Ok(jsDALHealthMonitorThread.Instance.GetLatest());
        }

        [HttpPost("/api/health/stop")]
        public ActionResult StopThread()
        {
            try
            {
                jsDALHealthMonitorThread.Instance.Shutdown();
                return Ok(true);
            }
            catch (System.Exception ex)
            {
                return BadRequest(ex.ToString());
            }
        }

        [HttpPost("/api/health/start")]
        public ActionResult StartThread()
        {
            try
            {
                jsDALHealthMonitorThread.Instance.Init();
                return Ok(true);
            }
            catch (System.Exception ex)
            {
                return BadRequest(ex.ToString());
            }
        }

        [HttpGet("/api/health/thread-status")]
        public ActionResult GetThreadStatus()
        {
            try
            {
                return Ok(new
                {
                    IsRunning = jsDALHealthMonitorThread.Instance.IsRunning

                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }
        }

        [HttpGet("/api/health/topN")]
        public ActionResult TopNResources([DateTimeModelBinder(DateFormat = "yyyyMMddHHmm"), FromQuery(Name = "from")] DateTime? fromDate,
            [DateTimeModelBinder(DateFormat = "yyyyMMddHHmm"), FromQuery(Name = "to")] DateTime? toDate)
        {
            try
            {
                if (!fromDate.HasValue) return BadRequest("The parameter 'from' is mandatory");
                if (!toDate.HasValue) return BadRequest("The parameter 'to' is mandatory");

                return Ok(jsDALHealthMonitorThread.Instance.GetReport(fromDate.Value, toDate.Value));
            }
            catch (System.Exception ex)
            {

                return BadRequest(ex.ToString());
            }
        }
    }

}