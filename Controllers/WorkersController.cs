using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class WorkersController : Controller
    {

        [HttpGet("/api/workers")]
        public ApiResponse getAllWokers()
        {
            try
            {
                var ret = WorkSpawner.workerList.Select(wl =>
                {
                    return new
                    {
                        id = wl.ID,
                        name = wl.DBSource.Name,
                        desc = wl.Description,
                        status = wl.Status,
                        /*lastProgress = wl.lastProgress,
                        lastProgressMoment = wl.lastProgressMoment,
                        lastConnectMoment = wl.lastConnectedMoment,*/
                        isRunning = wl.IsRunning
                    };
                });

                return ApiResponse.Payload(ret);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        [HttpGet("/api/workers/{id}")]
        public ApiResponse getWorkerLog([FromRoute] string id)
        {
            var workerName = id;
            var worker = WorkSpawner.getWorker(workerName);

            if (worker != null)
            {
                return ApiResponse.Payload("TODO!!");
                //!return ApiResponse.Payload(worker.log.Entries);
            }
            else
            {
                return ApiResponse.Payload(null);
            }
        }

        [HttpPost("/api/workers/{id}/start")]
        public ApiResponse startWorker([FromRoute] string id)
        {
            try
            {
                var worker = WorkSpawner.getWorkerById(id);

                if (worker != null)
                {
                    //!worker.Start();
                    //return ApiResponse.Success();
                    return ApiResponse.ExclamationModal("TODO: START");
                }
                else
                {
                    return ApiResponse.ExclamationModal($"Failed to find specified worker: {id}");
                }
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }
        }

        [HttpPost("/api/workers/{id}/stop")]
        public ApiResponse stopWorker([FromRoute] string id)
        {
            try
            {
                var worker = WorkSpawner.getWorkerById(id);

                if (worker == null)
                {
                    //!worker.stop();
                    //return ApiResponse.Success();
                    return ApiResponse.ExclamationModal("TODO: STOP");
                }
                else
                {
                    return ApiResponse.ExclamationModal($"Failed to find specified worker: {id}");
                }
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }

        }

    }
}