using System;
using Microsoft.AspNetCore.Http;

namespace jsdal_server_core
{

    public class jsDALServerVariables
    {
        private static readonly string PREFIX_MARKER = "$jsDAL$";

        public static string parse(HttpRequest request, string val)
        {
            if (val == null) return val;
            if (!val.ToLower().StartsWith(jsDALServerVariables.PREFIX_MARKER.ToLower())) return val;

            // remove the prefix
            val = val.Substring(jsDALServerVariables.PREFIX_MARKER.Length + 1);

            if (val.Equals("RemoteClient.IP"))
            {
                return request.HttpContext?.Connection?.RemoteIpAddress?.ToString();
            }
            if (val == "DBNull")
            {
                return null;
            }
            else
            {
                throw new Exception($"The server variable name \"{val}\" does not exist");
            }


        }



    }

}