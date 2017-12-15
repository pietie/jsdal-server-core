using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using shortid;

namespace jsdal_server_core
{

    public class ExceptionLogger
    {
        private static readonly int MAX_ENTRIES = 1000;
        private static List<ExceptionWrapper> exceptionList = new List<ExceptionWrapper>();

        private static string ExceptionFilePath = "./data/exceptions.lst";

        private static void SaveToFile()
        {
            try
            {
                lock (exceptionList)
                {
                    var fi = new FileInfo(ExceptionFilePath);

                    if (!fi.Directory.Exists)
                    {
                        fi.Directory.Create();
                    }

                    var json = JsonConvert.SerializeObject(exceptionList);
                    File.WriteAllText(ExceptionFilePath, json, System.Text.Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.logException(ex);
            }
        }

        private static bool LoadFromFile()
        {
            try
            {
                lock (exceptionList)
                {
                    if (!File.Exists(ExceptionFilePath)) return false;

                    var data = File.ReadAllText(ExceptionFilePath, System.Text.Encoding.UTF8);

                    if (data != null)
                    {
                        exceptionList = JsonConvert.DeserializeObject<List<ExceptionWrapper>>(data);

                    }



                    return true;
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.logException(ex);
                return false;
            }

        }

        public static void Init()
        {
            LoadFromFile();
        }

        public static List<ExceptionWrapper> Exceptions
        {
            get { return ExceptionLogger.exceptionList; }
        }

        public static ExceptionWrapper getException(string id)
        {
            return ExceptionLogger.exceptionList.FirstOrDefault(e => e.id == id);
        }

        public static IEnumerable<ExceptionWrapper> getTopN(int n)
        {
            if (n <= 0) return new List<ExceptionWrapper>();
            return ExceptionLogger.exceptionList.OrderByDescending(e => e.created).Take(Math.Min(n, exceptionList.Count));//.ToList().OrderByDescending(e=>e.created);
        }

        public static void clearAll()
        {
            ExceptionLogger.exceptionList.Clear();
            SaveToFile();
        }

        public static int TotalCnt
        {
            get
            {
                if (ExceptionLogger.exceptionList == null) return 0;
                return ExceptionLogger.exceptionList.Count;
            }
        }

        public static string logException(Exception ex, string additionalInfo = null, string appTitle = null)
        {
            lock (exceptionList)
            {
                if (ExceptionLogger.exceptionList.Count >= ExceptionLogger.MAX_ENTRIES)
                {
                    // cull from the front
                    ExceptionLogger.exceptionList.RemoveRange(0, ExceptionLogger.exceptionList.Count - ExceptionLogger.MAX_ENTRIES + 1);
                }

                var ew = new ExceptionWrapper(ex, additionalInfo, appTitle);

                ExceptionLogger.exceptionList.Add(ew);

                // TODO: Really save on each exception logged?
                SaveToFile();

                return ew.id;
            }
        }
    }


    [Serializable]
    public class ExceptionWrapper
    {
        public DateTime created;
        //!public Exception exceptionObject;
        public string id;
        public string appTitle;

        public string message;
        public string stackTrace;

        public ExceptionWrapper innerException;

        public ExceptionWrapper()
        {

        }
        public ExceptionWrapper(Exception ex, string additionalInfo = null, string appTitle = null)
        { // TODO: do something intersting with additionalInfo
            this.created = DateTime.Now;
            this.appTitle = appTitle;

            var msg = ex.Message;

            if (ex is SqlException)
            {
                SqlException re = (SqlException)ex;

                msg = $"Procedure ##{re.Procedure}##, Line {re.LineNumber}, Message: {re.Message}, Error {re.Number}, Level {re.Class}, State {re.State}";
            }


            this.id = ShortId.Generate(useNumbers: true, useSpecial:true, length: 6);
            this.message = msg;
            this.stackTrace = ex.StackTrace;

            if (ex.InnerException != null)
            {
                this.innerException = new ExceptionWrapper(ex.InnerException);
            }
        }

    }

}