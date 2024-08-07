using System;
using System.Linq;
using System.Diagnostics;
using Serilog;

namespace jsdal_server_core
{
    public class NetshWrapper
    {
        public static Process CreateNewProcess(string args)
        {
            var proc = new Process();
            var startInfo = new ProcessStartInfo();

            startInfo.FileName = System.IO.Path.Combine(System.Environment.SystemDirectory, "netsh.exe");
            startInfo.Arguments = args;


            Log.Information($"\r\n\tnetsh.exe {args}\r\n");

            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            //startInfo.Verb = "runas";

            proc.StartInfo = startInfo;
            proc.EnableRaisingEvents = true;

            return proc;
        }

        public static void Unregister(string hostname, int port)
        {
            var proc = CreateNewProcess($"http delete sslcert hostnameport={hostname}:{port}");

            proc.Start();
            proc.WaitForExit();
            var consoleOutput = proc.StandardError.ReadToEnd();
            var output = proc.StandardOutput.ReadToEnd();
            Log.Error("§ERROR:\t{0}", output);
        }

        public static void Register(string hostname, int port, string certHash)
        {
            var proc = CreateNewProcess($"http add sslcert hostnameport={hostname}:{port} certhash={certHash} appid={{3F0EEDC1-E386-4FA0-87AD-A195003BEE2F}} certstore=my");

            proc.Start();
            proc.WaitForExit();
            var consoleOutput = proc.StandardError.ReadToEnd();
            var output = proc.StandardOutput.ReadToEnd();
            Log.Error("§ERROR:\t{0}", output);
        }

        public static bool AddUrlToACL(bool isHttps, string hostname, int port)
        {
            var protocol = "http" + (isHttps ? "s" : "");
            var proc = CreateNewProcess($"http add urlacl url={protocol}://{hostname}:{port}/ user=Users");

            proc.Start();
            proc.WaitForExit();
            var consoleOutput = proc.StandardError.ReadToEnd();
            var output = proc.StandardOutput.ReadToEnd();

            Log.Error("§ERROR:\t{0}", output);

            return output?.ToLower().Contains("url reservation successfully added", StringComparison.CurrentCultureIgnoreCase) ?? false;
        }

        public static string ShowUrlAcl(bool isHttps, string hostname, int port)
        {
            var protocol = "http" + (isHttps ? "s" : "");
            var proc = CreateNewProcess($"http show urlacl url={protocol}://{hostname}:{port}/");

            proc.Start();
            proc.WaitForExit();

            var consoleOutput = proc.StandardError.ReadToEnd();
            var output = proc.StandardOutput.ReadToEnd();

            return output;
        }

        public static string ShowSslCert(string hostname, int port)
        {
            var proc = CreateNewProcess($"http show sslcert hostnameport=\"{hostname}:{port}\"");

            proc.Start();
            proc.WaitForExit();

            var consoleOutput = proc.StandardError.ReadToEnd();
            var output = proc.StandardOutput.ReadToEnd();

            return output;
        }

        public static bool ValidateSSLCertBinding(string hostname, int port)
        {
            try
            {
                var output = ShowSslCert(hostname, port);

                if (output == null) return false;

                output = output.ToLower();

                if (output.Contains("the system cannot find the file specified")) return false;

                return true;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return false;
            }
        }

        public static bool ValidateUrlAcl(bool isHttps, string hostname, int port)
        {
            try
            {
                var url = $"http{(isHttps ? "s" : "")}://{hostname}:{port}".ToLower();
                var acl = ShowUrlAcl(isHttps, hostname, port);

                var lines = acl.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

                var l = lines.FirstOrDefault(ln => ln.ToLower().Contains("reserved url"));

                if (l != null)
                {
                    return l.ToLower().Contains(url);
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                return false;
            }
        }

        //netsh http add sslcert hostnameport=XXXX:443 certhash=MyCertHash_Here appid={00000000-0000-0000-0000-000000000000}".
    }
}