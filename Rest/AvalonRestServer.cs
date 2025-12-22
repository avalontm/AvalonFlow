using AvalonFlow.Security;
using System.Net;

namespace AvalonFlow.Rest
{
    public class AvalonRestServer
    {
        private readonly EnhancedRateLimiter _rateLimiter;
        private readonly HttpListener _listener;
        private readonly ControllerRegistry _controllerRegistry;
        private readonly RequestHandler _requestHandler;
        private readonly ResponseHandler _responseHandler;
        private readonly ServerLogger _serverLogger;

        private readonly string _corsAllowedOrigins;
        private readonly string _corsAllowedMethods;
        private readonly string _corsAllowedHeaders;
        private int _port;
        private int _maxBodySize = 10;

        public int MaxBodySize
        {
            get => _maxBodySize;
            set => _maxBodySize = value > 0 ? value : 5;
        }

        public AvalonRestServer(
            int port = 5000,
            bool useHttps = false,
            string corsAllowedOrigins = "*",
            string corsAllowedMethods = "GET, POST, PUT, DELETE, OPTIONS",
            string corsAllowedHeaders = "Content-Type, Authorization",
            int maxBodySizeMB = 10)
        {
            _corsAllowedOrigins = corsAllowedOrigins;
            _corsAllowedMethods = corsAllowedMethods;
            _corsAllowedHeaders = corsAllowedHeaders;

            try
            {
                _maxBodySize = maxBodySizeMB;
                _port = port;
                _listener = new HttpListener();
                string scheme = useHttps ? "https" : "http";

                _listener.Prefixes.Add($"{scheme}://*:{port}/");

                ConfigureHttpListener();

                var rateLimitConfig = new RateLimitConfig();
                _rateLimiter = new EnhancedRateLimiter(rateLimitConfig);

                _controllerRegistry = new ControllerRegistry();
                _requestHandler = new RequestHandler(_rateLimiter, _controllerRegistry, _maxBodySize);
                _responseHandler = new ResponseHandler(_corsAllowedOrigins, _corsAllowedMethods, _corsAllowedHeaders);
                _serverLogger = new ServerLogger(_port);

                _controllerRegistry.RegisterAllControllers();
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error initializing server: {ex.Message}");
                throw;
            }
        }

        public EnhancedRateLimiter GetRateLimiter()
        {
            return _rateLimiter;
        }

        private void ConfigureHttpListener()
        {
            try
            {
                _listener.TimeoutManager.IdleConnection = TimeSpan.FromMinutes(10);
                _listener.TimeoutManager.RequestQueue = TimeSpan.FromMinutes(10);
                _listener.TimeoutManager.HeaderWait = TimeSpan.FromMinutes(5);
                _listener.TimeoutManager.MinSendBytesPerSecond = 512;

                AvalonFlowInstance.Log($"HTTP Listener configured with MaxBodySize: {_maxBodySize}MB");
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Warning: Could not configure HttpListener timeouts: {ex.Message}");
            }
        }

        public void AddService<T>()
        {
            AvalonServiceRegistry.RegisterSingleton<T>(Activator.CreateInstance<T>());
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            try
            {
                _listener.Start();
                _serverLogger.LogServerAddresses();

                while (!ct.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    _ = HandleRequestAsync(context);
                }
            }
            catch (HttpListenerException ex)
            {
                AvalonFlowInstance.Log($"HTTP Listener error: {ex.Message}");
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error in server loop: {ex.Message}");
            }
            finally
            {
                _listener.Stop();
                Console.WriteLine("REST server stopped.");
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                await _requestHandler.HandleAsync(context, _responseHandler);
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error: {ex}");
                await _responseHandler.RespondWithAsync(context, 500, new { error = "Internal server error" });
            }
            finally
            {
                var duration = DateTime.UtcNow - startTime;
                _serverLogger.LogRequest(context.Request, context.Response, duration);
            }
        }
    }
}