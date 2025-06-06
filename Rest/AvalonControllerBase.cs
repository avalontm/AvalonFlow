namespace AvalonFlow.Rest
{
    public abstract class AvalonControllerBase : IAvalonController
    {
        public AvalonHttpContext HttpContext { get; internal set; } = null!;

        protected ActionResult Ok(object? value = null) => new(200, value);
        protected ActionResult BadRequest(string message) => new(400, new { error = message });
        protected ActionResult NotFound(string message = "Not Found") => new(404, new { error = message });
        protected ActionResult InternalServerError(string message = "Internal Server Error") => new(500, new { error = message });
        protected ActionResult Unauthorized(string message = "Unauthorized") => new(401, new { error = message });

    }

}
