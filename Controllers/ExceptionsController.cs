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
        public ApiResponse GetException([FromRoute] string id, [FromQuery(Name = "parent")] string parentId)
        {
            try
            {
                ExceptionWrapper ret = null;

                if (!string.IsNullOrWhiteSpace(parentId))
                {
                    var parent = ExceptionLogger.GetException(parentId);

                    if (parent == null)
                    {
                        return ApiResponse.ExclamationModal($"A parent exception with id \"{parentId}\" could not be found.");
                    }

                    var child = parent.GetRelated(id);

                    if (child == null)
                    {
                        return ApiResponse.ExclamationModal($"An exception with id \"{id}\" could not be found.");
                    }

                    ret = child;
                }
                else
                {
                    var ex = ExceptionLogger.GetException(id);

                    if (ex == null)
                    {
                        ex = ExceptionLogger.DeepFindRelated(id);
                    }

                    if (ex == null)
                    {
                        return ApiResponse.ExclamationModal($"An exception with id \"{id}\" could not be found.");
                    }

                    ret = ex;
                }

                return ApiResponse.Payload(new
                {
                    ret.appTitle,
                    ret.appVersion,
                    ret.created,
                    ret.errorCode,
                    ret.execOptions,
                    ret.id,
                    ret.innerException,
                    ret.level,
                    ret.line,
                    ret.message,
                    ret.procedure,
                    ret.server,
                    //?ret.sqlErrorType,
                    ret.stackTrace,
                    ret.state,
                    ret.type

                });
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpGet("/api/exception/{id}/related")]
        public ApiResponse GetExceptionRelated([FromRoute] string id)
        {
            try
            {
                var ex = ExceptionLogger.GetException(id);

                if (ex == null)
                {
                    return ApiResponse.ExclamationModal($"An exception with id \"{id}\" could not be found.");
                }

                var ret = (from exception in ex.related
                           select new
                           {
                               exception.id,
                               exception.created,
                               message = exception.message.Left(200, true), // limit exception message length to something reasonable
                               exception.procedure,
                               exception.appTitle,
                               exception.appVersion,
                               relatedCount = exception.related?.Count ?? 0
                           }
                );

                return ApiResponse.Payload(ret);
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
                          select new
                          {
                              exception.id,
                              exception.created,
                              message = exception.message.Left(200, true), // limit exception message length to something reasonable
                              exception.procedure,
                              exception.appTitle,
                              exception.appVersion,
                              relatedCount = exception.related?.Count ?? 0
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