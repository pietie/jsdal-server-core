using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace jsdal_server_core.Controllers
{
    public class AuthController : Controller
    {
        private readonly IConfiguration _config;
        //        private readonly IUserManager _userManager;

        public AuthController(IConfiguration configuration/*, IUserManager userManager*/)
        {
            _config = configuration;
            //_userManager = userManager;
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("api/authenticate")]
        public IActionResult AuthLogin(string username, string password)
        {
            var res = HttpContext.Response;
            var isValid = UserManagement.Validate(username, password);

            if (!isValid)
            {
                return BadRequest("Authentication failed");
            }
            else
            {
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                var expires = DateTime.Now.AddHours(24);
                var expiresEpoch = Convert.ToInt64((expires - epoch).TotalSeconds) * 1000; // ecpoch in milliseconds

                var claims = new[] {
                        new Claim(JwtRegisteredClaimNames.Sub, username),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                        new Claim("role", "admin")

                    };

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Tokens:Key"]));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(_config["Tokens:Issuer"], _config["Tokens:Issuer"],
                                    claims, expires: expires, signingCredentials: creds);

                return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), expiresEpoch = expiresEpoch });
            }
        }

        [AllowAnonymous]
        [Route("token/validate")]
        [HttpGet]
        public IActionResult ValidateToken([FromQuery] string token)
        {
            try
            {
                //var tokenString = HttpContext.Request.Headers["Authorization"].FirstOrDefault();

                // if (tokenString == null) return Json(new { valid = false, message = "Invalid token" });

                // tokenString = tokenString.Substring("Bearer ".Length);

                var jwtToken = new JwtSecurityToken(token);

                var utcNow = DateTime.UtcNow;
                if (jwtToken.ValidFrom <=utcNow && utcNow < jwtToken.ValidTo)
                {
                    return Json(new { valid = true });
                }
                else
                {
                    return Json(new { valid = false, message = "Expired" });
                }
            }
            catch (Exception)
            {
                return Json(new { valid = false, message = "Invalid token" });
            }
        }
    }
}