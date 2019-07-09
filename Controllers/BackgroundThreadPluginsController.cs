using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class BackgroundThreadPluginsController : Controller
    {
        private readonly BackgroundThreadManager _bgThreadManager;
        public BackgroundThreadPluginsController(BackgroundThreadManager btm)
        {
            this._bgThreadManager = btm;
        }

        [HttpGet("/api/bgthreads")]
        public ApiResponse GetLoadedBackgroundThreadPlugins()
        {
            if (_bgThreadManager == null) return null;



            return null;
        }
    }
}