using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Web;

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
        private int _maxBodySize = 10;


        public int MaxBodySize
        {
            get { return _maxBodySize; }
            set { _maxBodySize = value; }
        }

        public AvalonRestServer(int port = 5000, bool useHttps = false, string corsAllowedOrigins = "*", string corsAllowedMethods = "GET, POST, PUT, DELETE, OPTIONS", string corsAllowedHeaders = "Content-Type, Authorization")
        {
            _corsAllowedOrigins = corsAllowedOrigins;
            _corsAllowedMethods = corsAllowedMethods;
            _corsAllowedHeaders = corsAllowedHeaders;

            try
            {
                _port = port;
                _listener = new HttpListener();
                string scheme = useHttps ? "https" : "http";

                // Usar * para escuchar en todas las interfaces (equivalente a 0.0.0.0)
                _listener.Prefixes.Add($"{scheme}://*:{port}/");

                RegisterControllersInAllAssemblies();
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error initializing server: {ex.Message}");
                throw;
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
                // Obtener IP del cliente
                var clientIp = context.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";

                // Verificar rate limiting
                if (!_rateLimiter.IsAllowed(clientIp))
                {
                    await RespondWith(context, 429, new { error = "Too many requests. Please try again later." });
                    return;
                }

                var request = context.Request;
                var response = context.Response;

                if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    AddCorsHeaders(response);
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                string method = context.Request.HttpMethod.ToUpperInvariant();
                string[] segments = context.Request.Url.AbsolutePath.Trim('/').Split('/');

                // Mejorar la lógica de parsing de rutas para manejar múltiples "api" en la URL
                int apiIndex = -1;
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].Equals("api", StringComparison.OrdinalIgnoreCase))
                    {
                        apiIndex = i;
                        break;
                    }
                }

                if (apiIndex == -1 || apiIndex + 1 >= segments.Length)
                {
                    await RespondWith(context, 404, new { error = "Invalid route - API endpoint not found" });
                    return;
                }

                // Usar el primer "api" encontrado como base
                string controllerName = segments[apiIndex + 1].ToLowerInvariant();
                string controllerKey = $"api/{controllerName}";

                // Construir subPath con los segmentos restantes después del controlador
                var remainingSegments = segments.Skip(apiIndex + 2);
                string subPath = "/" + string.Join("/", remainingSegments).ToLowerInvariant();

                if (!_controllers.TryGetValue(controllerKey, out var controllerType))
                {
                    await RespondWith(context, 404, new { error = $"Controller not found: {controllerKey}" });
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

                    // Si subPath está vacío, requestParts tendrá un elemento vacío
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
                    await RespondWith(context, 404, new { error = "Route not found" });
                    return;
                }

                // Auth
                var allowAnonymous = matchedMethod.GetCustomAttribute<AllowAnonymousAttribute>() != null;
                var authorizeAttr = matchedMethod.GetCustomAttribute<AuthorizeAttribute>() ??
                                    controllerType.GetCustomAttribute<AuthorizeAttribute>();

                ClaimsPrincipal? userPrincipal = null;

                if (!allowAnonymous && authorizeAttr != null)
                {
                    string? scheme = authorizeAttr.AuthenticationScheme?.Split(',').FirstOrDefault()?.Trim();

                    // Por defecto, asumimos esquema "Bearer" si no se especifica ninguno
                    scheme ??= "Bearer";

                    if (!scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        // Si el esquema no es "Bearer", se niega el acceso (podrías agregar otros esquemas aquí)
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

                    // Validar roles si están definidos
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

                    if (result is ActionResult actionResult)
                    {
                        // Extraer sólo la propiedad Value antes de devolver
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
            int maxBodySize = _maxBodySize * 1024 * 1024;

            if (context.Request.ContentLength64 > maxBodySize)
            {
                throw new InvalidOperationException($"Request body too large. Maximum allowed is {maxBodySize} bytes.");
            }

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
                    // Leer el cuerpo completo primero
                    using var reader = new StreamReader(context.Request.InputStream);
                    body = await reader.ReadToEndAsync();

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
                        bool hasFromFormParam = parameters.Any(p => p.GetCustomAttribute<FromFormAttribute>() != null);

                        if (hasFromFormParam)
                        {
                            if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                            {
                                // Para multipart, necesitamos recrear el stream
                                using var bodyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
                                multipartData = await MultipartFormDataParser.ParseAsync(bodyStream, contentType);
                                formData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var field in multipartData.Values)
                                {
                                    formData[field.Name] = field.IsFile ? field.FileName ?? "" : field.Value;
                                }
                            }
                            else if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                            {
                                formData = ParseUrlEncodedData(body);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error reading request body: {ex.Message}");
                    throw new InvalidOperationException($"Error processing request body: {ex.Message}");
                }
            }

            foreach (var param in parameters)
            {
                try
                {
                    var fromBody = param.GetCustomAttribute<FromBodyAttribute>() != null;
                    var fromHeader = param.GetCustomAttribute<FromHeaderAttribute>();
                    var fromQuery = param.GetCustomAttribute<FromQueryAttribute>();
                    var fromForm = param.GetCustomAttribute<FromFormAttribute>();
                    var fromFile = param.GetCustomAttribute<FromFileAttribute>();

                    if (fromFile != null && multipartData != null)
                    {
                        // Manejar parámetros FromFile (archivos subidos)
                        string fileName = fromFile.Name ?? param.Name!;

                        if (multipartData.TryGetValue(fileName, out var fileField) && fileField.IsFile)
                        {
                            if (param.ParameterType == typeof(MultipartFormDataParser.FormField))
                            {
                                resolved.Add(fileField);
                            }
                            else if (param.ParameterType == typeof(byte[]))
                            {
                                resolved.Add(fileField.FileData ?? Array.Empty<byte>());
                            }
                            else if (param.ParameterType == typeof(string))
                            {
                                resolved.Add(fileField.FileName ?? "");
                            }
                            else if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
                            {
                                var instance = Activator.CreateInstance(param.ParameterType);
                                var properties = param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                                foreach (var property in properties)
                                {
                                    if (property.CanWrite)
                                    {
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
                                }
                                resolved.Add(instance);
                            }
                            else
                            {
                                resolved.Add(fileField);
                            }
                        }
                        else
                        {
                            resolved.Add(param.HasDefaultValue ? param.DefaultValue : null);
                        }
                    }
                    else if (fromForm != null && !isJsonRequest)
                    {
                        // Manejar parámetros FromForm (solo si no es JSON)
                        if (formData == null && multipartData == null)
                        {
                            resolved.Add(param.HasDefaultValue ? param.DefaultValue :
                                        param.ParameterType.IsClass && param.ParameterType != typeof(string) ?
                                        Activator.CreateInstance(param.ParameterType) : null);
                            continue;
                        }

                        if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
                        {
                            var instance = Activator.CreateInstance(param.ParameterType);
                            var properties = param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                            foreach (var property in properties)
                            {
                                if (property.CanWrite)
                                {
                                    string formKey = !string.IsNullOrEmpty(fromForm.Name) ?
                                                  $"{fromForm.Name}.{property.Name}" : property.Name;

                                    object? formValue = multipartData?.Values
                                        .FirstOrDefault(f => string.Equals(f.Name, formKey, StringComparison.OrdinalIgnoreCase))?
                                        .Value;

                                    formValue ??= formData?.FirstOrDefault(f =>
                                        string.Equals(f.Key, formKey, StringComparison.OrdinalIgnoreCase)).Value;

                                    if (formValue != null)
                                    {
                                        try
                                        {
                                            Type targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                            object? convertedValue = ConvertValue(formValue.ToString(), targetType);
                                            property.SetValue(instance, convertedValue);
                                        }
                                        catch (Exception ex)
                                        {
                                            AvalonFlowInstance.Log($"Error converting form field '{formKey}': {ex.Message}");
                                        }
                                    }
                                }
                            }
                            resolved.Add(instance);
                        }
                        else
                        {
                            string formKey = fromForm.Name ?? param.Name!;
                            string? formValue = multipartData?.Values
                                .FirstOrDefault(f => string.Equals(f.Name, formKey, StringComparison.OrdinalIgnoreCase))?
                                .Value;

                            formValue ??= formData?.FirstOrDefault(f =>
                                string.Equals(f.Key, formKey, StringComparison.OrdinalIgnoreCase)).Value;

                            if (!string.IsNullOrEmpty(formValue))
                            {
                                try
                                {
                                    resolved.Add(Convert.ChangeType(formValue, param.ParameterType));
                                }
                                catch (Exception ex)
                                {
                                    throw new InvalidOperationException(
                                        $"Cannot convert form field '{formKey}' value '{formValue}' to type '{param.ParameterType.Name}': {ex.Message}");
                                }
                            }
                            else
                            {
                                resolved.Add(param.HasDefaultValue ? param.DefaultValue : null);
                            }
                        }
                    }
                    else if (fromBody)
                    {
                        if (jsonDoc == null)
                        {
                            // Si no hay JSON document pero se esperaba FromBody, verificar si hay body string
                            if (!string.IsNullOrWhiteSpace(body))
                            {
                                AvalonFlowInstance.Log($"FromBody parameter but no valid JSON. Body content: {body}");
                                throw new InvalidOperationException($"FromBody parameter '{param.Name}' requires valid JSON content");
                            }
                            resolved.Add(param.HasDefaultValue ? param.DefaultValue : null);
                            continue;
                        }

                        if (param.ParameterType == typeof(JsonElement))
                        {
                            resolved.Add(jsonDoc.RootElement.Clone());
                        }
                        else if (param.ParameterType == typeof(JsonDocument))
                        {
                            resolved.Add(jsonDoc);
                        }
                        else
                        {
                            try
                            {
                                var deserialized = JsonSerializer.Deserialize(
                                    jsonDoc.RootElement.GetRawText(),
                                    param.ParameterType,
                                    new JsonSerializerOptions
                                    {
                                        PropertyNameCaseInsensitive = true,
                                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                                    }
                                );

                                resolved.Add(deserialized ?? throw new InvalidOperationException(
                                    "Invalid JSON format: could not deserialize the object."));
                            }
                            catch (JsonException ex)
                            {
                                AvalonFlowInstance.Log($"JSON deserialization error for parameter '{param.Name}': {ex.Message}. JSON content: {jsonDoc.RootElement.GetRawText()}");
                                throw new InvalidOperationException(
                                    $"Invalid JSON format for parameter '{param.Name}': {ex.Message}");
                            }
                        }
                    }
                    else if (fromHeader != null)
                    {
                        string headerName = fromHeader.Name ?? param.Name!;
                        string? headerValue = GetHeaderValue(context.Request, headerName);

                        if (string.IsNullOrWhiteSpace(headerValue))
                        {
                            if (param.HasDefaultValue)
                            {
                                resolved.Add(param.DefaultValue);
                            }
                            else
                            {
                                throw new InvalidOperationException($"Missing required header: '{headerName}'");
                            }
                        }
                        else
                        {
                            try
                            {
                                resolved.Add(Convert.ChangeType(headerValue, param.ParameterType));
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException(
                                    $"Cannot convert header '{headerName}' value '{headerValue}' to type '{param.ParameterType.Name}': {ex.Message}");
                            }
                        }
                    }
                    else if (fromQuery != null)
                    {
                        string queryName = fromQuery.Name ?? param.Name!;
                        string? queryValue = context.Request.QueryString[queryName];

                        if (string.IsNullOrWhiteSpace(queryValue))
                        {
                            resolved.Add(param.HasDefaultValue ? param.DefaultValue :
                                        param.ParameterType.IsValueType ?
                                        Activator.CreateInstance(param.ParameterType) : null);
                        }
                        else
                        {
                            try
                            {
                                resolved.Add(Convert.ChangeType(queryValue, param.ParameterType));
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException(
                                    $"Cannot convert query parameter '{queryName}' value '{queryValue}' to type '{param.ParameterType.Name}': {ex.Message}");
                            }
                        }
                    }
                    else if (param.ParameterType == typeof(HttpListenerContext))
                    {
                        resolved.Add(context);
                    }
                    else if (routeParams.TryGetValue(param.Name!.ToLowerInvariant(), out var value))
                    {
                        try
                        {
                            resolved.Add(Convert.ChangeType(value, param.ParameterType));
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                $"Cannot convert route parameter '{param.Name}' value '{value}' to type '{param.ParameterType.Name}': {ex.Message}");
                        }
                    }
                    else
                    {
                        resolved.Add(param.HasDefaultValue ? param.DefaultValue : null);
                    }
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error resolving parameter '{param.Name}': {ex.Message}");
                    throw;
                }
            }

            return resolved.ToArray();
        }

        // Método auxiliar para parsing de datos URL encoded
        private Dictionary<string, string> ParseUrlEncodedData(string body)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(body))
                return result;

            var pairs = body.Split('&');
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=', 2);
                if (keyValue.Length == 2)
                {
                    var key = Uri.UnescapeDataString(keyValue[0].Replace('+', ' '));
                    var value = Uri.UnescapeDataString(keyValue[1].Replace('+', ' '));
                    result[key] = value;
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

        private void AddSecurityHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            response.Headers.Add("X-Frame-Options", "DENY");
            response.Headers.Add("X-XSS-Protection", "1; mode=block");
            response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            response.Headers.Add("Content-Security-Policy", "default-src 'self'");
            response.Headers.Add("Referrer-Policy", "no-referrer");
            response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=()");
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
            AddSecurityHeaders(context.Response);

            var response = context.Response;
            response.StatusCode = statusCode;
          
            AddCorsHeaders(response);

            try
            {
                if (value is FileActionResult file)
                {
                    context.Response.ContentType = file.ContentType ?? "application/octet-stream";
                    context.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{file.FileName}\"");
                    await context.Response.OutputStream.WriteAsync(file.Content, 0, file.Content.Length);
                }
                else if (value is StreamFileActionResult streamfile)
                {
                    context.Response.ContentType = streamfile.ContentType;

                    if (!string.IsNullOrEmpty(streamfile.FileName))
                    {
                        var dispositionType = streamfile.IsAttachment ? "attachment" : "inline";
                        context.Response.AddHeader("Content-Disposition", $"{dispositionType}; filename=\"{streamfile.FileName}\"");
                    }

                    byte[] buffer = new byte[81920]; // 80 KB buffer
                    int bytesRead;
                    while ((bytesRead = await streamfile.ContentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await context.Response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                        await context.Response.OutputStream.FlushAsync();
                    }
                    streamfile.ContentStream.Close();
                }
                else
                {
                    context.Response.ContentType = "application/json";
                    var responseJson = JsonSerializer.Serialize(value);
                    var buffer = Encoding.UTF8.GetBytes(responseJson);
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error writing response: {ex.Message}");
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch
                {
                    // Ignorar errores al cerrar la respuesta
                }
            }
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