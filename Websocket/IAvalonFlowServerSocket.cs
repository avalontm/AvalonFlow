using System.Net.WebSockets;
using System.Text.Json;

namespace AvalonFlow.Websocket
{
    public interface IAvalonFlowServerSocket
    {
        /// <summary>
        /// Authenticates the client using a token.
        /// </summary>
        /// <param name="token">The token to authenticate.</param>
        /// <returns>True if authentication is successful.</returns>
        Task<bool> AuthenticateAsync(AvalonSocketWebServer client, string token);

        /// <summary>
        /// Invoked when a new connection is established.
        /// </summary>
        Task OnConnectedAsync(AvalonSocketWebServer client);

        /// <summary>
        /// Invoked when a disconnection occurs.
        /// </summary>
        Task OnDisconnectedAsync(AvalonSocketWebServer client);

        /// <summary>
        /// Invoked when the client reconnects.
        /// </summary>
        Task OnReconnectingAsync(AvalonSocketWebServer client);

        /// <summary>
        /// Invoked when a message is received.
        /// </summary>
        Task OnMessageReceivedAsync(JsonElement message);

        /// <summary>
        /// Invoked when an error occurs during communication.
        /// </summary>
        Task OnErrorAsync(Exception ex);
    }
}
