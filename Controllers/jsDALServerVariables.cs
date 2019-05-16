using System;
using Microsoft.AspNetCore.Http;

namespace jsdal_server_core
{

    public class jsDALServerVariables
    {
        private static readonly string PREFIX_MARKER = "$jsDAL$";

        public static object Parse(string remoteIpAddress, object val)
        {
            if (val == null) return val;
            
            var str = val.ToString();

            if (!str.ToLower().StartsWith(jsDALServerVariables.PREFIX_MARKER.ToLower())) return val;

            // remove the prefix
            str = str.Substring(jsDALServerVariables.PREFIX_MARKER.Length + 1);

            if (str.Equals("RemoteClient.IP"))
            {
                return remoteIpAddress;
            }
            if (str.Equals("DBNull",StringComparison.OrdinalIgnoreCase))
            {
                return DBNull.Value;
            }
            else
            {
                throw new Exception($"The server variable name \"{str}\" does not exist");
            }


        }



    }

}