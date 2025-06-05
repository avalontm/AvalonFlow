using System;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AvalonFlow
{
    public class AvalonWebSocketServer
    {
        private readonly HttpListener _listener;
        private readonly Dictionary<string, MethodInfo> _handlers = new();
        private readonly IAvalonFlowSocket _handlerInstance;

        public AvalonWebSocketServer(string prefix, IAvalonFlowSocket handlerInstance = null)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _handlerInstance = handlerInstance;

            if(_handlerInstance == null)
            {
                _handlerInstance = new AvalonFlowServerEventHandler();
            }

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
            var wsContext = await context.AcceptWebSocketAsync(null);
            var webSocket = wsContext.WebSocket;

            if (_handlerInstance is IAvalonFlowSocket eventHandler)
            {
                eventHandler.SetWebSocket(webSocket);
            }

            var buffer = new byte[4096];
            try
            {
                while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (_handlerInstance is IAvalonFlowSocket eventHandlerClose)
                        {
                            await eventHandlerClose.OnDisconnectedAsync();
                            // Opcional: enviar mensaje de despedida antes de cerrar
                        }

                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
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

                            if (_handlerInstance is IAvalonFlowSocket _eventHandler)
                            {
                                if (action == "authenticate")
                                {
                                    var token = dataElement.GetString() ?? "";
                                    bool isAuth = await _eventHandler.AuthenticateAsync(token);

                                    if (!isAuth)
                                    {
                                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid token", CancellationToken.None);
                                        return;
                                    }

                                    continue;
                                }

                                // Permite manejar mensajes solo si está autenticado
                                if (_eventHandler.IsAuthenticated)
                                {
                                    await _eventHandler.OnMessageReceivedAsync(doc.RootElement);

                                    if (action != null && _handlers.TryGetValue(action, out var method))
                                    {
                                        method.Invoke(_handlerInstance, new object[] { dataElement });
                                    }
                                    else
                                    {
                                        Console.WriteLine($"No handler found for action '{action}'");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Client tried to send message before authentication");
                                    // Aquí puedes cerrar la conexión o simplemente ignorar el mensaje
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_handlerInstance is IAvalonFlowSocket eventHandlerErr)
                        {
                            await eventHandlerErr.OnErrorAsync(ex);
                            // Opcional: enviar mensaje de error al cliente
                        }
                    }
                    finally
                    {
                        if (_handlerInstance is IAvalonFlowSocket finalHandler)
                        {
                            // Llama al método OnConnectedAsync después de procesar el mensaje
                            await finalHandler.OnConnectedAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_handlerInstance is IAvalonFlowSocket eventHandlerErr)
                {
                    await eventHandlerErr.OnErrorAsync(ex);
                }
            }
            finally
            {
                if (webSocket.State == WebSocketState.Open)
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
            }
        }

    }
}
