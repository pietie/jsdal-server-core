using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using Terminal.Gui;

namespace jsdal_server_core
{
    public class TerminalUI
    {
        public static void Init()
        {
            Application.Init();
            var top = Application.Top;

            var win = new Window(new Rect(0, 0, top.Frame.Width, top.Frame.Height - 0), "jsDAL Server");
            top.Add(win);

            win.Add(
                new Button(3, 2, "Install service")
                {
                    Clicked = () =>
                    {
                        var dlg = BuildCreateNewServiceDialog();

                        Application.Run(dlg);
                    }
                },

                new Button(3, 3, "Uninstall service")
                {
                    Clicked = () =>
                    {
                        var dlg = BuildUninstallServiceDialog();
                        Application.Run(dlg);

                    }
                },
                new Button(3, 4, "Exit")
                {
                    Clicked = () =>
                    {
                        Application.RequestStop();
                    }
                }
                );

            Application.Run();
        }

        static Dialog BuildCreateNewServiceDialog()
        {
            var ok = new Button("Install");
            var cancel = new Button("Cancel");

            cancel.Clicked += () => { Application.RequestStop(); };

            Dialog dlg = new Dialog("Install service", 50, 15, ok, cancel) { Width = Dim.Percent(80), Height = Dim.Percent(80) };

            var tf1 = new TextField("jsdal-server") { X = 15, Y = 1, Width = Dim.Fill(5) };
            var tf2 = new TextField("jsDAL Server 2.0") { X = 15, Y = 3, Width = Dim.Fill(5) };

            dlg.Add(
                     new Label(1, 1, "Service name"),
                     tf1,

                     new Label(1, 3, "Display name"),
                     tf2
                );

            ok.Clicked = () =>
            {
                InstallService(tf1.Text.ToString(), tf2.Text.ToString());
            };

            return dlg;
        }

        static Dialog BuildUninstallServiceDialog()
        {
            var cancel = new Button("Cancel");

            cancel.Clicked += () => { Application.RequestStop(); };

            Dialog dlg = new Dialog("Uninstall service", 50, 15, cancel) { Width = Dim.Percent(80), Height = Dim.Percent(80) };

            ServiceController[] scServices;

            scServices = ServiceController.GetServices();

            var jsdalLikeServices = scServices.Where(sc => sc.ServiceName.ToLower().Contains("jsdal")).ToList();
            var items = jsdalLikeServices.Select(s => $"({s.Status})  {s.DisplayName}").ToArray();

            dlg.Add(new Label(1, 1, "Select a jsdal service to uninstall:"));

            for (var i = 0; i < items.Length; i++)
            {
                var local = i;
                dlg.Add(new Button(1, 3 + i, items[i])
                {
                    Clicked = () =>
                    {
                        var service = jsdalLikeServices[local];
                        int ret = MessageBox.Query(80, 10, "Confirm action", $"Are you sure you want to uninstall '{service.ServiceName}'?", "Confirm", "Cancel");

                        if (ret == 0)
                        {
                            ManagementObject wmiService;

                            wmiService = new ManagementObject("Win32_Service.Name='" + service.ServiceName + "'");
                            wmiService.Get();
                            wmiService.Delete();


                            MessageBox.Query(30, 8, "", "Service uninstalled", "Ok");
                            Application.RequestStop();
                        }
                        else if (ret == 1)
                        {

                        }
                    }
                });
            }

            return dlg;
        }

        static void InstallService(string serviceName, string displayName)
        {
            var binPath = Path.Combine(Environment.CurrentDirectory, "jsdal-server.exe");
            var startInfo = new ProcessStartInfo();

            startInfo.FileName = $"{Environment.ExpandEnvironmentVariables("%WINDIR%")}\\system32\\sc.exe";
            startInfo.Arguments = $"create \"{serviceName}\" binPath= \"{binPath} --service\" start=auto DisplayName= \"{displayName}\"";
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            var process = new Process();

            process.StartInfo = startInfo;
            process.EnableRaisingEvents = true;

            process.Start();

            var consoleOutput = process.StandardOutput.ReadToEnd();

            if (consoleOutput.ToLower().Contains("createservice success"))
            {
                startInfo = new ProcessStartInfo();

                startInfo.FileName = $"{Environment.ExpandEnvironmentVariables("%WINDIR%")}\\system32\\sc.exe";
                startInfo.Arguments = $"description \"{serviceName}\" \"Generates a JavaScript data access layer from MSSQL routines. \"";
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;

                process = new Process();

                process.StartInfo = startInfo;
                process.EnableRaisingEvents = true;

                process.Start();

                consoleOutput = process.StandardOutput.ReadToEnd();

                if (consoleOutput.ToLower().Contains("changeserviceconfig2 success"))
                {

                    // start service
                    {
                        startInfo = new ProcessStartInfo();

                        startInfo.FileName = $"{Environment.ExpandEnvironmentVariables("%WINDIR%")}\\system32\\sc.exe";
                        startInfo.Arguments = $"start \"{serviceName}\"";
                        startInfo.RedirectStandardOutput = true;
                        startInfo.RedirectStandardError = true;
                        startInfo.UseShellExecute = false;
                        startInfo.CreateNoWindow = true;

                        process = new Process();

                        process.StartInfo = startInfo;
                        process.EnableRaisingEvents = true;

                        process.Start();
                        consoleOutput = process.StandardOutput.ReadToEnd();
                    }

                    MessageBox.Query(50, 10, "", "Service installed successfully", "Ok");
                    Application.RequestStop();
                }
                else
                {
                    MessageBox.ErrorQuery(80, 10, "Failed to create service", "\r\n" + consoleOutput.Replace("[SC]", "").Trim(), "Ok");
                }


            }
            else
            {
                MessageBox.ErrorQuery(80, 10, "Failed to create service", "\r\n" + consoleOutput.Replace("[SC]", "").Trim(), "Ok");
            }



        }
    }
}