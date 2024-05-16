using System;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace jsdal_server_core
{

    public class jsDALServerVariables
    {
        private static readonly string PREFIX_MARKER = "$jsDAL$";

        public static object Parse(string remoteIpAddress, object val, Microsoft.AspNetCore.Http.HttpContext httpContext)
        {
            if (val == null) return val;

            var str = val.ToString()!;

            if (!str.ToLower().StartsWith(jsDALServerVariables.PREFIX_MARKER, StringComparison.OrdinalIgnoreCase)) return val;

            // remove the prefix
            str = str.Substring(jsDALServerVariables.PREFIX_MARKER.Length + 1);

            if (str.Equals("RemoteClient.IP", StringComparison.OrdinalIgnoreCase))
            {
                return remoteIpAddress;
            }
            else if (str.Equals("DBNull", StringComparison.OrdinalIgnoreCase))
            {
                return DBNull.Value;
            }
            else if (str.Equals("Identity", StringComparison.OrdinalIgnoreCase))
            {
                return JsonSerializer.Serialize(new
                {
                    Name = httpContext?.User?.Identity?.Name,
                    IsAuthenticated = httpContext?.User?.Identity?.IsAuthenticated ?? false,
                    AuthenticationType = httpContext?.User?.Identity?.AuthenticationType
                });
            }
            else
            {
                throw new Exception($"The server variable name \"{str}\" does not exist");
            }


        }



    }

}