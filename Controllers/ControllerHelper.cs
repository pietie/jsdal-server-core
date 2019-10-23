using System;
using System.Collections.Generic;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core
{
    public static class ControllerHelper
    {

        public static bool GetProject(string projectName, out Project project, out ApiResponse resp)
        {
            project = SettingsInstance.Instance.GetProject(projectName);
            resp = null;

            if (project == null)
            {
                resp = ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");
            }

            return project != null;
        }

        public static bool GetApplication(Project project, string appName, out Application app, out ApiResponse resp)
        {
            app = project.GetApplication(appName);
            resp = null;

            if (app == null)
            {
                resp = ApiResponse.ExclamationModal($"The app \"{appName}\" does not exist on the project \"{project.Name}\".");
            }

            return app != null;
        }

        public static bool GetProjectAndApp(string projectName, string appName, out Project project, out Application app, out ApiResponse resp)
        {
            project = null;
            app = null;
            resp = null;

            if (!ControllerHelper.GetProject(projectName, out project, out resp))
            {
                return false;
            }

            return ControllerHelper.GetApplication(project, appName, out app, out resp);

        }

        public static bool GetProjectAndAppAndEndpoint(string projectName, string appName, string endpointName, out Project project, out Application app, out Endpoint endpoint, out ApiResponse resp)
        {
            project = null;
            app = null;
            endpoint = null;
            resp = null;

            if (!ControllerHelper.GetProject(projectName, out project, out resp))
            {
                return false;
            }

            if (!ControllerHelper.GetApplication(project, appName, out app, out resp))
            {
                return false;
            }

            if (!app.GetEndpoint(endpointName, out endpoint, out var retVal))
            {
                resp = ApiResponse.ExclamationModal($"Failed to retrieve an endpoint from {projectName ?? "(null)"}/{appName ?? "(null)"}/{endpointName ?? "(null)"}");
            }


            return endpoint != null;
        }

        public static bool GetUnifiedCacheListWithApiResponse(this Application app, out List<CachedRoutine> allRoutines, out ApiResponse resp)
        {
            allRoutines = app.GetUnifiedCacheList();
            resp = null;

            if (allRoutines == null || allRoutines.Count == 0)
            {
                resp = ApiResponse.ExclamationModal("Routine cache does not exist. Make sure the worker thread is running and that it is able to access the database.");
                return false;
            }

            return true;
        }

    }
}