using AvalonFlow;
using AvalonFlow.Rest;
using AvalonFlowRest.Services;
using System.Text.Json;

namespace AvalonFlowRest.Controllers
{
    [AllowAnonymous]
    [AvalonController("api/[controller]")]
    public class EmailController : AvalonControllerBase
    {
        [HttpPost("sendmail")]
        public ActionResult EnviarCorreo([FromBody] JsonElement json)
        {
            var emailService = AvalonServiceRegistry.Resolve<EmailService>();
            emailService.Send("cliente@email.com", "Bienvenido", "Gracias por registrarte.");
            return Ok("Correo enviado.");
        }
    }
}
