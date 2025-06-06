using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AvalonFlow.Websocket
{
    public class AvalonSocketWebClient
    {
        public ClientWebSocket? webSocket { get; private set; }
        public string? UserId { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
        public bool IsConnected => webSocket != null && webSocket.State == WebSocketState.Open;

        public AvalonSocketWebClient(ClientWebSocket? webSocket = null, string userId = "")
        {
            this.webSocket = webSocket;
            UserId = userId;
        }

        public void SetWebSocket(ClientWebSocket webSocket)
        {
            this.webSocket = webSocket;
        }

        public void SetUserId(string userId)
        {
            UserId = userId;
        }

        public async Task SendMessageAsync(string action, object data)
        {
            if (!IsAuthenticated)
                throw new InvalidOperationException("WebSocket is not connected.");

            var message = new
            {
                action,
                data
            };
            var jsonMessage = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(jsonMessage);
            var segment = new ArraySegment<byte>(buffer);
            await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
