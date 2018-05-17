using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class ProjectController : Controller
    {
        [HttpGet("/api/project")]
        public ApiResponse Get()
        {
            return ApiResponse.Payload(SettingsInstance.Instance.ProjectList.Select(p =>
            {
                return new
                {
                    Name = p.Name,
                    NumberOfDatabaseSources = p.Applications.Count
                };
            }));
        }

        [HttpPost("/api/project")]
        public ApiResponse CreateNewProject([FromBody] string name)
        {
            var ret = SettingsInstance.Instance.AddProject(name);

            if (ret.isSuccess)
            {
                SettingsInstance.saveSettingsToFile();
                return ApiResponse.Success();
            }
            else
            {
                return ApiResponse.ExclamationModal(ret.userErrorVal);
            }
        }

        [HttpPut("/api/project/{name}")]
        public ApiResponse UpdateProject([FromRoute] string name, [FromBody] string newName) // TODO: clean up interface...get some consistency
        {
            var ret = SettingsInstance.Instance.UpdateProject(name, newName);

            if (ret.isSuccess)
            {
                SettingsInstance.saveSettingsToFile();
                return ApiResponse.Success();
            }
            else
            {
                return ApiResponse.ExclamationModal(ret.userErrorVal);
            }
        }

        [HttpDelete("/api/project")]
        public ApiResponse Delete([FromBody] string name)
        {
            var ret = SettingsInstance.Instance.DeleteProject(name);

            if (ret.isSuccess)
            {
                SettingsInstance.saveSettingsToFile();
                return ApiResponse.Success();
            }
            else
            {
                return ApiResponse.ExclamationModal(ret.userErrorVal);
            }
        }
    }
}