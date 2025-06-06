using AvalonFlow.Rest;

namespace AvalonFlowRest.Controllers
{
    [AvalonController("api/[controller]")]
    public class FileController : AvalonControllerBase
    {
        [HttpGet("download")]
        public ActionResult DownloadFile()
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "files", "ejemplo.txt");

            if (!System.IO.File.Exists(filePath))
                return NotFound("Archivo no encontrado.");

            var stream = System.IO.File.OpenRead(filePath);
            return File(stream, "application/octet-stream", "archivo.txt");
        }

        [HttpGet("video")]
        public ActionResult GetVideo()
        {
            var filePath =  Path.Combine(Directory.GetCurrentDirectory(), "files", "video.mp4");

            if (!System.IO.File.Exists(filePath))
                return NotFound("Archivo no encontrado.");

            var stream = System.IO.File.OpenRead(filePath);
            return new StreamFileActionResult(stream, "video/mp4", "video.mp4", isAttachment: false);
        }

        [HttpGet("image")]
        public ActionResult GetImage()
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "files", "imagen.jpg");

            if (!System.IO.File.Exists(filePath))
                return NotFound("Imagen no encontrada.");

            var stream = System.IO.File.OpenRead(filePath);
            return new StreamFileActionResult(stream, "image/jpeg", "imagen.jpg", isAttachment: false);
        }


    }
}
