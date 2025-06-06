using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AvalonFlow.Websocket
{
    public interface IAvalonFlowClientSocket
    {/// <summary>
     /// Invoked when a new connection is established.
     /// </summary>
        Task OnConnectedAsync();

        /// <summary>
        /// Invoked when a disconnection occurs.
        /// </summary>
        Task OnDisconnectedAsync();

        /// <summary>
        /// Invoked when the client reconnects.
        /// </summary>
        Task OnReconnectingAsync();

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
