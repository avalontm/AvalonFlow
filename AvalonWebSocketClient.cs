using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;

namespace AvalonFlow
{
    public class AvalonWebSocketClient
    {
        private readonly ClientWebSocket _ws = new();
        private readonly IAvalonFlowSocket _handlerInstance;
        private readonly Dictionary<string, MethodInfo> _eventHandlers = new();

        public AvalonWebSocketClient(IAvalonFlowSocket handlerInstance = null)
        {
            _handlerInstance = handlerInstance;

            if(_handlerInstance == null)
            {
                _handlerInstance = new AvalonFlowClientEventHandler();
            }

            RegisterEventHandlers();
        }

        private void RegisterEventHandlers()
        {
            var methods = _handlerInstance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<AvalonFlowAttribute>();
                if (attr != null && !string.IsNullOrEmpty(attr.EventName))
                {
                    _eventHandlers[attr.EventName] = method;
                }
            }
        }

        public async Task<bool> ConnectAsync(Uri uri, string? token = null)
        {
            try
            {
                await _ws.ConnectAsync(uri, CancellationToken.None);

                // Si envían token, envíalo como mensaje de autenticación justo después de conectar
                if (!string.IsNullOrEmpty(token))
                {
                    var authMessage = JsonSerializer.Serialize(new
                    {
                        action = "authenticate",
                        data = token
                    });

                    var authBuffer = System.Text.Encoding.UTF8.GetBytes(authMessage);
                    await _ws.SendAsync(new ArraySegment<byte>(authBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }

                _ = Task.Run(ReceiveLoop); // iniciar el loop de recepción sin bloquear
                return true;
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"WebSocket connection failed: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error connecting to WebSocket: {ex.Message}");
                return false;
            }
        }


        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];

            while (_ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, CancellationToken.None);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                using var doc = await JsonDocument.ParseAsync(ms);
                var root = doc.RootElement;

                if (root.TryGetProperty("action", out var eventProp) &&
                    root.TryGetProperty("data", out var dataProp))
                {
                    var evt = eventProp.GetString();
                    if (evt != null && _eventHandlers.TryGetValue(evt, out var method))
                    {
                        var parameters = method.GetParameters();
                        object?[] args;

                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(JsonElement))
                        {
                            args = new object?[] { dataProp };
                        }
                        else
                        {
                            args = Array.Empty<object>();
                        }

                        var resultInvoke = method.Invoke(_handlerInstance, args);
                        if (resultInvoke is Task task)
                        {
                            await task;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"No handler found for event '{evt}'");
                    }
                }
                else
                {
                    Console.WriteLine("Received message missing 'event' or 'data' properties.");
                }
            }
        }
    }
}
