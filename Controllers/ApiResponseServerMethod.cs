using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace jsdal_server_core
{
    public class ApiResponseServerMethodBase
    {
        public ApiResponseServerMethodBase()
        {
            this.ApiVersion = "SM1.0";
        }
        public ApiResponseType Type { get; set; }
        public string Error { get; set; }
        public string ApiVersion { get; set; }
        public Dictionary<string, string> OutputParms { get; set; }

        public static ApiResponseServerMethodBase Exception(Exception ex)
        {
            SessionLog.Exception(ex);

            // TODO: Record ServerMethod detail
            var id = ExceptionLogger.LogException(ex, (Controllers.ExecController.ExecOptions)null);

            var ret = new ApiResponseServerMethodBase();

            ret.Error = $"Error ref: {id}";
            ret.Type = ApiResponseType.Exception;
            
            return ret;
        }

        // public static implicit operator string(ApiResponseServerMethodBase r)
        // {
        //     return JsonConvert.SerializeObject(r);
        // }
    }

    public class ApiResponseServerMethodResult : ApiResponseServerMethodBase
    {
        public object Result { get; set; }

        public static ApiResponseServerMethodResult Success(object result, Dictionary<string, string> outputs)
        {
            return new ApiResponseServerMethodResult() { Type = ApiResponseType.Success, Result = result, OutputParms = outputs };
        }
    }

    public class ApiResponseServerMethodVoid : ApiResponseServerMethodBase
    {
        public static ApiResponseServerMethodVoid Success(Dictionary<string, string> outputs)
        {
            return new ApiResponseServerMethodVoid() { Type = ApiResponseType.Success, OutputParms = outputs };
        }
    }
}