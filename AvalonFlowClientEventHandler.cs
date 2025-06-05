using System.Net.WebSockets;
using System.Text.Json;

namespace AvalonFlow
{
    public class AvalonFlowClientEventHandler : IAvalonFlowSocket
    {
        private WebSocket? _webSocket;

        public bool IsAuthenticated { private set; get; }

        public void SetWebSocket(WebSocket webSocket)
        {
            _webSocket = webSocket;
        }

        public Task OnConnectedAsync()
        {
            Console.WriteLine("Client connected to server.");
            return Task.CompletedTask;
        }

        public Task OnDisconnectedAsync()
        {
            Console.WriteLine("Client disconnected from server.");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            Console.WriteLine($"Client error: {ex.Message}");
            return Task.CompletedTask;
        }

        public Task OnMessageReceivedAsync(JsonElement message)
        {
            Console.WriteLine($"Message received on client: {message}");
            return Task.CompletedTask;
        }

        public Task OnReconnectingAsync()
        {
            Console.WriteLine("Client reconnecting...");
            return Task.CompletedTask;
        }

        [AvalonFlow("CreateTicket")]
        public void CreateTicket(JsonElement json)
        {
            Console.WriteLine($"CreateTicket received: {json}");
        }

        [AvalonFlow("Welcome")]
        public void WelcomeMessage(JsonElement data)
        {
            var welcomeText = data.GetString();
            Console.WriteLine($"Welcome message from server: {welcomeText}");
        }

        public Task<bool> AuthenticateAsync(string token)
        {
            return Task.FromResult(false);
        }
    }
}
