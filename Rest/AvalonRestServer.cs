using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Web;

namespace AvalonFlow.Rest
{
    public class AvalonRestServer
    {
        private readonly HttpListener _listener;
        private readonly Dictionary<string, Type> _controllers = new();
        private readonly string _corsAllowedOrigins;
        private readonly string _corsAllowedMethods;
        private readonly string _corsAllowedHeaders;
        private int _port;

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
                AvalonFlowInstance.Log($"REST server started | Port: {_port}");

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

        // Método para parsear datos de formulario
        private Dictionary<string, string> ParseFormData(string body, string contentType)
        {
            var formData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (contentType.Contains("application/x-www-form-urlencoded"))
                {
                    // Parsear form URL encoded
                    var pairs = body.Split('&');
                    foreach (var pair in pairs)
                    {
                        var keyValue = pair.Split('=', 2);
                        if (keyValue.Length == 2)
                        {
                            string key = HttpUtility.UrlDecode(keyValue[0]);
                            string value = HttpUtility.UrlDecode(keyValue[1]);
                            formData[key] = value;
                        }
                    }
                }
                else if (contentType.Contains("multipart/form-data"))
                {
                    // Para multipart/form-data necesitarías una implementación más compleja
                    // Por ahora, lanzamos una excepción indicando que no está soportado
                    throw new NotSupportedException("multipart/form-data is not yet supported. Use application/x-www-form-urlencoded for now.");
                }
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error parsing form data: {ex.Message}");
                throw new InvalidOperationException($"Invalid form data format: {ex.Message}");
            }

            return formData;
        }

        // Actualización del método ParseFormData en AvalonRestServer.cs

