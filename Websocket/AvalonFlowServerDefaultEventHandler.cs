using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AvalonFlow.Websocket
{
    public class AvalonFlowServerDefaultEventHandler : IAvalonFlowServerSocket
    {
        public async Task OnConnectedAsync(SocketWebServer client)
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
                AvalonFlowInstance.Log($"Client connected: {client.UserId}");
            }
        }


        public Task OnDisconnectedAsync(SocketWebServer client)
        {
            AvalonFlowInstance.Log($"Client disconnected event received.");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            AvalonFlowInstance.Log($"Error event received: {ex.Message}");
            return Task.CompletedTask;
        }

        public Task OnMessageReceivedAsync(JsonElement message)
        {
            AvalonFlowInstance.Log($"Message received: {message}");
            return Task.CompletedTask;
        }

        public Task OnReconnectingAsync(SocketWebServer client)
        {
            AvalonFlowInstance.Log($"Reconnecting event received.");
            return Task.CompletedTask;
        }

        public async Task<bool> AuthenticateAsync(SocketWebServer client, string token, string parameters = "")
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
                AvalonFlowInstance.Log($"Invalid Token: {ex.Message}");
                return false;
            }
        }

        [AvalonFlow("chatMessage")]
        public async void HandleChatMessage(AvalonWebSocketServer socket, SocketWebServer client, JsonElement data)
        {
            if (data.TryGetProperty("message", out var messageElement))
            {
                string message = messageElement.GetString() ?? "";

                await socket.BroadcastAsync("chatMessage", new { time = DateTime.Now.ToString("hh:mm:ss"), from = client.UserId, message = message });
            }
            else
            {
                AvalonFlowInstance.Log($"El mensaje recibido no tiene la propiedad 'message'.");
            }
        }
    }
}
