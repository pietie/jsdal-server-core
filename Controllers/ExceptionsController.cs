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
    public class ExceptionsController : Controller
    {
        [HttpGet("/api/exception/{id}")]
        public ApiResponse GetException([FromRoute] string id)
        {
            try
            {
                var ex = ExceptionLogger.GetException(id);

                if (ex == null)
                {
                    return ApiResponse.ExclamationModal($"An exception with id \"{id}\" could not be found.");
                }

                return ApiResponse.Payload(ex);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


        [HttpGet("/api/exception/recent")]
        public ApiResponse GetRecentExceptions([FromQuery] int? top, [FromQuery] string endpoint, [FromQuery] string app, [FromQuery] string routine)
        {
            try
            {
                if (!top.HasValue) top = 20;
                if (top > 800) top = 800;

                string[] endpointLookup = null;
                string[] appLookup = null;


                if (string.IsNullOrEmpty(endpoint) || endpoint.Equals("all", StringComparison.OrdinalIgnoreCase)) endpoint = null;
                if (string.IsNullOrEmpty(app) || app.Equals("all", StringComparison.OrdinalIgnoreCase)) app = null;
                if (string.IsNullOrEmpty(routine)) routine = null;

                if (endpoint != null)
                {
                    endpointLookup = endpoint.Split(',', StringSplitOptions.RemoveEmptyEntries);

                    if (endpointLookup.FirstOrDefault(e => e.Equals("all", StringComparison.OrdinalIgnoreCase)) != null)
                    {
                        endpointLookup = null;
                    }

                }

                if (app != null)
                {
                    appLookup = app.Split(',', StringSplitOptions.RemoveEmptyEntries);

                    if (appLookup.FirstOrDefault(a => a.Equals("all", StringComparison.OrdinalIgnoreCase)) != null)
                    {
                        appLookup = null;
                    }
                }

                var ret = from exception in ExceptionLogger.GetAll(endpointLookup)
                          where exception.HasAppTitle(appLookup)
                            && (routine == null || (exception.execOptions?.MatchRoutine(routine) ?? false))
                          orderby exception.created.Ticks descending
                          select new {
                               exception.id,
                               exception.created,
                               exception.message,
                               exception.procedure,
                               exception.appTitle
                          }
                          ;

                return ApiResponse.Payload(new
                {
                    Results = ret.Take(Math.Min(top.Value, ret.Count())),
                    TotalExceptionCnt = ExceptionLogger.TotalCnt
                });
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        [HttpPost("/api/exception/clear-all")]
        public ApiResponse ClearAll()
        {
            try
            {
                ExceptionLogger.ClearAll();
                return ApiResponse.Success();

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


        [HttpGet("/api/exception/endpoints")]
        public ApiResponse GetEndpointsCbo()
        {
            return ApiResponse.Payload(ExceptionLogger.Endpoints);
        }


        [HttpGet("/api/exception/app-titles")]
        public ApiResponse GetAppTitlesCbo()
        {
            return ApiResponse.Payload(ExceptionLogger.AppTitles);
        }

    }
}