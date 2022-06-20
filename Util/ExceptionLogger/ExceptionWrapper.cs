using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.SqlClient;
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
        public string EndpointKey { get; set; }
        public int Id { get; set; } // auto set by LiteDB

        public DateTime created { get; set; }
        public string sId { get; set; }
        public string appTitle { get; set; }
        public string appVersion { get; set; }

        // SQL-specific  stuff
        public string procedure { get; set; }
        public string server { get; set; }
        public int? line { get; set; }
        public int? errorCode { get; set; }
        public byte? level { get; set; }
        public byte? state { get; set; }

        public SqlErrorType? sqlErrorType { get; set; }
        ///

        public string message { get; set; }
        public string additionalInfo { get; set; }

        public string stackTrace { get; set; }

        public ExceptionWrapper innerException { get; set; }

        public Controllers.ExecController.ExecOptions execOptions { get; set; }

        public string type { get; set; } // Exception Object class TypeName

        public List<ExceptionWrapper> _related { get; set; }

        public ExceptionWrapper GetRelated(string sId)
        {
            if (this._related == null) return null;
            return _related.FirstOrDefault(e => e.sId == sId);
        }

        public ExceptionWrapper()
        {
        }

        public ExceptionWrapper(Exception ex, string additionalInfo = null, string appTitle = null, string appVersion = null)
        {
            this.created = DateTime.Now;
            this.appTitle = appTitle;
            this.appVersion = appVersion;
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
                this.message = re.Message.Left(1024, true); // limit message length to something reasonable
                this.errorCode = re.Number;
                this.level = re.Class;
                this.state = re.State;
            }

            this.sId = ShortId.Generate(useNumbers: true, useSpecial: false, length: 7);
            this.message = msg;
            this.additionalInfo = additionalInfo;
            this.stackTrace = ex.StackTrace;

            if (ex.InnerException != null)
            {
                this.innerException = new ExceptionWrapper(ex.InnerException);
            }
        }
        public ExceptionWrapper(Exception ex, Controllers.ExecController.ExecOptions eo, string additionalInfo = null, string appTitle = null, string appVersion = null)
                    : this(ex, additionalInfo, appTitle, appVersion)
        {
            this.execOptions = eo;
            this.additionalInfo = additionalInfo;
        }

        public bool HasAppTitle(string[] appTitleLookup)
        {
            if (appTitleLookup == null || appTitleLookup.Length == 0) return true;
            if (string.IsNullOrWhiteSpace(this.appTitle)) return false;

            return appTitleLookup.FirstOrDefault(t => t.Equals(this.appTitle, StringComparison.OrdinalIgnoreCase)) != null;
        }

        public bool AddRelated(ExceptionWrapper ew)
        {
            if (_related == null) _related = new List<ExceptionWrapper>();

            // cap related items to 20 max
            if (_related.Count >= 20) return false;

            lock (_related)
            {
                _related.Add(ew);

                return true;
            }

        }
    }
}