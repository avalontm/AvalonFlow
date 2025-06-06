using AvalonFlow;
using AvalonFlow.Rest;
using AvalonFlowRest.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlowRest.Controllers
{
    [Authorize(Roles = "Admin")]
    [AvalonController("api/[controller]")]
    public class AdminController : AvalonControllerBase
    {
        [HttpGet]
        public ActionResult Get()
        {
            return Ok(new { message = "Welcome to the AdminController!" });
        }

        [HttpGet("info")]
        public ActionResult Info()
        {
            var ip = HttpContext.Request.RemoteEndPoint?.ToString();
            return Ok(new { message = "Hola desde el servidor", clientIp = ip });
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public ActionResult Login([FromBody] LoginRequest request)
        {
            if (request.Username != "admin" || request.Password != "1234")
            {
                return Unauthorized("Credenciales inválidas");
            }
            var token = AvalonFlowInstance.GenerateJwtToken(request.Username, "Admin");
            return Ok(new
            {
                token,
                user = new { name = request.Username }
            });
        }
    }
}
