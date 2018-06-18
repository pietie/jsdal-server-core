using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace jsdal_server_core.Controllers
{
    [Authorize(Roles = "admin")]
    public class UtilController : Controller
    {

        [HttpGet("/api/util/listdbs")]
        public ApiResponse ListDBs([FromQuery] string datasource, [FromQuery] string u, [FromQuery] string p, [FromQuery] int port, [FromQuery] string instanceName)
        {
            try
            {
                string connStr;
                string user = u;
                string pass = p;

                if (!string.IsNullOrWhiteSpace(user))
                {
                    connStr = string.Format("Data Source={0};Persist Security Info=False;User ID={1};Password={2};", datasource, user, pass);
                }
                else
                {
                    connStr = string.Format("Data Source={0};Persist Security Info=False;Integrated Security=True", datasource);
                }

                List<dynamic> ret = null;
                using (var con = new SqlConnection())
                {
                    con.ConnectionString = connStr;

                    con.Open();

                    var cmd = new SqlCommand("select Name from sys.databases order by 1", con);

                    using (var reader = cmd.ExecuteReader())
                    {
                        ret = reader.Cast<dynamic>().Select(s => s.GetString(0)).ToList();
                        reader.Close();
                    }

                    con.Close();
                }

                return ApiResponse.Payload(ret);

            }
            catch (SqlException se)
            {
                if (se.ErrorCode == -2146232060/*Operation Cancelled by user*/) return null; // TODO: Not sure what sort of ApiReponse to return here?
                throw;
            }


        }

        [HttpGet("/api/util/testconnection")]
        public ApiResponse TestConnection([FromQuery] string dataSource, [FromQuery] string catalog, [FromQuery] string username, [FromQuery] string password, [FromQuery] int port, [FromQuery] string instanceName)
        {
            try
            {
                string connStr = null;

                if (!string.IsNullOrWhiteSpace(username))
                {
                    connStr = string.Format("Data Source={0};Persist Security Info=False;User ID={1};Password={2}; Initial Catalog={3}", dataSource, username, password, catalog);
                }
                else
                {
                    connStr = string.Format("Data Source={0};Persist Security Info=False;Initial Catalog={1};Integrated Security=True", dataSource, catalog);
                }

                using (var con = new SqlConnection(connStr))
                {
                    con.Open();
                }

                return ApiResponse.Success();
            }
            catch (SqlException se)
            {
                if (se.Number == 18456)
                {
                    return ApiResponse.ExclamationModal("Login failed. Please check your username and password.");
                }

                return ApiResponse.Exception(se);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpPost("/api/util/test-compile")]
        public async Task<ApiResponse> TestConnection(dynamic bodyIgnored)
        {
            try
            {
                // var s = bodyIgnored.GetType();
                // var t = bodyIgnored.Type;
                
                using (var sr = new System.IO.StreamReader(this.Request.Body))
                {
                    var code  = sr.ReadToEnd();

                    try
                    {
                    var x = await CSharpScript.EvaluateAsync(code);

                    int n =0;
                    }
                    catch(CompilationErrorException ce)
                    {
                            return ApiResponse.Payload(new { Error = ce.Message });
                    }
// CSharpScript
  //                  var csharp = new CSharpLanguage();

                    
                }

               

                return ApiResponse.Success();
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

    }
}