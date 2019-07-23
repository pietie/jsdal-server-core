using System;
using System.Linq;
using jsdal_server_core.PluginManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class BackgroundThreadPluginsController : Controller
    {
        private readonly BackgroundThreadPluginManager _bgThreadManager;
        public BackgroundThreadPluginsController(BackgroundThreadPluginManager btm)
        {
            this._bgThreadManager = btm;
        }

        [HttpPost("/api/bgthreads/{instanceId}/start")]
        public ApiResponse StartInstance(string instanceId)
        {
            var instance = this._bgThreadManager.Registrations.SelectMany(reg => reg.GetLoadedInstances()).FirstOrDefault(inst => inst.Id.Equals(instanceId, StringComparison.Ordinal));

            if (instance == null) return ApiResponse.ExclamationModal("The specified instance was not found.");

            instance.Plugin.Start();

            return ApiResponse.Success();
        }

        [HttpPost("/api/bgthreads/{instanceId}/stop")]
        public ApiResponse StopInstance(string instanceId)
        {
            var instance = this._bgThreadManager.Registrations.SelectMany(reg => reg.GetLoadedInstances()).FirstOrDefault(inst => inst.Id.Equals(instanceId, StringComparison.Ordinal));

            if (instance == null) return ApiResponse.ExclamationModal("The specified instance was not found.");

            instance.Plugin.Stop();

            return ApiResponse.Success();
        }

        [HttpGet("/api/bgthreads/{pluginGuid}/all-config")]
        public ApiResponse GetAllConfigs(string pluginGuid)
        {
            // TODO: Performance enhancement - _bgThreadManager.Registrations needs to index registrations by InstanceId and then we can just call bgThreadManager.GetByInstanceId
            //var instance = this._bgThreadManager.Registrations.SelectMany(reg => reg.GetLoadedInstances()).FirstOrDefault(inst => inst.Id.Equals(instanceId, StringComparison.Ordinal));
            var pluginReg = this._bgThreadManager.Registrations.FirstOrDefault(reg => reg.PluginGuid.Equals(pluginGuid, StringComparison.OrdinalIgnoreCase));

            if (pluginReg == null) return ApiResponse.ExclamationModal("The specified plugin was not found.");

            var runningInstances = (from inst in pluginReg.GetLoadedInstances()
                                    select new
                                    {
                                        InstanceId = inst.Id,
                                        App = $"{inst.Endpoint.Application.Project.Name}/{inst.Endpoint.Application.Name}",
                                        Endpoint = inst.Endpoint.Name,
                                        Plugin = inst.Plugin
                                    }).ToList();

            // TODO: What do we do if there are no running instances? With no running instance we cannot request the default config

            if (runningInstances.Count == 0)
            {
                return ApiResponse.ExclamationModal("Can only configure config keys for plugins with at least one instance. You need to enable the plugin on at least one App.");
            }


            var defaultConfig = runningInstances[0].Plugin.GetDefaultConfig();

            var instancesGroups = (from inst in runningInstances
                                   group new { /* inst.App, */inst.Endpoint, inst.InstanceId, Display = true } by inst.App into appGroup
                                   select appGroup).ToDictionary(g => g.Key, g => g.ToList());

            var ret = new
            {
                Instances = instancesGroups,
                Default = defaultConfig,
                Plugin = defaultConfig,
                App = defaultConfig,
                Endpoint = defaultConfig
            };

            return ApiResponse.Payload(ret);
        }

        // [HttpGet("/api/bgthreads")]
        // public ApiResponse GetLoadedBackgroundThreadPlugins()
        // {
        //     if (_bgThreadManager == null) return null;

        //     var ret = _bgThreadManager.Registrations
        //                 .SelectMany(reg => reg.GetLoadedInstances())
        //                 .Select(a => new
        //                 {
        //                     a.Name,
        //                     a.IsRunning,
        //                     a.EndpointPedigree,
        //                     a.Status,
        //                     a.Description
        //                 });


        //     return ApiResponse.Payload(ret);
        // }
    }
}