using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using jsdal_server_core;

namespace jsdal_server_core.Controllers
{

    [Authorize(Roles = "admin")]
    public class BlobController : Controller
    {
        [HttpGet("/api/blob/stats")]
        public ApiResponse GetBlobStats()
        {
            try
            {
                return ApiResponse.Payload(BlobStore.Instance.GetStats());
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }

        }

        [HttpGet("/api/blob")]
        public ApiResponse Get([FromQuery] int? top)
        {
            try
            {
                if (!top.HasValue) top = 20;
                if (top > 800) top = 800;

                var list = BlobStore.Instance.GetTopN(top.Value);

                return ApiResponse.Payload(from blob in list
                                           select new
                                           {
                                               blob.Ref,
                                               blob.ContentType,
                                               blob.ExpiryDate,
                                               blob.Filename,
                                               HasExpired = blob.ExpiryDate.HasValue && blob.ExpiryDate.Value <= DateTime.Now,
                                               blob.Size
                                           });
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }

        }

        [HttpGet("/api/blob/{blobRef}")]
        public ApiResponse Get([FromRoute] string blobRef)
        {
            try
            {
                var blob = BlobStore.Instance.GetBlobByRef(blobRef);
                //if (blob ==null) return NotFound();
                return ApiResponse.Payload(new
                {
                    blob.Ref,
                    blob.ContentType,
                    blob.ExpiryDate,
                    blob.Filename,
                    HasExpired = blob.ExpiryDate.HasValue && blob.ExpiryDate.Value <= DateTime.Now,
                    blob.Size
                });
            }
            catch (Exception e)
            {
                return ApiResponse.Exception(e);
            }

        }
    }
}