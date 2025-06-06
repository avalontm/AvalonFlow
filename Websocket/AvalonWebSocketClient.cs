using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AvalonFlow.Websocket
{
    public class AvalonWebSocketClient
    {
        private ClientWebSocket _ws = new();
        private readonly IAvalonFlowClientSocket _handlerInstance;
        private readonly Dictionary<string, MethodInfo> _eventHandlers = new();
        private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
        private readonly int _maxRetries = -1; // -1 para intentos infinitos
        private Uri? _lastUri;
        private string? _lastToken;
        private bool _manualDisconnect = false;
        private CancellationTokenSource _cts = new();

        public bool IsConnected => _ws.State == WebSocketState.Open;

        public AvalonWebSocketClient(IAvalonFlowClientSocket handlerInstance = null)
        {
            _handlerInstance = handlerInstance ?? new AvalonFlowClientDefaultEventHandler();
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
            _lastUri = uri;
            _lastToken = token;
            _manualDisconnect = false;

            try
            {
                if (_ws != null)
                    _ws.Dispose();

                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(uri, CancellationToken.None);
                await _handlerInstance.OnConnectedAsync(_ws);

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

                _ = Task.Run(ReceiveLoop);
                return true;
            }
            catch (Exception ex)
            {
                await _handlerInstance.OnErrorAsync(ex);
                return false;
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];

            try
            {
                while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _ws.ReceiveAsync(buffer, _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (_ws.State != WebSocketState.Open)
                        break;

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
                            object?[] args = parameters.Length == 1 && parameters[0].ParameterType == typeof(JsonElement)
                                ? new object?[] { dataProp }
                                : Array.Empty<object>();

                            var resultInvoke = method.Invoke(_handlerInstance, args);
                            if (resultInvoke is Task task)
                                await task;
                        }
                        else
                        {
                            Console.WriteLine($"No handler found for event '{evt}'");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Receive loop cancelled.");
            }
            catch (Exception ex)
            {
                await _handlerInstance.OnErrorAsync(ex);
            }
            finally
            {
                await _handlerInstance.OnDisconnectedAsync(_ws);

                if (!_manualDisconnect && _lastUri != null && _ws.State != WebSocketState.Open)
                {
                    await AttemptReconnectAsync();
                }
            }
        }

        public async Task DisconnectAsync()
        {
            _manualDisconnect = true;
            _cts.Cancel();

            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);

                var buffer = new byte[1024];
                while (_ws.State != WebSocketState.Closed && _ws.State != WebSocketState.Aborted)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing after server close", CancellationToken.None);
                        break;
                    }
                }
            }

            await _handlerInstance.OnDisconnectedAsync(_ws);
        }


        private async Task AttemptReconnectAsync()
        {
            int retries = 0;

            while ((_maxRetries < 0 || retries < _maxRetries) && !_manualDisconnect)
            {
                await _handlerInstance.OnReconnectingAsync(_ws);

                try
                {
                    await Task.Delay(_reconnectDelay);
                    bool success = await ConnectAsync(_lastUri!, _lastToken);

                    if (success)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    await _handlerInstance.OnErrorAsync(ex);
                }

                retries++;
            }
        }


        public async Task SendMessageAsync(string action, object data)
        {
            if (!IsConnected)
                throw new InvalidOperationException("WebSocket is not open.");

            var message = JsonSerializer.Serialize(new
            {
                action,
                data
            });

            var buffer = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }

    }
}
