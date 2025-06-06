using System;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace AvalonFlow.Websocket
{
    public class AvalonWebSocketServer
    {
        private readonly HttpListener _listener;
        private readonly Dictionary<string, MethodInfo> _handlers = new();
        private readonly IAvalonFlowServerSocket _handlerInstance;

        // Almacena conexiones activas: userId -> WebSocket
        private readonly ConcurrentDictionary<string, AvalonWebSocket> _clients = new();

        // Opcional: grupos -> lista de userIds
        private readonly ConcurrentDictionary<string, List<AvalonWebSocket>> _groups = new();

        public AvalonWebSocketServer(string prefix, IAvalonFlowServerSocket handlerInstance = null)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _handlerInstance = handlerInstance ?? new AvalonFlowServerDefaultEventHandler();

            var methods = _handlerInstance.GetType()
                .GetMethods()
                .Where(m => m.GetCustomAttribute<AvalonFlowAttribute>() != null);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<AvalonFlowAttribute>();
                if (attr != null)
                    _handlers[attr.EventName] = method;
            }
        }

        public async Task StartAsync(CancellationToken ct)
        {
            _listener.Start();
            Console.WriteLine("WebSocket server started. Listening for connections...");

            while (!ct.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    _ = HandleConnectionAsync(context, ct);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }

            _listener.Stop();
        }

        private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken ct)
        {
            WebSocket webSocket = null;
            string socketId = null;
            bool authenticated = false;

            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                webSocket = wsContext.WebSocket;

                var buffer = new byte[4096];

                while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    try
                    {
                        var doc = JsonDocument.Parse(message);

                        if (doc.RootElement.TryGetProperty("action", out var actionElement) &&
                            doc.RootElement.TryGetProperty("data", out var dataElement))
                        {
                            var action = actionElement.GetString();

                            if (_handlerInstance is IAvalonFlowServerSocket _eventHandler)
                            {
                                if (action == "authenticate" && !authenticated)
                                {
                                    // Delay para esperar el token del cliente
                                    await Task.Delay(100);
                                    AvalonWebSocket client = new AvalonWebSocket(webSocket);
                                    string token = dataElement.GetString() ?? "";
                                    bool isAuth = await _eventHandler.AuthenticateAsync(client, token);

                                    if (!isAuth)
                                    {
                                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid token", CancellationToken.None);
                                        return;
                                    }

                                    socketId = client?.SocketId;
                                    // Registrar el cliente
                                    authenticated = client?.IsAuthenticated ?? false;
                                    _clients[socketId] = client;

                                    // Llamar evento
                                    await _handlerInstance.OnConnectedAsync(client);

                                    var authSuccessMsg = JsonSerializer.Serialize(new { action = "authenticated", data = "success" });
                                    var authBuffer = Encoding.UTF8.GetBytes(authSuccessMsg);
                                    await webSocket.SendAsync(authBuffer, WebSocketMessageType.Text, true, CancellationToken.None);

                                    continue;
                                }

                                if (!authenticated)
                                {
                                    Console.WriteLine("Client sent message before authentication.");
                                    await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Not authenticated", CancellationToken.None);
                                    return;
                                }

                                await _eventHandler.OnMessageReceivedAsync(doc.RootElement);

                                if (action != null && _handlers.TryGetValue(action, out var method))
                                {
                                    if (!string.IsNullOrEmpty(socketId) && _clients.TryGetValue(socketId, out var client))
                                    {
                                        var invokeResult = method.Invoke(_handlerInstance, new object[] { this, client, dataElement });

                                        if (invokeResult is Task task)
                                        {
                                            await task;
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("Client not found or not authenticated.");
                                    }
                                }

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_handlerInstance is IAvalonFlowServerSocket eventHandlerErr)
                        {
                            await eventHandlerErr.OnErrorAsync(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_handlerInstance is IAvalonFlowServerSocket eventHandlerErr)
                {
                    await eventHandlerErr.OnErrorAsync(ex);
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(socketId))
                {
                    _clients.TryRemove(socketId, out _);
                    Console.WriteLine($"Disconnected: {socketId}");
                }

                if (webSocket != null && webSocket.State == WebSocketState.Open)
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing connection", CancellationToken.None);
            }
        }


        // Enviar a un cliente específico
        public async Task SendToClientAsync(string socketId, string action, object data)
        {
            if (_clients.TryGetValue(socketId, out var client) && client.IsConnected)
            {
                var msg = JsonSerializer.Serialize(new { action, data });
                var buffer = Encoding.UTF8.GetBytes(msg);
                await client.webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        // Broadcast a todos los clientes
        public async Task BroadcastAsync(string action, object data)
        {
            var msg = JsonSerializer.Serialize(new { action, data });
            var buffer = Encoding.UTF8.GetBytes(msg);

            foreach (var client in _clients.Values)
            {
                if (client.IsConnected)
                {
                    await client.webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }

        // Enviar a un grupo de usuarios
        public async Task SendToGroupAsync(string groupId, string action, object data)
        {
            if (_groups.TryGetValue(groupId, out var users))
            {
                foreach (AvalonWebSocket socket in users)
                {
                    await SendToClientAsync(socket.UserId, action, data);
                }
            }
        }

        // Agregar usuario a grupo
        public void AddToGroup(string groupId, AvalonWebSocket socket)
        {
            _groups.AddOrUpdate(groupId,
                _ => new List<AvalonWebSocket> { socket },
                (_, list) =>
                {
                    if (!list.Contains(socket))
                        list.Add(socket);
                    return list;
                });
        }

        // Remover usuario de grupo
        public void RemoveFromGroup(string groupId, AvalonWebSocket socket)
        {
            if (_groups.TryGetValue(groupId, out var list))
            {
                list.Remove(socket);
                if (list.Count == 0)
                {
                    _groups.TryRemove(groupId, out _);
                }
            }
        }
    }
}
