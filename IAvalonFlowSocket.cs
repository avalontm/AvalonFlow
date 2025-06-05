using System.Net.WebSockets;
using System.Text.Json;

namespace AvalonFlow
{
    public interface IAvalonFlowSocket
    {
        /// <summary>
        /// Indicates whether the client is authenticated.
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Sets the WebSocket instance.
        /// </summary>
        /// <param name="webSocket">The WebSocket connection.</param>
        void SetWebSocket(WebSocket webSocket);

        /// <summary>
        /// Invoked when the socket successfully connects.
        /// </summary>
        Task OnConnectedAsync();

        /// <summary>
        /// Invoked when the socket disconnects.
        /// </summary>
        Task OnDisconnectedAsync();

        /// <summary>
        /// Invoked when a reconnection attempt is made.
        /// </summary>
        Task OnReconnectingAsync();

        /// <summary>
        /// Invoked when a connection error occurs.
        /// </summary>
        Task OnErrorAsync(Exception ex);

        /// <summary>
        /// Receives incoming messages.
        /// </summary>
        Task OnMessageReceivedAsync(JsonElement message);

        /// <summary>
        /// Authenticates the client using the provided token.
        /// </summary>
        /// <param name="token">The authentication token.</param>
        /// <returns>A task that represents the asynchronous authentication operation.</returns>
        Task<bool> AuthenticateAsync(string token);
    }
}