using System;
using Newtonsoft.Json;

namespace jsdal_server_core
{
    public class ApiResponseScalar : ApiResponse
    {
        public bool IsDate { get; set; }

        public static ApiResponseScalar Payload(object data, bool isDate)
        {
            return new ApiResponseScalar() { Data = data, IsDate = isDate };
        }
    }

    public class ApiResponse
    {
        public ApiResponse()
        {
            this.ApiResponseVer = "1.0";
        }
        public string ApiResponseVer { get; set; }

        public string Message { get; set; }
        public string Title { get; set; }
        public ApiResponseType Type { get; set; }

        public object Data { get; set; }

        public static ApiResponse Success()
        {
            return new ApiResponse() { Type = ApiResponseType.Success };
        }

        public static ApiResponse ExclamationModal(string msg)
        {
            return new ApiResponse() { Message = msg, Type = ApiResponseType.ExclamationModal };
        }

        public static ApiResponse InformationToast(string msg, object data = null)
        {
            return new ApiResponse() { Message = msg, Type = ApiResponseType.InfoMsg, Data = data };
        }

        public static ApiResponse ExecException(Exception ex, Controllers.ExecController.ExecOptions execOptions, string additionalInfo = null, string appTitle = null)
        {
            SessionLog.Exception(ex);

            var id = ExceptionLogger.LogException(ex, execOptions, additionalInfo, appTitle);

            var ret = new ApiResponse();

            ret.Message = $"Error ref: {id}";
            ret.Type = ApiResponseType.Exception;
            ret.Data = new System.Dynamic.ExpandoObject();
            ((dynamic)ret.Data).Ref = id;

            return ret;
        }
        public static ApiResponse Exception(Exception ex, string additionalInfo = null, string appTitle = null)
        {
            SessionLog.Exception(ex);

            var id = ExceptionLogger.LogException(ex, additionalInfo, appTitle);

            var ret = new ApiResponse();

            ret.Message = $"Error ref: {id}";
            ret.Type = ApiResponseType.Exception;
            ret.Data = new System.Dynamic.ExpandoObject();
            ((dynamic)ret.Data).Ref = id;

            return ret;
        }

        public static ApiResponse Payload(object data)
        {
            return new ApiResponse() { Data = data, Type = ApiResponseType.Success };
        }

        // 04/02/2016, PL: Created.
        public static implicit operator string(ApiResponse r)
        {
            return JsonConvert.SerializeObject(r);
        }
    }

    public enum ApiResponseType
    {
        Unknown = 0,
        Success = 1,
        InfoMsg = 10,
        ExclamationModal = 20,
        Error = 30,
        Exception = 40
    }
}