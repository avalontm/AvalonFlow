using System.Net.WebSockets;

namespace AvalonFlow.Websocket
{
    public class SocketWebServer
    {
        public WebSocket? webSocket { get; private set; }
        public string? SocketId => webSocket?.GetHashCode().ToString();
        public string? UserId { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
        public bool IsConnected => webSocket != null && webSocket.State == WebSocketState.Open;

        public SocketWebServer(WebSocket? webSocket = null, string userId = "")
        {
            this.webSocket = webSocket;
            UserId = userId;
        }

        public void SetWebSocket(WebSocket webSocket)
        {
            this.webSocket = webSocket;
        }

        public void SetUserId(string userId)
        {
            UserId = userId;
        }
    }
}
