using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Enc = System.Text.Encoding;

namespace jsdal_server_core
{
    public class UptimeLogger
    {
        private static string FullFilePath { get; set; }
        private const int RECORD_LENGTH = 25;

        static UptimeLogger()
        {
            try
            {
                var path = $"./log";

                if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                var filename = $"run.log";
                FullFilePath = Path.Combine(path, filename);
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex);
            }
        }
        public static async Task LogServerUptimeAsync()
        {
            try
            {
                using (var fs = File.Open(FullFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    fs.Seek(0, SeekOrigin.End);

                    async Task WriteTotalSecondsAsync()
                    {
                        var totalSeconds = (int)DateTime.Now.Subtract(Program.StartDate.Value).TotalSeconds;
                        var str = totalSeconds.ToString().PadLeft(10);
                        var buffer = Enc.UTF8.GetBytes(str);
                        await fs.WriteAsync(buffer, 0, buffer.Length);
                        await fs.FlushAsync(Program.CTS.Token);
                    }

                    var buffer = Enc.UTF8.GetBytes($"{DateTime.Now:yyyyMMddHHmmss}");

                    // write start date & time
                    await fs.WriteAsync(buffer, 0, buffer.Length);

                    var pos = fs.Position;

                    await WriteTotalSecondsAsync();

                    buffer = Enc.UTF8.GetBytes("\n");

                    await fs.WriteAsync(buffer, 0, buffer.Length);

                    while (!Program.CTS.IsCancellationRequested)
                    {
                        // move back to just after date & time
                        fs.Seek(pos, SeekOrigin.Begin);

                        await WriteTotalSecondsAsync();

                        await Task.Delay(5000, Program.CTS.Token);
                    }
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex);
            }
        }

        public static List<UptimeEntry> GetRecentHistory()
        {
            List<UptimeEntry> ret = new();
            int numOfRecords = 10;

            using (var fs = File.Open(FullFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var startPos = fs.Length - (RECORD_LENGTH * numOfRecords);

                if (startPos < 0) startPos = 0;

                while (startPos + RECORD_LENGTH <= fs.Length)
                {
                    fs.Seek(startPos, SeekOrigin.Begin);

                    var buffer = new byte[RECORD_LENGTH];

                    fs.Read(buffer, 0, buffer.Length);

                    var line = Enc.UTF8.GetString(buffer);

                    if (DateTime.TryParseExact(line.Left(14), "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        if (int.TryParse(line.Substring(14, 10), out var totalSeconds))
                        {
                            ret.Add(new() { Date = dt, TotalSeconds = totalSeconds });
                        }
                    }

                    startPos += RECORD_LENGTH;
                }
            }

            return ret;
        }
    }

    public class UptimeEntry
    {
        public DateTime Date { get; set; }
        public int TotalSeconds { get; set; }
    }
}