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
        public ApiResponse getException([FromRoute] string id)
        {
            try
            {
                var ex = ExceptionLogger.getException(id);

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


        [HttpGet("/api/exception/top/{n}")]
        public ApiResponse getRecentExceptions([FromRoute] int n)
        {
            try
            {
                if (n > 500) n = 500;

                var ret = ExceptionLogger.getTopN(n).OrderByDescending(x => x.created.Ticks);

                return ApiResponse.Payload(new
                {
                    Results = ret,
                    TotalExceptionCnt = ExceptionLogger.TotalCnt
                });
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        [HttpPost("/api/exception/clear-all")]
        public ApiResponse clearAll()
        {
            try
            {
                ExceptionLogger.clearAll();
                return ApiResponse.Success();

            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

    }
}