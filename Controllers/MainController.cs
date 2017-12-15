using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{
    public class MainController : Controller
    {
        [Authorize(Roles = "admin")]
        [HttpGet]
        [Route("api/main/stats")]
        public ApiResponse GetStats()
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();

            return ApiResponse.Payload(new
            {
                WebServerStartDate = Program.StartDate,
                Performance = new
                {
                    WorkingSet = proc.WorkingSet64,
                    PeakWorkingSet = proc.PeakWorkingSet64,
                    PrivateMemorySize = proc.PrivateMemorySize64
                },
                TickCount = Environment.TickCount,
                Environment.ProcessorCount,
                Environment.WorkingSet
            });
        }


        [Authorize(Roles = "admin")]
        [HttpGet]
        [Route("api/main/sessionlog")]
        public ApiResponse GetSessionLog()
        {
            return ApiResponse.Payload(SessionLog.Entries);
        }

        [Authorize(Roles = "admin")]
        [HttpGet]
        [Route("api/main/memdetail")]
        public ApiResponse getMemDetail()
        {
            try
            {

                // var memDetail =
                //     {
                //     ExceptionLogger: ExceptionLogger.memDetail(),
                //     Workers: WorkSpawner.memDetail(),
                //     SessionLog: SessionLog.memDetail()
                //     }
                var memDetail = "TODO";

                return ApiResponse.Payload(memDetail);
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }


        [HttpGet]
        [Route("api/main/issetupcomplete")]
        public ApiResponse isFirstTimeSetupComplete()
        {
            // res.setHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
            // res.setHeader("Pragma", "no-cache"); // HTTP 1.0.
            // res.setHeader("Content-Type", "application/json");

            return ApiResponse.Payload(UserManagement.adminUserExists);
        }

        [HttpPost]
        [Route("api/main/1sttimesetup")]
        public ApiResponse performFirstTimeSetup(dynamic user)
        {
            //if (req.body.adminUser)
            {
                //UserManagement.addUser({ username: req.body.adminUser.username, password: req.body.adminUser.password, isAdmin: true });
                //UserManagement.saveToFile();
            }
            return ApiResponse.ExclamationModal("TODO");
            return ApiResponse.Success();
        }

    }
}