using System.Text;

namespace AvalonFlow.Rest
{
    public class ContentResult : ActionResult
    {
        public string Content { get; }
        public string ContentType { get; }
        public Encoding ContentEncoding { get; }

        public ContentResult(string content, string contentType, Encoding contentEncoding) : base(200, null)
        {
            Content = content;
            ContentType = contentType;
            ContentEncoding = contentEncoding;
        }
    }
}