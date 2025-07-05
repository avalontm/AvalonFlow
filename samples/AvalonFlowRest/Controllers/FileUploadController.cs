using AvalonFlow;
using AvalonFlow.Rest;
using System.Text.Json;

namespace AvalonFlowRest.Controllers
{
    [AvalonController("api/[controller]")]
    public class FileUploadController : AvalonControllerBase
    {
        [HttpPost("upload")]
        public async Task<ActionResult> UploadFile([FromFile] IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("No file uploaded or empty file");

                // Debug info
                var logData = new
                {
                    file.FileName,
                    file.ContentType,
                    file.Length,
                    file.Name
                };

                AvalonFlowInstance.Log($"File received: {JsonSerializer.Serialize(logData)}");

                return Ok(new
                {
                    Status = "File received",
                    file.FileName,
                    Size = file.Length
                });
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Upload error: {ex}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("upload2")]
        public async Task<ActionResult> UploadFileWithForm(
            [FromFile] IFormFile file,
            [FromForm] string description,
            [FromForm] int categoryId)
        {
            try
            {
                // Validar archivo
                if (file == null || file.Length == 0)
                    return BadRequest("No file uploaded or empty file");

                // Validar campos del formulario
                if (string.IsNullOrWhiteSpace(description))
                    return BadRequest("Description is required");

                if (categoryId <= 0)
                    return BadRequest("Invalid category ID");

                // Debug info
                var logData = new
                {
                    FileName = file.FileName,
                    FileSize = file.Length,
                    Description = description,
                    CategoryId = categoryId,
                    ReceivedAt = DateTime.UtcNow
                };

                AvalonFlowInstance.Log($"File with form data received: {JsonSerializer.Serialize(logData)}");

                // Aquí iría la lógica para guardar el archivo y los datos
                // Por ejemplo:
                // await SaveFileWithMetadata(file, description, categoryId);

                return Ok(new
                {
                    Status = "File and form data received successfully",
                    FileName = file.FileName,
                    FileSize = file.Length,
                    Description = description,
                    CategoryId = categoryId
                });
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Upload error: {ex}");
                return StatusCode(500, new
                {
                    Error = "Internal server error",
                    Details = ex.Message
                });
            }
        }
    }
}
