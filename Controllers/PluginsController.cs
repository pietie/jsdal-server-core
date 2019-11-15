using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
//using Newtonsoft.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using jsdal_server_core.Settings.ObjectModel.Plugins.InlinePlugins;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class PluginsController : Controller
    {
        [HttpPost("/inline-plugin/{id?}")]
        public async Task<ApiResponse> AddUpdate([FromRoute] string id, dynamic bodyIgnored)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id)) id = null;

                string name = null;
                string description = null;
                string code = null;
                string error = null;

                var codeProblems = new List<string>();

                using (var sr = new System.IO.StreamReader(this.Request.Body))
                {
                    string bodyContent = sr.ReadToEnd();
                    var bodyJson = JsonSerializer.Deserialize<JsonElement>(bodyContent);

                    if (bodyJson.TryGetProperty("name", out var nameProperty))
                    {
                        name = nameProperty.GetString();
                    }

                    if (bodyJson.TryGetProperty("description", out var descProperty))
                    {
                        description = descProperty.GetString();
                    }

                    if (bodyJson.TryGetProperty("code", out var codeProperty))
                    {
                        code = codeProperty.GetString();
                    }

                    if (string.IsNullOrWhiteSpace(name)) name = null;
                    if (string.IsNullOrWhiteSpace(description)) description = null;
                    if (string.IsNullOrWhiteSpace(code)) code = null;
                }

                (error, id, codeProblems) = await InlinePluginManager.Instance.AddUpdateModuleAsync(id, name, description, code);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return ApiResponse.ExclamationModal(error);
                }

                return ApiResponse.Payload(new { id = id, CompilationError = codeProblems });
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpGet("/inline-plugin/{id}")]
        public ApiResponse GetInlinePluginModuleSource([FromRoute] string id)
        {
            try
            {
                var ret =  PluginLoader.Instance.GetInlinePluginModuleSource(id, out var source);

                if (ret.IsSuccess)
                {
                    return ApiResponse.Payload(source);
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