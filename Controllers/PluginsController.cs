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
        public ApiResponse AddUpdate([FromRoute] string id, dynamic bodyIgnored)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id)) id = null;

                string name = null;
                string description = null;
                string code = null;

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

                var assembly = CSharpCompilerHelper.CompileIntoAssembly(name, code, out var codeProblems);

                if (codeProblems.Count == 0)
                {
                    InlineModuleManifestEntry existingEntry = null;

                    // look for existing inline module
                    if (id != null)
                    {
                        existingEntry = InlineModuleManifest.Instance.GetEntryById(id);

                        if (existingEntry == null)
                        {
                            return ApiResponse.ExclamationModal($"Inline module with id {id} not found");
                        }

                        PluginLoader.Instance.LoadOrUpdateInlineAssembly(existingEntry.Id, assembly, out codeProblems);

                    }
                }
                else
                {
                    return ApiResponse.Payload(new { id = id, CompilationError = codeProblems });
                }

                if (codeProblems.Count == 0/* || saveAnyway*/)
                {
                    InlineModuleManifest.Instance.AddUpdateSource(id, name, description, code);
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
                var ret = PluginLoader.Instance.GetInlinePluginModuleSource(id, out var inlineEntry, out var source);

                if (ret.IsSuccess)
                {
                    return ApiResponse.Payload(new
                    {
                        Name = inlineEntry.Name,
                        Description = inlineEntry.Description,
                        Source = source
                    });
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

        [HttpGet("/plugins/diagnostics")]
        public IActionResult GetDiagnosticInfo()
        {
            try
            {
                var q = from pa in PluginLoader.Instance.PluginAssemblies
                        select new
                        {
                            pa.Assembly.FullName,
                            pa.IsInline,
                            pa.InlineEntryId,
                            pa.InstanceId,
                            Plugins = (from p in pa.Plugins
                                       select new
                                       {
                                           p.Name,
                                           p.Description,
                                           p.Guid,
                                           Type = p.Type.ToString()
                                           // TODO: CNT of Endpoints that use this plugin
                                       })
                        };



                return Ok(q);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }
        }


    }
}