namespace AvalonFlow.Rest
{
    public class FileActionResult : ActionResult
    {
        public byte[] Content { get; }
        public string ContentType { get; }
        public string FileName { get; }

        public FileActionResult(byte[] content, string contentType, string fileName) : base(200, null)
        {
            Content = content;
            ContentType = contentType;
            FileName = fileName;
        }
    }

}
