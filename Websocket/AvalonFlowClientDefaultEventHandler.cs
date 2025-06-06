using System.Text.Json;

namespace AvalonFlow.Websocket
{
    public class AvalonFlowClientDefaultEventHandler : IAvalonFlowClientSocket
    {
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

        [AvalonFlow("Welcome")]
        public void WelcomeMessage(AvalonSocketWebClient socket, JsonElement data)
        {
            var welcomeText = data.GetString();
            Console.WriteLine($"Welcome message from server: {welcomeText}");
        }

        [AvalonFlow("chatMessage")]
        public void ReceiveChatMessage(AvalonSocketWebClient socket, JsonElement data)
        {
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
