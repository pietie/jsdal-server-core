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
    public class ServerMethodsController : Controller
    {
        [HttpGet("/server-api")]
        public ApiResponse GetServerMethodCollection()
        {
            try
            {
                var q = from p in SettingsInstance.Instance.InlinePlugins
                        where p.Type == PluginType.ServerMethod
                        select new
                        {
                            p.Id,
                            p.IsValid,
                            p.Name,
                            p.Description
                        };

                return ApiResponse.Payload(q);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        [HttpPost("/server-api/{id?}")]
        public async Task<ApiResponse> AddUpdateServerMethodCollection([FromRoute] string id, dynamic bodyIgnored)
        {
            try
            {
                string code = null;

                using (var sr = new System.IO.StreamReader(this.Request.Body))
                {
                    code = sr.ReadToEnd();

                    var (success, ret) = await CSharpCompilerHelper.Evaluate(code);

                    if (!success) return ret;
                }

                if (!CSharpCompilerHelper.ParseAgainstBase<jsdal_plugin.ServerMethodPlugin>(code, out var pluginName, out var pluginGuid, out var pluginDesc, out var problems))
                {
                    return ApiResponse.Payload(new { CompilationError = problems });
                }


// TODO: if server-method is add or UPDATED we need to refresh/recompile a version in memory that is used when doing the actual execution
// We also need to cache metadata for those
                if (id == null)
                {
                    var plugin = ServerMethodPlugin.Create(code, pluginName, pluginGuid, pluginDesc, true/*TODO:?!?!?!?!*/);

                    var ret = SettingsInstance.Instance.AddInlinePlugin(plugin);

                    if (ret.isSuccess)
                    {
                        SettingsInstance.SaveSettingsToFile();
                    }
                    else
                    {
                        return ApiResponse.ExclamationModal(ret.userErrorVal);
                    }
                }


                return ApiResponse.Success();
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }

        }

        [HttpDelete("/server-api/{id}")]
        public ApiResponse DeleteServerMethodCollection([FromRoute] string id)
        {
            try
            {
                var ret = SettingsInstance.Instance.DeleteInlinePlugin(id);

                if (ret.isSuccess)
                {
                    SettingsInstance.SaveSettingsToFile();
                    return ApiResponse.Success();
                }
                else
                {
                    return ApiResponse.ExclamationModal(ret.userErrorVal);
                }
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }


    }
}