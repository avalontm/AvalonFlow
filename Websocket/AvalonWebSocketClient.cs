using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AvalonFlow.Websocket
{
    public class AvalonWebSocketClient
    {
        private AvalonSocketWebClient _client = new();
        private readonly IAvalonFlowClientSocket _handlerInstance;
        private readonly Dictionary<string, MethodInfo> _eventHandlers = new();
        private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
        private readonly int _maxRetries = -1; // -1 para intentos infinitos
        private Uri? _lastUri;
        private string? _lastToken;
        private bool _manualDisconnect = false;
        private CancellationTokenSource _cts = new();

        public bool IsConnected => _client.IsConnected;

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
                if (_client.webSocket != null)
                    _client.webSocket.Dispose();

                _client.SetWebSocket(new ClientWebSocket());
                await _client.webSocket.ConnectAsync(uri, CancellationToken.None);
                await _handlerInstance.OnConnectedAsync();

                if (!string.IsNullOrEmpty(token))
                {
                    var authMessage = JsonSerializer.Serialize(new
                    {
                        action = "authenticate",
                        data = token
                    });

                    var authBuffer = System.Text.Encoding.UTF8.GetBytes(authMessage);
                    await _client.webSocket.SendAsync(new ArraySegment<byte>(authBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
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
                while (_client.IsConnected && !_cts.Token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _client.webSocket.ReceiveAsync(buffer, _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (!_client.IsConnected)
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
                            try
                            {
                                var args = new object[] { _client, dataProp };
                                var resultInvoke = method.Invoke(_handlerInstance, args);

                                if (resultInvoke is Task task)
                                    await task;
                            }
                            catch (Exception ex)
                            {
                                await _handlerInstance.OnErrorAsync(ex);
                            }
                        }
                        else
                        {
                            // Si no hay un manejador para el evento, se puede manejar aquí o ignorar
                            Console.WriteLine($"No handler for event: {evt}");
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
                await _handlerInstance.OnDisconnectedAsync();

                if (!_manualDisconnect && _lastUri != null && !_client.IsConnected)
                {
                    await AttemptReconnectAsync();
                }
            }
        }

        public async Task DisconnectAsync()
        {
            _manualDisconnect = true;
            _cts.Cancel();

            if (_client.IsConnected || _client.webSocket.State == WebSocketState.CloseReceived)
            {
                await _client.webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);

                var buffer = new byte[1024];
                while (_client.webSocket.State != WebSocketState.Closed && _client.webSocket.State != WebSocketState.Aborted)
                {
                    var result = await _client.webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _client.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing after server close", CancellationToken.None);
                        break;
                    }
                }
            }

            await _handlerInstance.OnDisconnectedAsync();
        }


        private async Task AttemptReconnectAsync()
        {
            int retries = 0;

            while ((_maxRetries < 0 || retries < _maxRetries) && !_manualDisconnect)
            {
                await _handlerInstance.OnReconnectingAsync();

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
            await _client.webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }

    }
}
