using System.Net;
using System.Security.Claims;

namespace AvalonFlow.Rest
{
    public class AvalonHttpContext
    {
        public HttpListenerContext Raw { get; }
        public ClaimsPrincipal? User { get; set; } // Nuevo

        public AvalonHttpContext(HttpListenerContext context)
        {
            Raw = context;
        }

        public EndPoint? RemoteEndPoint => Raw.Request.RemoteEndPoint;
        public HttpListenerRequest Request => Raw.Request;
        public HttpListenerResponse Response => Raw.Response;
    }

}
