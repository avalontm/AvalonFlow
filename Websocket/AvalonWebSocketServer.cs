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
        private readonly ConcurrentDictionary<string, SocketWebServer> _clients = new();

        // Opcional: grupos -> lista de userIds
        private readonly ConcurrentDictionary<string, List<SocketWebServer>> _groups = new();

        private int _port;
        private bool _useHttps;

        /// <summary>
        /// 
        /// </summary>
        public AvalonWebSocketServer(int port, string path, bool useHttps = false, IAvalonFlowServerSocket handlerInstance = null)
        {
            try
            {
                _listener = new HttpListener();
                _port = port;
                _useHttps = useHttps;

                if (port < 1 || port > 65535)
                    throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

                string scheme = useHttps ? "https" : "http";
                string prefix = $"{scheme}://+:{port}/{path.Trim('/')}/";

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
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error initializing WebSocket server: {ex.Message}");
            }
        }

        public async Task StartAsync(CancellationToken ct)
        {
            try
            {
                _listener.Start();
                AvalonFlowInstance.Log($"WebSocket server started at " + _listener.Prefixes.FirstOrDefault());

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
            catch (HttpListenerException ex)
            {
                AvalonFlowInstance.Log($"HTTP Listener error: {ex.Message}");
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error in WebSocket server: {ex.Message}");
            }
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
                                    SocketWebServer client = new SocketWebServer(webSocket);
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
                                    AvalonFlowInstance.Log("Client sent message before authentication.");
                                    await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Not authenticated", CancellationToken.None);
                                    return;
                                }

                                await _eventHandler.OnMessageReceivedAsync(doc.RootElement);

                                if (action != null && _handlers.TryGetValue(action, out var method))
                                {
                                    if (!string.IsNullOrEmpty(socketId) && _clients.TryGetValue(socketId, out var client))
                                    {
                                        try
                                        {
                                            var parameters = method.GetParameters();
                                            object?[] args;

                                            if (parameters.Length == 3 &&
                                                parameters[0].ParameterType == typeof(AvalonWebSocketServer) &&
                                                parameters[1].ParameterType == typeof(SocketWebServer) &&
                                                parameters[2].ParameterType == typeof(JsonElement))
                                            {
                                                args = new object[] { this, client, dataElement };
                                            }
                                            else if (parameters.Length == 2 &&
                                                     parameters[0].ParameterType == typeof(SocketWebServer) &&
                                                     parameters[1].ParameterType == typeof(JsonElement))
                                            {
                                                args = new object[] { client, dataElement };
                                            }
                                            else if (parameters.Length == 1 &&
                                                     parameters[0].ParameterType == typeof(JsonElement))
                                            {
                                                args = new object[] { dataElement };
                                            }
                                            else
                                            {
                                                AvalonFlowInstance.Log($"Unsupported parameter signature for handler '{action}'.");
                                                return;
                                            }

                                            var invokeResult = method.Invoke(_handlerInstance, args);

                                            if (invokeResult is Task task)
                                            {
                                                await task;
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
                                    else
                                    {
                                        AvalonFlowInstance.Log("Client not found or not authenticated.");
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
                foreach (SocketWebServer socket in users)
                {
                    await SendToClientAsync(socket.UserId, action, data);
                }
            }
        }

        // Agregar usuario a grupo
        public void AddToGroup(string groupId, SocketWebServer socket)
        {
            _groups.AddOrUpdate(groupId,
                _ => new List<SocketWebServer> { socket },
                (_, list) =>
                {
                    if (!list.Contains(socket))
                        list.Add(socket);
                    return list;
                });
        }

        // Remover usuario de grupo
        public void RemoveFromGroup(string groupId, SocketWebServer socket)
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
