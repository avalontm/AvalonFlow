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
                string prefix = $"{scheme}://*:{port}/{path.Trim('/')}/";

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
                AvalonFlowInstance.Log($"WebSocket stopped");
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
            SocketWebServer client = null;

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
                                // Manejo de autenticación (solo la primera vez)
                                if (action == "authenticate" && client == null)
                                {
                                    client = new SocketWebServer(webSocket);
                                    string token = dataElement.GetString() ?? "";
                                    doc.RootElement.TryGetProperty("parameters", out var dataParameters);
                                    string parameters = dataParameters.GetString() ?? "";

                                    bool isAuth = await _eventHandler.AuthenticateAsync(client, token, parameters);

                                    if (!isAuth)
                                    {
                                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid token", CancellationToken.None);
                                        return;
                                    }

                                    socketId = client.SocketId;

                                    // Registrar el cliente
                                    _clients[socketId] = client;

                                    // Llamar evento de conexión
                                    await _handlerInstance.OnConnectedAsync(client);

                                    // Enviar confirmación de autenticación
                                    await SendToClientAsync(socketId, "authenticated", new { success = true, socketId = socketId });

                                    continue;
                                }

                                // Verificar que el cliente esté autenticado
                                if (client == null || !client.IsAuthenticated)
                                {
                                    AvalonFlowInstance.Log("Client sent message before authentication.");
                                    await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Not authenticated", CancellationToken.None);
                                    return;
                                }

                                // Procesar mensaje recibido
                                await _eventHandler.OnMessageReceivedAsync(doc.RootElement);

                                // Ejecutar handler específico si existe
                                if (action != null && _handlers.TryGetValue(action, out var method))
                                {
                                    try
                                    {
                                        await ExecuteHandlerMethod(method, client, dataElement);
                                    }
                                    catch (Exception ex)
                                    {
                                        AvalonFlowInstance.Log($"Error executing handler '{action}': {ex.Message}");
                                        await _eventHandler.OnErrorAsync(ex);
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        AvalonFlowInstance.Log($"Invalid JSON received: {ex.Message}");
                        if (_handlerInstance is IAvalonFlowServerSocket eventHandlerErr)
                        {
                            await eventHandlerErr.OnErrorAsync(ex);
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
                AvalonFlowInstance.Log($"Connection error: {ex.Message}");
                if (_handlerInstance is IAvalonFlowServerSocket eventHandlerErr)
                {
                    await eventHandlerErr.OnErrorAsync(ex);
                }
            }
            finally
            {
                // Limpieza cuando se cierra la conexión
                if (!string.IsNullOrEmpty(socketId))
                {
                    _clients.TryRemove(socketId, out _);

                    // Remover de todos los grupos
                    if (client != null)
                    {
                        RemoveClientFromAllGroups(client);

                        // Llamar evento de desconexión
                        if (_handlerInstance != null)
                        {
                            try
                            {
                                await _handlerInstance.OnDisconnectedAsync(client);
                            }
                            catch (Exception ex)
                            {
                                AvalonFlowInstance.Log($"Error in OnDisconnectedAsync: {ex.Message}");
                            }
                        }
                    }
                }

                if (webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing connection", CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        AvalonFlowInstance.Log($"Error closing WebSocket: {ex.Message}");
                    }
                }
            }
        }

        private async Task ExecuteHandlerMethod(MethodInfo method, SocketWebServer client, JsonElement dataElement)
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
                throw new InvalidOperationException($"Unsupported parameter signature for handler method.");
            }

            var invokeResult = method.Invoke(_handlerInstance, args);

            if (invokeResult is Task task)
            {
                await task;
            }
        }

        private void RemoveClientFromAllGroups(SocketWebServer client)
        {
            var groupsToRemove = new List<string>();

            foreach (var group in _groups)
            {
                if (group.Value.Contains(client))
                {
                    group.Value.Remove(client);
                    if (group.Value.Count == 0)
                    {
                        groupsToRemove.Add(group.Key);
                    }
                }
            }

            foreach (var groupId in groupsToRemove)
            {
                _groups.TryRemove(groupId, out _);
            }
        }

        // Enviar a un cliente específico
        public async Task SendToClientAsync(string socketId, string action, object data)
        {
            if (_clients.TryGetValue(socketId, out var client) && client.IsConnected)
            {
                try
                {
                    var msg = JsonSerializer.Serialize(new { action, data });
                    var buffer = Encoding.UTF8.GetBytes(msg);
                    await client.webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error sending message to client {socketId}: {ex.Message}");
                    // Remover cliente si hay error de envío
                    _clients.TryRemove(socketId, out _);
                }
            }
        }

        // Broadcast a todos los clientes
        public async Task BroadcastAsync(string action, object data)
        {
            var msg = JsonSerializer.Serialize(new { action, data });
            var buffer = Encoding.UTF8.GetBytes(msg);
            var clientsToRemove = new List<string>();

            foreach (var kvp in _clients)
            {
                try
                {
                    if (kvp.Value.IsConnected)
                    {
                        await kvp.Value.webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    else
                    {
                        clientsToRemove.Add(kvp.Key);
                    }
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error broadcasting to client {kvp.Key}: {ex.Message}");
                    clientsToRemove.Add(kvp.Key);
                }
            }

            // Remover clientes desconectados
            foreach (var clientId in clientsToRemove)
            {
                _clients.TryRemove(clientId, out _);
            }
        }

        // Enviar a un grupo de usuarios
        public async Task SendToGroupAsync(string groupId, string action, object data)
        {
            if (_groups.TryGetValue(groupId, out var users))
            {
                var msg = JsonSerializer.Serialize(new { action, data });
                var buffer = Encoding.UTF8.GetBytes(msg);
                var usersToRemove = new List<SocketWebServer>();

                foreach (var socket in users.ToList()) // ToList para evitar modificación durante iteración
                {
                    try
                    {
                        if (socket.IsConnected)
                        {
                            await socket.webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        else
                        {
                            usersToRemove.Add(socket);
                        }
                    }
                    catch (Exception ex)
                    {
                        AvalonFlowInstance.Log($"Error sending to group member: {ex.Message}");
                        usersToRemove.Add(socket);
                    }
                }

                // Remover usuarios desconectados del grupo
                foreach (var user in usersToRemove)
                {
                    users.Remove(user);
                }

                // Si el grupo queda vacío, eliminarlo
                if (users.Count == 0)
                {
                    _groups.TryRemove(groupId, out _);
                }
            }
        }

        // Agregar usuario a grupo
        public bool AddToGroup(string groupId, SocketWebServer socket)
        {
            try
            {
                _groups.AddOrUpdate(groupId,
                    _ => new List<SocketWebServer> { socket },
                    (_, list) =>
                    {
                        if (!list.Contains(socket))
                            list.Add(socket);
                        return list;
                    });
                return true;
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error adding to group {groupId}: {ex.Message}");
                return false;
            }
        }

        // Remover usuario de grupo
        public bool RemoveFromGroup(string groupId, SocketWebServer socket)
        {
            try
            {
                if (_groups.TryGetValue(groupId, out var list))
                {
                    bool isRemoved = list.Remove(socket);
                    if (list.Count == 0)
                    {
                        _groups.TryRemove(groupId, out _);
                    }
                    return isRemoved;
                }
                return false;
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error removing from group {groupId}: {ex.Message}");
                return false;
            }
        }

        // Métodos de utilidad
        public int GetConnectedClientsCount() => _clients.Count;

        public IEnumerable<string> GetConnectedClientIds() => _clients.Keys.ToList();

        public bool IsClientConnected(string socketId) => _clients.ContainsKey(socketId) && _clients[socketId].IsConnected;
    }
}