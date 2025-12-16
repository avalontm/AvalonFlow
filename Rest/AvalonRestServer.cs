using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AvalonFlow.Rest
{
    public class AvalonRestServer
    {
        private readonly RateLimiter _rateLimiter = new RateLimiter(100, TimeSpan.FromMinutes(1)); // 100 peticiones/minuto
        private readonly HttpListener _listener;
        private readonly Dictionary<string, Type> _controllers = new();
        private readonly string _corsAllowedOrigins;
        private readonly string _corsAllowedMethods;
        private readonly string _corsAllowedHeaders;
        private int _port;
        // Aumentar el tamaño máximo por defecto a 10MB
        private int _maxBodySize = 10; // MB
        private bool _relaxedCspForDocs = true; // Control CSP para documentación

        public int MaxBodySize
        {
            get => _maxBodySize;
            set => _maxBodySize = value > 0 ? value : 5; // Mínimo 5MB
        }

        public AvalonRestServer(int port = 5000, bool useHttps = false, string corsAllowedOrigins = "*", string corsAllowedMethods = "GET, POST, PUT, DELETE, OPTIONS", string corsAllowedHeaders = "Content-Type, Authorization", int maxBodySizeMB = 10)
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

                // Usar * para escuchar en todas las interfaces (equivalente a 0.0.0.0)
                _listener.Prefixes.Add($"{scheme}://*:{port}/");

                // Configurar límites del HttpListener
                ConfigureHttpListener();

                RegisterControllersInAllAssemblies();
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error initializing server: {ex.Message}");
                throw;
            }
        }

        private void ConfigureHttpListener()
        {
            try
            {
                // Configurar el tamaño máximo de datos de entrada
                // HttpListener no tiene una propiedad directa para esto, pero podemos configurar el timeout
                _listener.TimeoutManager.IdleConnection = TimeSpan.FromMinutes(10);
                _listener.TimeoutManager.RequestQueue = TimeSpan.FromMinutes(10);
                _listener.TimeoutManager.HeaderWait = TimeSpan.FromMinutes(5);
                _listener.TimeoutManager.MinSendBytesPerSecond = 512; // 1KB/s mínimo

                AvalonFlowInstance.Log($"HTTP Listener configured with MaxBodySize: {_maxBodySize}MB");
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Warning: Could not configure HttpListener timeouts: {ex.Message}");
            }
        }


        private void LogServerAddresses()
        {
            try
            {
                string hostName = Dns.GetHostName();
                IPAddress[] hostAddresses = Dns.GetHostAddresses(hostName);
                var activeListeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();

                AvalonFlowInstance.Log($"Host: {hostName}");
                AvalonFlowInstance.Log("Direcciones IP locales:");

                foreach (var ip in hostAddresses)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) // IPv4
                    {
                        AvalonFlowInstance.Log($"- {ip}");
                    }
                }

                AvalonFlowInstance.Log("\nInterfaces de red activas:");
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        AvalonFlowInstance.Log($"- {ni.Name} ({ni.Description})");
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                AvalonFlowInstance.Log($"  - {ip.Address}");
                            }
                        }
                    }
                }

                AvalonFlowInstance.Log("\nPuertos escuchando:");
                foreach (var listener in activeListeners)
                {
                    if (listener.Port == _port)
                    {
                        AvalonFlowInstance.Log($"- {listener.Address}:{listener.Port} ({(listener.Address.Equals(IPAddress.Any) ? "PÚBLICO (0.0.0.0)" : (listener.Address.Equals(IPAddress.Loopback) ? "LOCALHOST" : "INTERFAZ ESPECÍFICA"))})");
                    }
                }

                CheckPublicAccessibility();
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error al registrar direcciones: {ex.Message}");
            }
        }

        private void CheckPublicAccessibility()
        {
            try
            {
                bool isPublic = false;
                var activeListeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();

                foreach (var listener in activeListeners)
                {
                    if (listener.Port == _port && listener.Address.Equals(IPAddress.Any))
                    {
                        isPublic = true;
                        break;
                    }
                }

                if (isPublic)
                {
                    AvalonFlowInstance.Log("\nESTADO DE ACCESO: PÚBLICO (0.0.0.0)");
                    AvalonFlowInstance.Log("El servidor está configurado para aceptar conexiones desde cualquier red.");
                    AvalonFlowInstance.Log("ADVERTENCIA: Asegúrate de tener protección adecuada (firewall, autenticación)");
                }
                else
                {
                    AvalonFlowInstance.Log("\nESTADO DE ACCESO: PRIVADO");
                    AvalonFlowInstance.Log("El servidor solo acepta conexiones locales o de interfaces específicas.");
                }
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error al verificar accesibilidad pública: {ex.Message}");
            }
        }

        public void AddService<T>()
        {
            AvalonServiceRegistry.RegisterSingleton<T>(Activator.CreateInstance<T>());
        }

        private void RegisterControllersInAllAssemblies()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                RegisterControllersWithAttribute(assembly);
            }
        }

        private void RegisterControllersWithAttribute(Assembly assembly)
        {
            var controllerTypes = assembly.GetTypes()
                .Where(t =>
                    t.GetCustomAttribute<AvalonControllerAttribute>() != null &&
                    typeof(IAvalonController).IsAssignableFrom(t) &&
                    !t.IsAbstract && t.IsClass);

            foreach (var type in controllerTypes)
            {
                var attr = type.GetCustomAttribute<AvalonControllerAttribute>();
                var controllerName = type.Name.ToLowerInvariant().Replace("controller", "");

                string route = attr.Route.ToLowerInvariant();
                route = route.Replace("[controller]", controllerName).Trim('/');

                _controllers[route] = type;
            }
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            try
            {
                _listener.Start();
                LogServerAddresses();

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
                var clientIp = context.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";

                if (!_rateLimiter.IsAllowed(clientIp))
                {
                    await RespondWith(context, 429, new { error = "Too many requests. Please try again later." });
                    return;
                }

                var request = context.Request;
                var response = context.Response;

                if (request.HasEntityBody && request.ContentLength64 > 0)
                {
                    long maxBytesAllowed = (long)_maxBodySize * 1024 * 1024;

                    if (request.ContentLength64 > maxBytesAllowed)
                    {
                        var errorResponse = new
                        {
                            error = "Request entity too large",
                            maxAllowedSizeMB = _maxBodySize,
                            receivedSizeMB = request.ContentLength64 / (1024.0 * 1024.0),
                            suggestion = $"Split your request into smaller chunks or contact support to increase the limit"
                        };

                        await RespondWith(context, 413, errorResponse);
                        return;
                    }
                }

                if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    AddCorsHeaders(response);
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                string method = context.Request.HttpMethod.ToUpperInvariant();
                string requestPath = context.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();
                string[] requestSegments = string.IsNullOrEmpty(requestPath)
                    ? new string[0]
                    : requestPath.Split('/');

                // Buscar coincidencia exacta de controlador
                // Itera desde la ruta más larga posible hasta la más corta
                Type controllerType = null;
                string matchedControllerKey = null;
                string subPath = "";

                // Ordenar las rutas de controladores por longitud descendente para hacer match con las más específicas primero
                var sortedControllers = _controllers
                    .OrderByDescending(kvp => kvp.Key.Split('/').Length)
                    .ThenByDescending(kvp => kvp.Key.Length);

                foreach (var controllerEntry in sortedControllers)
                {
                    string controllerRoute = controllerEntry.Key; // ej: "v1/deliverypricing" o "v2/admin/deliverypricing"
                    string[] controllerSegments = controllerRoute.Split('/');

                    // Verificar si la ruta de la petición comienza con la ruta del controlador
                    if (requestSegments.Length >= controllerSegments.Length)
                    {
                        bool isMatch = true;

                        for (int i = 0; i < controllerSegments.Length; i++)
                        {
                            if (!requestSegments[i].Equals(controllerSegments[i], StringComparison.OrdinalIgnoreCase))
                            {
                                isMatch = false;
                                break;
                            }
                        }

                        if (isMatch)
                        {
                            controllerType = controllerEntry.Value;
                            matchedControllerKey = controllerRoute;

                            // El subPath son los segmentos restantes después del controlador
                            var remainingSegments = requestSegments.Skip(controllerSegments.Length);
                            subPath = remainingSegments.Any()
                                ? "/" + string.Join("/", remainingSegments)
                                : "/";

                            break;
                        }
                    }
                }

                if (controllerType == null)
                {
                    await RespondWith(context, 404, new
                    {
                        error = "Controller not found",
                        requestedPath = requestPath,
                        availableRoutes = _controllers.Keys.ToArray()
                    });
                    return;
                }

                var controllerInstance = Activator.CreateInstance(controllerType)!;

                if (controllerInstance is AvalonControllerBase baseController)
                {
                    baseController.HttpContext = new AvalonHttpContext(context);
                }

                var methods = controllerType.GetMethods()
                    .Where(m => m.GetCustomAttribute<AvalonRestAttribute>() is not null)
                    .ToList();

                MethodInfo? matchedMethod = null;
                Dictionary<string, string> routeParams = new();

                foreach (var methodInfo in methods)
                {
                    var attr = methodInfo.GetCustomAttribute<AvalonRestAttribute>()!;
                    if (!attr.Method.ToString().Equals(method, StringComparison.OrdinalIgnoreCase)) continue;

                    var templateParts = attr.Path.Trim('/').Split('/');
                    var requestParts = subPath.Trim('/').Split('/');

                    if (subPath.Trim('/') == string.Empty)
                    {
                        requestParts = new string[0];
                    }

                    if (templateParts.Length == 1 && templateParts[0] == string.Empty)
                    {
                        templateParts = new string[0];
                    }

                    if (templateParts.Length != requestParts.Length) continue;

                    bool isMatch = true;
                    var tempParams = new Dictionary<string, string>();

                    for (int i = 0; i < templateParts.Length; i++)
                    {
                        if (templateParts[i].StartsWith("{") && templateParts[i].EndsWith("}"))
                        {
                            string key = templateParts[i].Trim('{', '}');
                            tempParams[key] = requestParts[i];
                        }
                        else if (!string.Equals(templateParts[i], requestParts[i], StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = false;
                            break;
                        }
                    }

                    if (isMatch)
                    {
                        matchedMethod = methodInfo;
                        routeParams = tempParams;
                        break;
                    }
                }

                if (matchedMethod == null)
                {
                    await RespondWith(context, 404, new
                    {
                        error = "Route not found",
                        controller = matchedControllerKey,
                        subPath = subPath,
                        method = method
                    });
                    return;
                }

                // Auth (resto del código sin cambios)
                var allowAnonymous = matchedMethod.GetCustomAttribute<AllowAnonymousAttribute>() != null;
                var authorizeAttr = matchedMethod.GetCustomAttribute<AuthorizeAttribute>() ??
                                    controllerType.GetCustomAttribute<AuthorizeAttribute>();

                ClaimsPrincipal? userPrincipal = null;

                if (!allowAnonymous && authorizeAttr != null)
                {
                    string? scheme = authorizeAttr.AuthenticationScheme?.Split(',').FirstOrDefault()?.Trim();
                    scheme ??= "Bearer";

                    if (!scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        await RespondWith(context, 401, new { error = "Unauthorized: Unsupported authentication scheme" });
                        return;
                    }

                    string? authHeader = GetHeaderValue(context.Request, "Authorization");

                    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        await RespondWith(context, 401, new { error = "Unauthorized: Missing or invalid token" });
                        return;
                    }

                    string token = authHeader["Bearer ".Length..].Trim();
                    userPrincipal = AvalonFlowInstance.ValidateJwtToken(token);
                    if (userPrincipal == null)
                    {
                        await RespondWith(context, 401, new { error = "Unauthorized: Invalid token" });
                        return;
                    }

                    if (controllerInstance is AvalonControllerBase secured)
                    {
                        secured.HttpContext.User = userPrincipal;
                    }

                    var rolesProperty = authorizeAttr.GetType().GetProperty("Roles");
                    var requiredRoles = rolesProperty?.GetValue(authorizeAttr) as string;

                    if (!string.IsNullOrEmpty(requiredRoles))
                    {
                        var rolesList = requiredRoles.Split(',').Select(r => r.Trim()).Where(r => !string.IsNullOrEmpty(r));
                        bool hasRequiredRole = rolesList.Any(role => userPrincipal.IsInRole(role));

                        if (!hasRequiredRole)
                        {
                            await RespondWith(context, 403, new { error = "Forbidden: Insufficient role" });
                            return;
                        }
                    }
                }

                try
                {
                    object[] parameters = await ResolveParameters(matchedMethod, context, routeParams);

                    var result = matchedMethod.Invoke(controllerInstance, parameters);

                    if (result is Task task)
                    {
                        await task;
                        result = task.GetType().IsGenericType
                            ? task.GetType().GetProperty("Result")?.GetValue(task)
                            : null;
                    }

                    if (result is ContentResult contentResult)
                    {
                        await RespondWith(context, contentResult.StatusCode, contentResult);
                        return;
                    }

                    if (result is ActionResult actionResult)
                    {
                        var valueToReturn = actionResult.GetType().GetProperty("Value")?.GetValue(actionResult) ?? new { };
                        await RespondWith(context, actionResult.StatusCode, valueToReturn);
                    }
                    else
                    {
                        await RespondWith(context, 200, result ?? new { });
                    }
                }
                catch (InvalidOperationException invEx)
                {
                    await RespondWith(context, 400, new { error = invEx.Message });
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error: {ex}");
                    await RespondWith(context, 500, new { error = "Internal server error" });
                }
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error: {ex}");
                await RespondWith(context, 500, new { error = "Internal server error" });
            }
            finally
            {
                var duration = DateTime.UtcNow - startTime;
                LogRequest(context.Request, context.Response, duration);
            }
        }

        // Método mejorado para obtener headers de manera más robusta
        private string? GetHeaderValue(HttpListenerRequest request, string headerName)
        {
            try
            {
                // Método 1: Acceso directo
                var headerValue = request.Headers[headerName];
                if (!string.IsNullOrEmpty(headerValue))
                {
                    return headerValue;
                }

                // Método 2: Búsqueda case-insensitive usando AllKeys
                if (request.Headers.AllKeys != null)
                {
                    foreach (string key in request.Headers.AllKeys)
                    {
                        if (string.Equals(key, headerName, StringComparison.OrdinalIgnoreCase))
                        {
                            var value = request.Headers[key];
                            return value;
                        }
                    }
                }

                // Método 3: Búsqueda usando GetKey/Get
                for (int i = 0; i < request.Headers.Count; i++)
                {
                    string key = request.Headers.GetKey(i);
                    if (string.Equals(key, headerName, StringComparison.OrdinalIgnoreCase))
                    {
                        var value = request.Headers.Get(i);
                        return value;
                    }
                }

                // Método 4: Búsqueda con variaciones comunes del nombre
                var headerVariations = new[] {
            headerName.ToLowerInvariant(),
            headerName.ToUpperInvariant(),
            headerName.Replace("_", "-"),
            headerName.Replace("-", "_"),
            $"X-{headerName}",
            $"x-{headerName}",
            headerName.Replace("_", "-").ToLowerInvariant(),
            headerName.Replace("-", "_").ToLowerInvariant()
        };

                foreach (var variation in headerVariations)
                {
                    var value = request.Headers[variation];
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error getting header '{headerName}': {ex.Message}");
                return null;
            }
        }

        private async Task<object[]> ResolveParameters(MethodInfo method, HttpListenerContext context, Dictionary<string, string> routeParams)
        {
            var parameters = method.GetParameters();
            var resolved = new List<object>();

            // Variables para almacenar los datos parseados
            string? body = null;
            Dictionary<string, string>? formData = null;
            Dictionary<string, MultipartFormDataParser.FormField>? multipartData = null;
            JsonDocument? jsonDoc = null;
            bool isJsonRequest = false;

            if (context.Request.HasEntityBody)
            {
                try
                {
                    // Verificación adicional del tamaño durante la lectura
                    long maxBytesAllowed = (long)_maxBodySize * 1024 * 1024;

                    // Leer el cuerpo de forma controlada
                    using var reader = new StreamReader(context.Request.InputStream);
                    var buffer = new char[65536]; // 64KB buffer
                    var stringBuilder = new StringBuilder();
                    int totalBytesRead = 0;
                    int bytesRead;

                    while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        totalBytesRead += Encoding.UTF8.GetByteCount(buffer, 0, bytesRead);

                        if (totalBytesRead > maxBytesAllowed)
                        {
                            throw new InvalidOperationException($"Request body exceeds maximum allowed size of {_maxBodySize}MB");
                        }

                        stringBuilder.Append(buffer, 0, bytesRead);

                        // Log de progreso para requests grandes
                        if (totalBytesRead > 10 * 1024 * 1024) // > 10MB
                        {
                            var progressMB = totalBytesRead / (1024.0 * 1024.0);
                            if ((int)progressMB % 50 == 0) // Log cada 50MB
                            {
                                AvalonFlowInstance.Log($"Reading request body progress: {progressMB:F1}MB");
                            }
                        }
                    }

                    body = stringBuilder.ToString();

                    string contentType = context.Request.ContentType ?? "";

                    // Determinar si es una solicitud JSON
                    isJsonRequest = contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                                   parameters.Any(p => p.GetCustomAttribute<FromBodyAttribute>() != null);

                    if (isJsonRequest && !string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            jsonDoc = JsonDocument.Parse(body);
                        }
                        catch (JsonException ex)
                        {
                            AvalonFlowInstance.Log($"Error parsing JSON: {ex.Message}. Body content: {body}");
                            throw new InvalidOperationException($"Invalid JSON format: {ex.Message}");
                        }
                    }
                    else if (!isJsonRequest && !string.IsNullOrWhiteSpace(body))
                    {
                        // Procesar form data solo si no es JSON
                        bool hasFormParam = parameters.Any(p =>
                            p.GetCustomAttribute<FromFormAttribute>() != null ||
                            p.GetCustomAttribute<FromFileAttribute>() != null);

                        if (hasFormParam)
                        {
                            if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                            {
                                // Para multipart, necesitamos recrear el stream
                                using var bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(body));
                                multipartData = await MultipartFormDataParser.ParseAsync(bodyStream, contentType);
                                formData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                                foreach (var field in multipartData.Values)
                                {
                                    if (!field.IsFile) // Solo campos de formulario normales
                                    {
                                        formData[field.Name] = field.Value;
                                    }
                                }
                            }
                            else if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                            {
                                formData = ParseUrlEncodedData(body);
                            }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Re-lanzar excepciones de tamaño
                    throw;
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error reading request body: {ex.Message}");
                    throw new InvalidOperationException($"Error processing request body: {ex.Message}");
                }
            }

            // Procesar cada parámetro del método
            foreach (var param in parameters)
            {
                try
                {
                    object? resolvedValue = ResolveParameter(param, context, routeParams,
                        new ParameterResolutionContext
                        {
                            Body = body,
                            FormData = formData,
                            MultipartData = multipartData,
                            JsonDoc = jsonDoc,
                            IsJsonRequest = isJsonRequest
                        });

                    resolved.Add(resolvedValue);
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error resolving parameter '{param.Name}': {ex.Message}");
                    throw;
                }
            }

            return resolved.ToArray();
        }

        private object? ResolveParameter(ParameterInfo param, HttpListenerContext context,
            Dictionary<string, string> routeParams, ParameterResolutionContext ctx)
        {
            var fromBody = param.GetCustomAttribute<FromBodyAttribute>() != null;
            var fromHeader = param.GetCustomAttribute<FromHeaderAttribute>();
            var fromQuery = param.GetCustomAttribute<FromQueryAttribute>();
            var fromForm = param.GetCustomAttribute<FromFormAttribute>();
            var fromFile = param.GetCustomAttribute<FromFileAttribute>();

            // 1. FromFile - Manejo de archivos
            if (fromFile != null && ctx.MultipartData != null)
            {
                return ResolveFileParameter(param, fromFile, ctx.MultipartData);
            }

            // 2. FromForm - Campos de formulario
            if (fromForm != null && !ctx.IsJsonRequest && (ctx.FormData != null || ctx.MultipartData != null))
            {
                return ResolveFormParameter(param, fromForm, ctx.FormData, ctx.MultipartData);
            }

            // 3. FromBody - JSON
            if (fromBody)
            {
                return ResolveBodyParameter(param, ctx.JsonDoc, ctx.Body);
            }

            // 4. FromHeader - Encabezados
            if (fromHeader != null)
            {
                return ResolveHeaderParameter(param, fromHeader, context.Request);
            }

            // 5. FromQuery - Parámetros de consulta
            if (fromQuery != null)
            {
                return ResolveQueryParameter(param, fromQuery, context.Request);
            }

            // 6. HttpListenerContext
            if (param.ParameterType == typeof(HttpListenerContext))
            {
                return context;
            }

            // 7. Route parameters
            if (routeParams.TryGetValue(param.Name!.ToLowerInvariant(), out var routeValue))
            {
                return Convert.ChangeType(routeValue, param.ParameterType);
            }

            // 8. Valor por defecto
            return param.HasDefaultValue ? param.DefaultValue :
                   param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null;
        }

        private object? ResolveFileParameter(ParameterInfo param, FromFileAttribute fromFile,
            Dictionary<string, MultipartFormDataParser.FormField> multipartData)
        {
            string fieldName = fromFile.Name ?? param.Name!;

            if (!multipartData.TryGetValue(fieldName, out var fileField) || !fileField.IsFile)
            {
                return param.HasDefaultValue ? param.DefaultValue : null;
            }

            if (param.ParameterType == typeof(IFormFile))
            {
                var stream = fileField.FileData != null
                    ? new MemoryStream(fileField.FileData)
                    : new MemoryStream();
                stream.Position = 0;

                return new FormFile(
                    stream: stream,
                    name: fieldName,
                    fileName: fileField.FileName ?? Path.GetRandomFileName(),
                    contentType: fileField.ContentType ?? "application/octet-stream",
                    length: fileField.FileData?.Length ?? 0
                );
            }

            // Otros tipos soportados para archivos
            if (param.ParameterType == typeof(MultipartFormDataParser.FormField))
            {
                return fileField;
            }
            else if (param.ParameterType == typeof(byte[]))
            {
                return fileField.FileData ?? Array.Empty<byte>();
            }
            else if (param.ParameterType == typeof(string))
            {
                return fileField.FileName ?? "";
            }
            else if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
            {
                return CreateFileModel(param.ParameterType, fileField);
            }

            return fileField;
        }

        private object? ResolveFormParameter(ParameterInfo param, FromFormAttribute fromForm,
            Dictionary<string, string> formData, Dictionary<string, MultipartFormDataParser.FormField> multipartData)
        {
            string fieldName = fromForm.Name ?? param.Name!;

            // Buscar en formData (campos normales)
            if (formData != null && formData.TryGetValue(fieldName, out var formValue))
            {
                return Convert.ChangeType(formValue, param.ParameterType);
            }

            // Buscar en multipartData (si no se encontró en formData)
            if (multipartData != null && multipartData.TryGetValue(fieldName, out var multipartField))
            {
                if (!multipartField.IsFile) // Solo campos de formulario normales
                {
                    return Convert.ChangeType(multipartField.Value, param.ParameterType);
                }
            }

            // Para objetos complejos
            if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
            {
                return CreateFormModel(param.ParameterType, fromForm, formData, multipartData);
            }

            return param.HasDefaultValue ? param.DefaultValue : null;
        }

        private object? ResolveBodyParameter(ParameterInfo param, JsonDocument jsonDoc, string body)
        {
            if (jsonDoc == null)
            {
                if (!string.IsNullOrWhiteSpace(body))
                {
                    AvalonFlowInstance.Log($"FromBody parameter but no valid JSON. Body content: {body}");
                    throw new InvalidOperationException($"FromBody parameter '{param.Name}' requires valid JSON content");
                }
                return param.HasDefaultValue ? param.DefaultValue : null;
            }

            if (param.ParameterType == typeof(JsonElement))
            {
                return jsonDoc.RootElement.Clone();
            }
            else if (param.ParameterType == typeof(JsonDocument))
            {
                return jsonDoc;
            }

            try
            {
                return JsonSerializer.Deserialize(
                    jsonDoc.RootElement.GetRawText(),
                    param.ParameterType,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }
                ) ?? (param.HasDefaultValue ? param.DefaultValue : null);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Invalid JSON format for parameter '{param.Name}': {ex.Message}");
            }
        }

        private object? ResolveHeaderParameter(ParameterInfo param, FromHeaderAttribute fromHeader, HttpListenerRequest request)
        {
            string headerName = fromHeader.Name ?? param.Name!;

            // Lista de posibles variantes del nombre del header
            var possibleHeaderNames = new List<string>
            {
                headerName, // Nombre original
                headerName.Replace('_', '-'), // Reemplazar guiones bajos por guiones
                headerName.Replace('-', '_')  // Reemplazar guiones por guiones bajos
            }.Distinct(); // Eliminar duplicados si hay

            string headerValue = null;

            // Buscar el header en todas las variantes posibles
            foreach (var name in possibleHeaderNames)
            {
                headerValue = request.Headers[name];
                if (!string.IsNullOrEmpty(headerValue))
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(headerValue))
            {
                if (param.HasDefaultValue)
                    return param.DefaultValue;

                throw new InvalidOperationException($"Missing required header. Tried: {string.Join(", ", possibleHeaderNames)}");
            }

            try
            {
                return Convert.ChangeType(headerValue, param.ParameterType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot convert header value '{headerValue}' to type '{param.ParameterType.Name}': {ex.Message}");
            }
        }

        private object? ResolveQueryParameter(ParameterInfo param, FromQueryAttribute fromQuery, HttpListenerRequest request)
        {
            string queryName = fromQuery.Name ?? param.Name!;

            // Generar todas las posibles variantes del nombre del parámetro
            var possibleParamNames = new List<string>
            {
                queryName, // Nombre original
                queryName.Replace('_', '-'), // Reemplazar guiones bajos por guiones
                queryName.Replace('-', '_')  // Reemplazar guiones por guiones bajos
            }.Distinct().ToList(); // Eliminar duplicados

            string queryValue = null;

            // Buscar el parámetro en todas las variantes posibles
            foreach (var name in possibleParamNames)
            {
                queryValue = request.QueryString[name];
                if (!string.IsNullOrEmpty(queryValue))
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(queryValue))
            {
                return param.HasDefaultValue ? param.DefaultValue :
                       param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null;
            }

            try
            {
                return Convert.ChangeType(queryValue, param.ParameterType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot convert query parameter '{queryName}' value '{queryValue}' " +
                    $"to type '{param.ParameterType.Name}'. Tried variants: {string.Join(", ", possibleParamNames)}. " +
                    $"Error: {ex.Message}");
            }
        }

        private object? CreateFileModel(Type modelType, MultipartFormDataParser.FormField fileField)
        {
            var instance = Activator.CreateInstance(modelType);
            var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (!property.CanWrite) continue;

                switch (property.Name.ToLowerInvariant())
                {
                    case "filename":
                        property.SetValue(instance, fileField.FileName);
                        break;
                    case "contenttype":
                        property.SetValue(instance, fileField.ContentType);
                        break;
                    case "data":
                    case "content":
                    case "filedata":
                        if (property.PropertyType == typeof(byte[]))
                            property.SetValue(instance, fileField.FileData);
                        break;
                    case "size":
                    case "length":
                        if (property.PropertyType == typeof(int) || property.PropertyType == typeof(long))
                            property.SetValue(instance, Convert.ChangeType(fileField.FileData?.Length ?? 0, property.PropertyType));
                        break;
                }
            }

            return instance;
        }

        private object? CreateFormModel(Type modelType, FromFormAttribute fromForm,
            Dictionary<string, string> formData, Dictionary<string, MultipartFormDataParser.FormField> multipartData)
        {
            var instance = Activator.CreateInstance(modelType);
            var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (!property.CanWrite) continue;

                string formKey = !string.IsNullOrEmpty(fromForm.Name)
                    ? $"{fromForm.Name}.{property.Name}"
                    : property.Name;

                string formValue = null;

                // Buscar en formData primero
                if (formData != null && formData.TryGetValue(formKey, out var fdValue))
                {
                    formValue = fdValue;
                }
                // Buscar en multipartData si no se encontró
                else if (multipartData != null && multipartData.TryGetValue(formKey, out var mpField) && !mpField.IsFile)
                {
                    formValue = mpField.Value;
                }

                if (formValue != null)
                {
                    try
                    {
                        Type targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                        object convertedValue = Convert.ChangeType(formValue, targetType);
                        property.SetValue(instance, convertedValue);
                    }
                    catch (Exception ex)
                    {
                        AvalonFlowInstance.Log($"Error converting form field '{formKey}': {ex.Message}");
                    }
                }
            }

            return instance;
        }

        private Dictionary<string, string> ParseUrlEncodedData(string formData)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pairs = formData.Split('&');

            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2)
                {
                    result[Uri.UnescapeDataString(keyValue[0])] = Uri.UnescapeDataString(keyValue[1]);
                }
            }

            return result;
        }


        // Método auxiliar para conversión de valores
        private object? ConvertValue(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value))
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType == typeof(string))
            {
                return value;
            }

            // Intentar JSON primero si parece ser JSON
            if ((value.Trim().StartsWith("{") && value.Trim().EndsWith("}")) ||
                (value.Trim().StartsWith("[") && value.Trim().EndsWith("]")))
            {
                try
                {
                    return JsonSerializer.Deserialize(value, targetType, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    // Si falla la deserialización JSON, continuar con conversión normal
                }
            }

            if (targetType == typeof(Guid) && Guid.TryParse(value, out var guidValue))
            {
                return guidValue;
            }

            if (targetType.IsEnum && Enum.TryParse(targetType, value, true, out var enumValue))
            {
                return enumValue;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(value, out var boolValue))
                    return boolValue;
                if (value.Equals("1")) return true;
                if (value.Equals("0")) return false;
            }

            // Manejar tipos nullable
            Type underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                return ConvertValue(value, underlyingType);
            }

            return Convert.ChangeType(value, targetType);
        }

        private void AddSecurityHeaders(HttpListenerResponse response, bool isDocsEndpoint = false)
        {
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            response.Headers.Add("X-Frame-Options", "DENY");
            response.Headers.Add("X-XSS-Protection", "1; mode=block");
            response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            response.Headers.Add("Referrer-Policy", "no-referrer");
            response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=()");

            if (isDocsEndpoint && _relaxedCspForDocs)
            {
                response.Headers.Add("Content-Security-Policy",
                    "default-src 'self'; " +
                    "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com https://cdn.jsdelivr.net; " +
                    "style-src 'self' 'unsafe-inline' https://unpkg.com https://cdn.jsdelivr.net; " +
                    "img-src 'self' data: https: http:; " +
                    "font-src 'self' data: https://unpkg.com; " +
                    "connect-src 'self' https://unpkg.com http://localhost:5000;");
            }
            else
            {
                response.Headers.Add("Content-Security-Policy", "default-src 'self'");
            }
        }

        private void AddCorsHeaders(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = _corsAllowedOrigins;
            response.Headers["Access-Control-Allow-Methods"] = _corsAllowedMethods;
            response.Headers["Access-Control-Allow-Headers"] = _corsAllowedHeaders;
            response.Headers["Access-Control-Allow-Credentials"] = "true";
        }

        private async Task RespondWith(HttpListenerContext context, int statusCode, object value)
        {
            var response = context.Response;
            response.StatusCode = statusCode;
            try
            {
                AddCorsHeaders(response);

                // Detectar si es endpoint de documentación
                string path = context.Request.Url?.AbsolutePath?.ToLowerInvariant() ?? "";
                bool isDocsEndpoint = path.Contains("/docs") || path.Contains("/swagger");

                if (!response.Headers.AllKeys.Any(k => k.Equals("Content-Security-Policy", StringComparison.OrdinalIgnoreCase)))
                {
                    AddSecurityHeaders(response, isDocsEndpoint);
                }
                else
                {
                    response.Headers.Add("X-Content-Type-Options", "nosniff");
                    response.Headers.Add("X-Frame-Options", "DENY");
                    response.Headers.Add("X-XSS-Protection", "1; mode=block");
                    response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
                    response.Headers.Add("Referrer-Policy", "no-referrer");
                    response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=()");
                }

                // Manejo especial para ContentResult
                if (value is ContentResult contentResult)
                {
                    response.ContentType = contentResult.ContentType;
                    byte[] buffer = contentResult.ContentEncoding.GetBytes(contentResult.Content);
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    return;
                }

                // Si es ActionResult, extraer el Value
                if (value is ActionResult actionResult)
                {
                    value = actionResult.Value;
                }

                // Manejo para otros tipos de respuesta
                switch (value)
                {
                    case FileActionResult file:
                        await HandleFileResponse(response, file);
                        break;

                    case Stream stream:
                        await HandleGenericStreamResponse(response, stream);
                        break;

                    case string str when !response.ContentType.StartsWith("application/json"):
                        response.ContentType = "text/plain";
                        byte[] strBuffer = Encoding.UTF8.GetBytes(str);
                        await response.OutputStream.WriteAsync(strBuffer, 0, strBuffer.Length);
                        break;

                    default:
                        if (value != null)
                        {
                            await HandleJsonResponse(response, value);
                        }
                        else
                        {
                            response.ContentLength64 = 0;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error writing response: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    var error = new { error = "Internal server error", details = ex.Message };
                    await HandleJsonResponse(response, error);
                }
                catch
                {
                    // Si falla el envío del error, simplemente cerramos
                }
            }
            finally
            {
                try
                {
                    if (response.OutputStream.CanWrite)
                    {
                        await response.OutputStream.FlushAsync();
                    }
                    response.Close();
                }
                catch
                {
                    // Ignorar errores al cerrar
                }
            }
        }

        private async Task HandleFileResponse(HttpListenerResponse response, FileActionResult file)
        {
            response.ContentType = file.ContentType ?? "application/octet-stream";
            response.AddHeader("Content-Disposition", $"attachment; filename=\"{file.FileName}\"");
            response.ContentLength64 = file.Content.Length;

            await response.OutputStream.WriteAsync(file.Content, 0, file.Content.Length);
        }

        private async Task HandleStreamFileResponse(HttpListenerResponse response, StreamFileActionResult streamFile)
        {
            response.ContentType = streamFile.ContentType ?? "application/octet-stream";

            if (!string.IsNullOrEmpty(streamFile.FileName))
            {
                var dispositionType = streamFile.IsAttachment ? "attachment" : "inline";
                response.AddHeader("Content-Disposition",
                    $"{dispositionType}; filename=\"{streamFile.FileName}\"");
            }

            try
            {
                byte[] buffer = new byte[81920]; // 80 KB buffer
                int bytesRead;
                long totalBytes = 0;

                while ((bytesRead = await streamFile.ContentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytes += bytesRead;

                    // Actualizar el ContentLength64 si no estaba establecido
                    if (response.ContentLength64 == 0)
                    {
                        try
                        {
                            response.ContentLength64 = streamFile.ContentStream.Length;
                        }
                        catch
                        {
                            // Algunos streams no soportan Length
                        }
                    }
                }
            }
            finally
            {
                streamFile.ContentStream.Close();
            }
        }

        private async Task HandleGenericStreamResponse(HttpListenerResponse response, Stream stream)
        {
            response.ContentType = "application/octet-stream";

            try
            {
                byte[] buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                }
            }
            finally
            {
                stream.Close();
            }
        }

        private async Task HandleJsonResponse(HttpListenerResponse response, object value)
        {
            response.ContentType = "application/json; charset=utf-8";

            var responseJson = JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var buffer = Encoding.UTF8.GetBytes(responseJson);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private void LogRequest(HttpListenerRequest request, HttpListenerResponse response, TimeSpan duration)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                ClientIP = request.RemoteEndPoint?.Address?.ToString(),
                Method = request.HttpMethod,
                Url = request.Url?.AbsoluteUri,
                StatusCode = response.StatusCode,
                DurationMs = duration.TotalMilliseconds,
                UserAgent = request.UserAgent,
                ContentLength = request.ContentLength64,
                Headers = request.Headers.AllKeys.ToDictionary(k => k, k => request.Headers[k])
            };

            AvalonFlowInstance.Log(JsonSerializer.Serialize(logEntry));
        }
    }
}