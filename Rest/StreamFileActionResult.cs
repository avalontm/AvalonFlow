namespace AvalonFlow.Rest
{
    public class StreamFileActionResult : ActionResult
    {
        public Stream ContentStream { get; }
        public string ContentType { get; }
        public string? FileName { get; }
        public bool IsAttachment { get; }

        public StreamFileActionResult(Stream contentStream, string contentType, string? fileName = null, bool isAttachment = true)
            : base(200, null)
        {
            ContentStream = contentStream;
            ContentType = contentType;
            FileName = fileName;
            IsAttachment = isAttachment;
        }
    }
}
