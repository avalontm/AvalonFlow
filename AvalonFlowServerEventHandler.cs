using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AvalonFlow
{
    public class AvalonFlowServerEventHandler : IAvalonFlowSocket
    {
        public string? UserId { get; private set; }
        private WebSocket? _currentSocket;

        public bool IsAuthenticated { private set; get; }

        public void SetWebSocket(WebSocket webSocket)
        {
            _currentSocket = webSocket;
        }

        public async Task OnConnectedAsync()
        {
            if (_currentSocket != null && _currentSocket.State == WebSocketState.Open)
            {
                var welcomeMessage = new
                {
                    action = "Welcome",
                    data = IsAuthenticated
                        ? $"Welcome to AvalonFlow WebSocket server! {UserId}."
                        : "Welcome to AvalonFlow WebSocket server! Please authenticate."
                };

                var json = JsonSerializer.Serialize(welcomeMessage);
                var bytes = Encoding.UTF8.GetBytes(json);

                await _currentSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }


        public Task OnDisconnectedAsync()
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

        public Task OnReconnectingAsync()
        {
            Console.WriteLine("Reconnecting event received.");
            return Task.CompletedTask;
        }

        public async Task<bool> AuthenticateAsync(string token)
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

                UserId = principal?.FindFirst("userId")?.Value ?? "";
                IsAuthenticated = !string.IsNullOrEmpty(UserId);

                return IsAuthenticated;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token inválido: {ex.Message}");
                return false;
            }
        }

    }
}
