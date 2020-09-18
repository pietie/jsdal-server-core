using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Performance.DataCollector;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{

    [Authorize(Roles = "admin")]
    public class DataCollectorController : Controller
    {
        [HttpGet("/api/data-collector")]
        public ActionResult Test()
        {
            return Ok(DataCollectorThread.Instance.GetAllDataTmp());

        }

        [HttpGet("/api/data-collector/test-data")]
        public ActionResult TestData()
        {
            return Ok(DataCollectorThread.Instance.GetSampleData());

        }

        [HttpDelete("/api/data-collector/executions")]
        public ActionResult ClearoutExecutions()
        {
            return Ok(DataCollectorThread.Instance.ClearExecutions());

        }

    }

}