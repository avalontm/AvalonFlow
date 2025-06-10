using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AvalonFlow.Rest
{
    public class AvalonRestServer
    {
        private readonly HttpListener _listener;
        private readonly Dictionary<string, Type> _controllers = new();
        private readonly string _corsAllowedOrigins;
        private readonly string _corsAllowedMethods;
        private readonly string _corsAllowedHeaders;

        public AvalonRestServer(int port = 5000, bool useHttps = false, string corsAllowedOrigins = "*", string corsAllowedMethods = "GET, POST, PUT, DELETE, OPTIONS", string corsAllowedHeaders = "Content-Type, Authorization")
        {
            _corsAllowedOrigins = corsAllowedOrigins;
            _corsAllowedMethods = corsAllowedMethods;
            _corsAllowedHeaders = corsAllowedHeaders;

            try
            {
                _listener = new HttpListener();
                string scheme = useHttps ? "https" : "http";
                _listener.Prefixes.Add($"{scheme}://+:{port}/");

                RegisterControllersInAllAssemblies();
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error initializing server: {ex.Message}");
                throw;
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
                Console.WriteLine("REST server started.");

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
            try
            {
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

            string? body = null;
            if (context.Request.HasEntityBody)
            {
                try
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    body = await reader.ReadToEndAsync();
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error reading request body: {ex.Message}");
                }
            }

            foreach (var param in parameters)
            {
                try
                {
                    var fromBody = param.GetCustomAttribute<FromBodyAttribute>() != null;
                    var fromHeader = param.GetCustomAttribute<FromHeaderAttribute>();
                    var fromQuery = param.GetCustomAttribute<FromQueryAttribute>();

                    if (fromBody)
                    {
                        if (string.IsNullOrEmpty(body))
                        {
                            if (param.HasDefaultValue)
                            {
                                resolved.Add(param.DefaultValue);
                            }
                            else
                            {
                                resolved.Add(null);
                            }
                            continue;
                        }

                        if (param.ParameterType == typeof(JsonElement))
                        {
                            var jsonDoc = JsonDocument.Parse(body);
                            var root = jsonDoc.RootElement;

                            if (root.ValueKind != JsonValueKind.Object)
                                throw new InvalidOperationException("Invalid JSON format: expected a JSON object.");

                            resolved.Add(root);
                        }
                        else
                        {
                            var deserialized = JsonSerializer.Deserialize(body, param.ParameterType, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (deserialized == null)
                                throw new InvalidOperationException("Invalid JSON format: could not deserialize the object.");

                            resolved.Add(deserialized);
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
                                AvalonFlowInstance.Log($"ERROR: Header '{headerName}' is required but not found");
                                throw new InvalidOperationException($"Missing required header: '{headerName}'");
                            }
                        }
                        else
                        {
                            try
                            {
                                var converted = Convert.ChangeType(headerValue, param.ParameterType);
                                resolved.Add(converted);
                            }
                            catch (Exception ex)
                            {
                                AvalonFlowInstance.Log($"ERROR: Cannot convert header '{headerName}' value '{headerValue}' to type '{param.ParameterType.Name}': {ex.Message}");
                                throw new InvalidOperationException($"Cannot convert header '{headerName}' value '{headerValue}' to type '{param.ParameterType.Name}': {ex.Message}");
                            }
                        }
                    }
                    else if (fromQuery != null)
                    {
                        string queryName = fromQuery.Name ?? param.Name!;
                        string? queryValue = context.Request.QueryString[queryName];

                        if (string.IsNullOrWhiteSpace(queryValue))
                        {
                            if (param.HasDefaultValue)
                            {
                                resolved.Add(param.DefaultValue);
                            }
                            else if (param.ParameterType.IsValueType)
                            {
                                // Para tipos value types sin valor por defecto, agregar valor por defecto de tipo
                                resolved.Add(Activator.CreateInstance(param.ParameterType));
                            }
                            else
                            {
                                resolved.Add(null);
                            }
                        }
                        else
                        {
                            try
                            {
                                var converted = Convert.ChangeType(queryValue, param.ParameterType);
                                resolved.Add(converted);
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException($"Cannot convert query parameter '{queryName}' value '{queryValue}' to type '{param.ParameterType.Name}': {ex.Message}");
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
                            var converted = Convert.ChangeType(value, param.ParameterType);
                            resolved.Add(converted);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Cannot convert route parameter '{param.Name}' value '{value}' to type '{param.ParameterType.Name}': {ex.Message}");
                        }
                    }
                    else
                    {
                        if (param.HasDefaultValue)
                        {
                            resolved.Add(param.DefaultValue);
                        }
                        else
                        {
                            resolved.Add(null); // valor por defecto si no hay coincidencia
                        }
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
    }
}