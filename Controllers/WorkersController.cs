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
                        name = wl.Endpoint.Name,
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
            var worker = WorkSpawner.GetWorker(id);

            if (worker != null)
            {
                return ApiResponse.Payload(new { Endpoint = worker.Endpoint.Pedigree, Log = worker.LogEntries });
            }
            else
            {
                return ApiResponse.Payload(null);
            }
        }

        [HttpPost("/api/workers/{id}/start")]
        public ApiResponse StartWorker([FromRoute] string id)
        {
            try
            {
                var worker = WorkSpawner.GetWorkerById(id);

                if (worker != null)
                {
                    if (!WorkSpawner.RestartWorker(worker))
                    {
                        return ApiResponse.ExclamationModal("Timeout reached while waiting for running worker to restart.");
                    }

                    return ApiResponse.Success();
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
                var worker = WorkSpawner.GetWorkerById(id);

                if (worker != null)
                {
                    worker.Stop();
                    
                    return ApiResponse.Success();
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