        private async Task<Dictionary<string, string>> ParseFormDataAsync(Stream inputStream, string contentType)
        {
            var formData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (contentType.Contains("application/x-www-form-urlencoded"))
                {
                    // Parsear form URL encoded
                    using var reader = new StreamReader(inputStream);
                    string body = await reader.ReadToEndAsync();

                    var pairs = body.Split('&');
                    foreach (var pair in pairs)
                    {
                        var keyValue = pair.Split('=', 2);
                        if (keyValue.Length == 2)
                        {
                            string key = HttpUtility.UrlDecode(keyValue[0]);
                            string value = HttpUtility.UrlDecode(keyValue[1]);
                            formData[key] = value;
                        }
                    }
                }
                else if (contentType.Contains("multipart/form-data"))
                {
                    // Usar el MultipartFormDataParser personalizado
                    var multipartFields = await MultipartFormDataParser.ParseAsync(inputStream, contentType);

                    foreach (var field in multipartFields.Values)
                    {
                        if (field.IsFile)
                        {
                            // Para archivos, almacenar información del archivo
                            // Puedes ajustar esto según tus necesidades
                            formData[field.Name] = field.FileName ?? "";

                            // Opcionalmente, también podrías almacenar metadatos del archivo
                            formData[$"{field.Name}_contenttype"] = field.ContentType ?? "";
                            formData[$"{field.Name}_size"] = field.FileData?.Length.ToString() ?? "0";
                        }
                        else
                        {
                            // Para campos de texto normales
                            formData[field.Name] = field.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error parsing form data: {ex.Message}");
                throw new InvalidOperationException($"Invalid form data format: {ex.Message}");
            }

            return formData;
        }

        // Actualización del método ResolveParameters para usar la versión async
        private async Task<object[]> ResolveParameters(MethodInfo method, HttpListenerContext context, Dictionary<string, string> routeParams)
        {
            var parameters = method.GetParameters();
            var resolved = new List<object>();

            string? body = null;
            Dictionary<string, string>? formData = null;
            Dictionary<string, MultipartFormDataParser.FormField>? multipartData = null;

            if (context.Request.HasEntityBody)
            {
                try
                {
                    // Si hay algún parámetro FromForm, parsear los datos del formulario
                    bool hasFromFormParam = parameters.Any(p => p.GetCustomAttribute<FromFormAttribute>() != null);

                    if (hasFromFormParam)
                    {
                        string contentType = context.Request.ContentType ?? "";

                        if (contentType.Contains("multipart/form-data"))
                        {
                            // Usar MultipartFormDataParser para multipart/form-data
                            multipartData = await MultipartFormDataParser.ParseAsync(context.Request.InputStream, contentType);

                            // También crear un diccionario simplificado para compatibilidad
                            formData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var field in multipartData.Values)
                            {
                                if (field.IsFile)
                                {
                                    formData[field.Name] = field.FileName ?? "";
                                    formData[$"{field.Name}_contenttype"] = field.ContentType ?? "";
                                    formData[$"{field.Name}_size"] = field.FileData?.Length.ToString() ?? "0";
                                }
                                else
                                {
                                    formData[field.Name] = field.Value;
                                }
                            }
                        }
                        else
                        {
                            // Para application/x-www-form-urlencoded
                            formData = await ParseFormDataAsync(context.Request.InputStream, contentType);
                        }
                    }
                    else
                    {
                        // Para parámetros FromBody, leer como string
                        using var reader = new StreamReader(context.Request.InputStream);
                        body = await reader.ReadToEndAsync();
                    }
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
                    var fromForm = param.GetCustomAttribute<FromFormAttribute>();
                    var fromFile = param.GetCustomAttribute<FromFileAttribute>(); // Nuevo atributo para archivos

                    if (fromFile != null && multipartData != null)
                    {
                        // Manejar parámetros FromFile (para archivos subidos)
                        string fileName = fromFile.Name ?? param.Name!;

                        if (multipartData.TryGetValue(fileName, out var fileField) && fileField.IsFile)
                        {
                            if (param.ParameterType == typeof(MultipartFormDataParser.FormField))
                            {
                                resolved.Add(fileField);
                            }
                            else if (param.ParameterType == typeof(byte[]))
                            {
                                resolved.Add(fileField.FileData ?? new byte[0]);
                            }
                            else if (param.ParameterType == typeof(string))
                            {
                                resolved.Add(fileField.FileName ?? "");
                            }
                            else
                            {
                                // Intentar crear un objeto personalizado para el archivo
                                if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
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
                        }
                        else
                        {
                            if (param.HasDefaultValue)
                            {
                                resolved.Add(param.DefaultValue);
                            }
                            else
                            {
                                resolved.Add(null);
                            }
                        }
                    }
                    else if (fromForm != null)
                    {
                        // Manejar parámetros FromForm (versión mejorada)
                        if (formData == null && multipartData == null)
                        {
                            if (param.HasDefaultValue)
                            {
                                resolved.Add(param.DefaultValue);
                            }
                            else
                            {
                                if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
                                {
                                    resolved.Add(Activator.CreateInstance(param.ParameterType));
                                }
                                else
                                {
                                    resolved.Add(null);
                                }
                            }
                            continue;
                        }

                        if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
                        {
                            // Crear una instancia del objeto y mapear las propiedades
                            var instance = Activator.CreateInstance(param.ParameterType);
                            var properties = param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                            foreach (var property in properties)
                            {
                                if (property.CanWrite)
                                {
                                    string formKey = property.Name;

                                    if (!string.IsNullOrEmpty(fromForm.Name))
                                    {
                                        formKey = $"{fromForm.Name}.{property.Name}";
                                    }

                                    object formValue = null;

                                    // Buscar primero en multipartData si está disponible
                                    if (multipartData != null)
                                    {
                                        foreach (var kvp in multipartData)
                                        {
                                            if (string.Equals(kvp.Key, formKey, StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(kvp.Key, property.Name, StringComparison.OrdinalIgnoreCase))
                                            {
                                                formValue = kvp.Value.IsFile ? kvp.Value.FileName : kvp.Value.Value;
                                                break;
                                            }
                                        }
                                    }

                                    // Si no se encontró en multipart, buscar en formData
                                    if (formValue == null && formData != null)
                                    {
                                        foreach (var kvp in formData)
                                        {
                                            if (string.Equals(kvp.Key, formKey, StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(kvp.Key, property.Name, StringComparison.OrdinalIgnoreCase))
                                            {
                                                formValue = kvp.Value;
                                                break;
                                            }
                                        }
                                    }

                                    if (formValue != null)
                                    {
                                        try
                                        {
                                            Type targetType = property.PropertyType;
         
                                            // Handle nullable types
                                            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                                            {
                                                targetType = Nullable.GetUnderlyingType(targetType);
                                            }

                                            object convertedValue = formValue; // Default to original value

                                            // Only attempt JSON conversion if the value looks like JSON
                                            if (formValue.ToString().Trim().StartsWith("{") || formValue.ToString().Trim().StartsWith("["))
                                            {
                                                try
                                                {
                                                    convertedValue = JsonSerializer.Deserialize(formValue.ToString(), targetType) ?? formValue;
                                                }
                                                catch (JsonException)
                                                {
                                                    // If JSON deserialization fails, keep original value
                                                    convertedValue = formValue;
                                                }
                                            }
                                            else
                                            {
                                                // Handle simple type conversions
                                                if (targetType == typeof(Guid))
                                                {
                                                    if (Guid.TryParse(formValue.ToString(), out var guidValue))
                                                        convertedValue = guidValue;
                                                }
                                                else if (targetType.IsEnum)
                                                {
                                                    if (Enum.TryParse(targetType, formValue.ToString(), true, out var enumValue))
                                                        convertedValue = enumValue;
                                                }
                                                else if (targetType == typeof(bool))
                                                {
                                                    if (bool.TryParse(formValue.ToString(), out var boolValue))
                                                        convertedValue = boolValue;
                                                }
                                                else
                                                {
                                                    try
                                                    {
                                                        convertedValue = Convert.ChangeType(formValue, targetType);
                                                    }
                                                    catch
                                                    {
                                                        // Conversion failed, keep original value
                                                    }
                                                }
                                            }

                                            property.SetValue(instance, convertedValue);
                                        }
                                        catch (Exception ex)
                                        {
                                            AvalonFlowInstance.Log($"Error converting form field '{formKey}' to property '{property.Name}': {ex.Message}");
                                        }
                                    }
                                }
                            }

                            resolved.Add(instance);
                        }
                        else
                        {
                            // Tipo simple (string, int, etc.)
                            string formKey = fromForm.Name ?? param.Name!;
                            string? formValue = null;

                            // Buscar en multipartData primero
                            if (multipartData != null && multipartData.TryGetValue(formKey, out var field))
                            {
                                formValue = field.IsFile ? field.FileName : field.Value;
                            }
                            // Luego en formData
                            else if (formData != null && formData.TryGetValue(formKey, out var value))
                            {
                                formValue = value;
                            }

                            if (!string.IsNullOrEmpty(formValue))
                            {
                                try
                                {
                                    var converted = Convert.ChangeType(formValue, param.ParameterType);
                                    resolved.Add(converted);
                                }
                                catch (Exception ex)
                                {
                                    throw new InvalidOperationException($"Cannot convert form field '{formKey}' value '{formValue}' to type '{param.ParameterType.Name}': {ex.Message}");
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
                                    resolved.Add(null);
                                }
                            }
                        }
                    }
                    // ... resto del código para fromBody, fromHeader, fromQuery permanece igual
                    else if (fromBody)
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
                            resolved.Add(null);
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