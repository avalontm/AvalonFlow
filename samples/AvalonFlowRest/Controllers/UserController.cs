using AvalonFlow;
using AvalonFlow.Rest;
using AvalonFlowRest.Models;
using System.Text.Json;

namespace AvalonFlowRest.Controllers
{
    [AllowAnonymous]
    [AvalonController("api/[controller]")]
    public class UserController : AvalonControllerBase
    {
        [HttpGet]
        public ActionResult Get()
        {
            return Ok(new { message = "Welcome to the UserController!" });
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("security")]
        public ActionResult Security()
        {
            var username = this.HttpContext.User?.Identity?.Name;

            return Ok(new { username = username, message = "This is a secure endpoint." });
        }

        [HttpGet("info")]
        public ActionResult Hello()
        {
            var ip = HttpContext.Request.RemoteEndPoint?.ToString();
            return Ok(new { message = "Hola desde el servidor", clientIp = ip });
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public ActionResult Login([FromBody] LoginRequest request)
        {
            if (request.Username != "admin" || request.Password != "1234")
            {
                return Unauthorized("Credenciales inválidas");
            }

            var token = AvalonFlowInstance.GenerateJwtToken(request.Username);

            return Ok(new
            {
                token,
                user = new { name = request.Username }
            });
        }

        [HttpPut("{user_id}")]
        public ActionResult Complete(string user_id, [FromBody] JsonElement json)
        {
            string? name = null;
            if (json.TryGetProperty("name", out var _name))
            {
                name = _name.GetString();
            }
            return Ok(new { message = $"User {user_id} as {name}" });
        }

        [HttpPatch("{userId}")]
        public ActionResult PatchUser(string userId, [FromBody] JsonElement data)
        {
            // lógica para modificar solo campos enviados
            return Ok();
        }

        [HttpOptions]
        public ActionResult Options()
        {
            HttpContext.Response.Headers.Add("Allow", "GET,POST,PUT,DELETE,PATCH");
            return Ok(new { message = "todo bien"});
        }

        [HttpHead("{id}")]
        public ActionResult HeadCheck(string id)
        {
            // si el recurso existe, return 200 sin body
            return Ok();
        }

    }
}
