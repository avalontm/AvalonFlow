using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
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

        public AvalonRestServer(int port, bool useHttps = false)
        {
            try
            {
                _listener = new HttpListener();
                string scheme = useHttps ? "https" : "http";
                _listener.Prefixes.Add($"{scheme}://localhost:{port}/");

                RegisterControllersInAllAssemblies();
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error initializing server: {ex.Message}");
                throw;
            }
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
                string method = context.Request.HttpMethod.ToUpperInvariant();
                string[] segments = context.Request.Url.AbsolutePath.Trim('/').Split('/');

                if (segments.Length < 2 || segments[0].ToLowerInvariant() != "api")
                {
                    await RespondWith(context, 404, new { error = "Invalid route" });
                    return;
                }

                string controllerKey = $"{segments[0].ToLowerInvariant()}/{segments[1].ToLowerInvariant()}";
                string subPath = "/" + string.Join("/", segments.Skip(2)).ToLowerInvariant();

                if (!_controllers.TryGetValue(controllerKey, out var controllerType))
                {
                    await RespondWith(context, 404, new { error = "Controller not found" });
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

                    string? authHeader = context.Request.Headers["Authorization"];

                    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        await RespondWith(context, 401, new { error = "Unauthorized: Missing or invalid token" });
                        return;
                    }

                    string token = authHeader["Bearer ".Length..].Trim();
                    userPrincipal = ValidateJwtToken(token);
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

                // Ejecutar el método
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
                    await RespondWith(context, actionResult.StatusCode, actionResult.Value);
                }
                else
                {
                    await RespondWith(context, 200, result ?? new { });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
                await RespondWith(context, 500, new { error = "Internal server error" });
            }
        }

        private async Task<object[]> ResolveParameters(MethodInfo method, HttpListenerContext context, Dictionary<string, string> routeParams)
        {
            var parameters = method.GetParameters();
            var resolved = new List<object>();

            string body = null;
            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream);
                body = await reader.ReadToEndAsync();
            }

            foreach (var param in parameters)
            {
                var fromBody = param.GetCustomAttribute<FromBodyAttribute>() != null;

                if (fromBody)
                {
                    if (string.IsNullOrEmpty(body))
                    {
                        resolved.Add(null);
                        continue;
                    }

                    if (param.ParameterType == typeof(JsonElement))
                    {
                        var jsonDoc = JsonDocument.Parse(body);
                        resolved.Add(jsonDoc.RootElement);
                    }
                    else
                    {
                        var deserialized = JsonSerializer.Deserialize(body, param.ParameterType, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        resolved.Add(deserialized);
                    }
                }
                else if (param.ParameterType == typeof(HttpListenerContext))
                {
                    resolved.Add(context);
                }
                else if (routeParams.TryGetValue(param.Name!, out var value))
                {
                    // Convertir tipo primitivo si es necesario
                    resolved.Add(Convert.ChangeType(value, param.ParameterType));
                }
                else
                {
                    resolved.Add(null); // O extender para [FromQuery]
                }
            }

            return resolved.ToArray();
        }

        private async Task RespondWith(HttpListenerContext context, int statusCode, object value)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var responseJson = JsonSerializer.Serialize(value);
            var buffer = Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        }

        private ClaimsPrincipal? ValidateJwtToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(AvalonFlowInstance.JwtSecretKey);

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                return principal;
            }
            catch
            {
                return null;
            }
        }

    }
}
