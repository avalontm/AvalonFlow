using System.Net.WebSockets;
using System.Text.Json;

namespace AvalonFlow.Websocket
{
    public class AvalonFlowClientDefaultEventHandler : IAvalonFlowClientSocket
    {

        public Task OnConnectedAsync(ClientWebSocket client)
        {
            Console.WriteLine("Client connected to server.");
            return Task.CompletedTask;
        }

        public Task OnDisconnectedAsync(ClientWebSocket client)
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

        public Task OnReconnectingAsync(ClientWebSocket client)
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

        public Task<bool> AuthenticateAsync(AvalonWebSocket client, string token)
        {
            return Task.FromResult(false);
        }

        [AvalonFlow("chatMessage")]
        public void ReceiveChatMessage(JsonElement data)
        {
            // El mensaje y el remitente suelen estar en data["message"] y data["from"]
            string from = "";
            string message = "";

            if (data.TryGetProperty("from", out var fromProp))
                from = fromProp.GetString() ?? "";

            if (data.TryGetProperty("message", out var messageProp))
                message = messageProp.GetString() ?? "";

            Console.WriteLine($"[Chat][{from}]: {message}");
        }
    }
}
