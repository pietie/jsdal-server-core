using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using shortid;

namespace jsdal_server_core
{

    // See System.Data.SqlClient.TdsEnums https://referencesource.microsoft.com/#System.Data/fx/src/data/System/Data/SqlClient/TdsEnums.cs,0146f11d0456012d
    [Serializable]
    public enum SqlErrorType
    {
        Timeout = -2,
        LoginFailed = 18456,
        PasswordExpired = 18488,
        ImpersonationFailed = 1346,
        TokenTooLong = 103,

    }
    [Serializable]
    public class ExceptionWrapper
    {
        public DateTime created;
        public string id;
        public string appTitle;

        // SQL-specific  stuff
        public string procedure;
        public string server;
        public int? line;
        public int? errorCode;
        public byte? level;
        public byte? state;

        public SqlErrorType? sqlErrorType;
        ///

        public string message;

        public ExceptionWrapper GetRelated(string id)
        {
            if (this.related == null) return null;
            return related.FirstOrDefault(e => e.id == id);
        }

        public string stackTrace;

        public ExceptionWrapper innerException;

        public Controllers.ExecController.ExecOptions execOptions;

        public string type; // Exception Object class TypeName

        public List<ExceptionWrapper> related;

        public ExceptionWrapper()
        {
        }

        public ExceptionWrapper(Exception ex, string additionalInfo = null, string appTitle = null)
        {
            this.created = DateTime.Now;
            this.appTitle = appTitle;
            this.type = ex.GetType().FullName;

            var msg = ex.Message;

            if (ex is SqlException)
            {
                SqlException re = (SqlException)ex;

                if (Enum.IsDefined(typeof(SqlErrorType), re.Number))
                {
                    this.sqlErrorType = (SqlErrorType)re.Number;
                }



                // TODO: There could be multiple re.Errors present..do something with that info? Seems those exception messages get concatted to main one anyway
                //!msg = $"Procedure ##{re.Procedure}##, Line {re.LineNumber}, Message: {re.Message}, Error {re.Number}, Level {re.Class}, State {re.State}";

                this.server = re.Server;
                this.procedure = re.Procedure;
                this.line = re.LineNumber;
                this.message = re.Message;
                this.errorCode = re.Number;
                this.level = re.Class;
                this.state = re.State;
            }


            this.id = ShortId.Generate(useNumbers: true, useSpecial: false, length: 6);
            this.message = msg;
            this.stackTrace = ex.StackTrace;

            if (ex.InnerException != null)
            {
                this.innerException = new ExceptionWrapper(ex.InnerException);
            }
        }
        public ExceptionWrapper(Exception ex, Controllers.ExecController.ExecOptions eo, string additionalInfo = null, string appTitle = null) : this(ex, additionalInfo, appTitle)
        { // TODO: do something interesting with additionalInfo

            this.execOptions = eo;
        }

        public bool HasAppTitle(string[] appTitleLookup)
        {
            if (appTitleLookup == null || appTitleLookup.Length == 0) return true;
            if (string.IsNullOrWhiteSpace(this.appTitle)) return false;

            return appTitleLookup.FirstOrDefault(t => t.Equals(this.appTitle, StringComparison.OrdinalIgnoreCase)) != null;
        }

        public void AddRelated(ExceptionWrapper ew)
        {
            if (related == null) related = new List<ExceptionWrapper>();

            lock (related)
            {
                related.Add(ew);
            }

        }
    }
}