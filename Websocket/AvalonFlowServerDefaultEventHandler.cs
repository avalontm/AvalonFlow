using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AvalonFlow.Websocket
{
    public class AvalonFlowServerDefaultEventHandler : IAvalonFlowServerSocket
    {
        public async Task OnConnectedAsync(AvalonWebSocket client)
        {
            if (client.IsConnected)
            {
                var welcomeMessage = new
                {
                    action = "Welcome",
                    data = $"Welcome to AvalonFlow WebSocket server!"
                };

                var json = JsonSerializer.Serialize(welcomeMessage);
                var bytes = Encoding.UTF8.GetBytes(json);

                await client.webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                Console.WriteLine($"Client connected");
            }
        }


        public Task OnDisconnectedAsync(AvalonWebSocket client)
        {
            Console.WriteLine("Client disconnected event received.");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            Console.WriteLine($"Error event received: {ex.Message}");
            return Task.CompletedTask;
        }

        public Task OnMessageReceivedAsync(JsonElement message)
        {
            Console.WriteLine($"Message received: {message}");
            return Task.CompletedTask;
        }

        public Task OnReconnectingAsync(AvalonWebSocket client)
        {
            Console.WriteLine("Reconnecting event received.");
            return Task.CompletedTask;
        }

        public async Task<bool> AuthenticateAsync(AvalonWebSocket client, string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(AvalonFlowInstance.JwtSecretKey);

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                }, out SecurityToken validatedToken);

                client.SetUserId(principal?.FindFirst("userId")?.Value ?? "");

                return client.IsAuthenticated;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token inválido: {ex.Message}");
                return false;
            }
        }

        [AvalonFlow("chatMessage")]
        public async void HandleChatMessage(AvalonWebSocketServer socket, AvalonWebSocket client, JsonElement data)
        {
            if (data.TryGetProperty("message", out var messageElement))
            {
                string message = messageElement.GetString() ?? "";

                await socket.BroadcastAsync("chatMessage", new { from = client.UserId, message = message });
            }
            else
            {
                Console.WriteLine("El mensaje recibido no tiene la propiedad 'message'.");
            }
        }
    }
}
