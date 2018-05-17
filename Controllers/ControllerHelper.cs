using System;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core
{
    public class ControllerHelper
    {

        public static bool GetProject(string projectName, out Project project, out ApiResponse resp)
        {
            project = SettingsInstance.Instance.getProject(projectName);
            resp = null;

            if (project == null)
            {
                resp = ApiResponse.ExclamationModal($"The project \"{projectName}\" does not exist.");
            }

            return project != null;
        }

        public static bool GetApplication(Project project, string appName, out Application app, out ApiResponse resp)
        {
            app = project.getDatabaseSource(appName);
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

        /*


            

         */

    }
}