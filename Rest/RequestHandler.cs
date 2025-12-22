using AvalonFlow.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class RequestHandler
    {
        private readonly EnhancedRateLimiter _rateLimiter;
        private readonly ControllerRegistry _controllerRegistry;
        private readonly ParameterResolver _parameterResolver;
        private readonly int _maxBodySize;

        public RequestHandler(EnhancedRateLimiter rateLimiter, ControllerRegistry controllerRegistry, int maxBodySize)
        {
            _rateLimiter = rateLimiter;
            _controllerRegistry = controllerRegistry;
            _maxBodySize = maxBodySize;
            _parameterResolver = new ParameterResolver(maxBodySize);
        }

        public async Task HandleAsync(HttpListenerContext context, ResponseHandler responseHandler)
        {
            // ✅ CORRECCIÓN: Obtener IP real considerando proxies
            var clientIp = GetRealClientIP(context.Request);
            var endpoint = context.Request.Url?.AbsolutePath ?? "/";
            var userAgent = context.Request.Headers["User-Agent"];
            var authHeader = context.Request.Headers["Authorization"];
            string token = null;

            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader.Substring("Bearer ".Length).Trim();
            }

            // Rate Limiting
            if (!_rateLimiter.IsAllowed(clientIp, endpoint, userAgent, token))
            {
                var status = _rateLimiter.GetStatus(clientIp, endpoint, userAgent, token);

                await responseHandler.RespondWithAsync(context, 429, new
                {
                    error = "Too many requests",
                    message = status.Message,
                    retryAfter = status.BlockedUntil?.ToString("yyyy-MM-dd HH:mm:ss"),
                    limit = status.MaxRequests,
                    window = $"{status.TimeWindow.TotalMinutes} minutes"
                });
                return;
            }

            var request = context.Request;
            var response = context.Response;

            // Validar tamaño del body
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
                        suggestion = "Split your request into smaller chunks or contact support"
                    };

                    await responseHandler.RespondWithAsync(context, 413, errorResponse);
                    return;
                }
            }

            // Manejar OPTIONS (CORS preflight)
            if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                responseHandler.AddCorsHeaders(response);
                response.StatusCode = 204;
                response.Close();
                return;
            }

            string method = request.HttpMethod.ToUpperInvariant();
            string requestPath = request.Url.AbsolutePath.Trim('/').ToLowerInvariant();

            // Buscar controlador
            var controllerMatch = _controllerRegistry.FindController(requestPath);

            if (!controllerMatch.HasValue)
            {
                await responseHandler.RespondWithAsync(context, 404, new
                {
                    error = "Controller not found",
                    requestedPath = requestPath,
                    availableRoutes = _controllerRegistry.GetRegisteredRoutes().ToArray()
                });
                return;
            }

            var (controllerType, subPath) = controllerMatch.Value;

            // Crear instancia del controlador
            var controllerInstance = Activator.CreateInstance(controllerType);

            if (controllerInstance is AvalonControllerBase baseController)
            {
                baseController.HttpContext = new AvalonHttpContext(context);
            }

            // Buscar método del controlador
            var methods = controllerType.GetMethods()
                .Where(m => m.GetCustomAttribute<AvalonRestAttribute>() is not null)
                .ToList();

            var (matchedMethod, routeParams) = FindMatchingMethod(methods, method, subPath);

            if (matchedMethod == null)
            {
                await responseHandler.RespondWithAsync(context, 404, new
                {
                    error = "Route not found",
                    subPath = subPath,
                    method = method
                });
                return;
            }

            // Autorización
            var authResult = await AuthorizeRequest(matchedMethod, controllerType, controllerInstance, context);
            if (!authResult.IsAuthorized)
            {
                await responseHandler.RespondWithAsync(context, authResult.StatusCode, new { error = authResult.ErrorMessage });
                return;
            }

            // Ejecutar método
            try
            {
                object[] parameters = await _parameterResolver.ResolveParametersAsync(matchedMethod, context, routeParams);

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
                    await responseHandler.RespondWithAsync(context, contentResult.StatusCode, contentResult);
                    return;
                }

                if (result is ActionResult actionResult)
                {
                    var valueToReturn = actionResult.GetType().GetProperty("Value")?.GetValue(actionResult) ?? new { };
                    await responseHandler.RespondWithAsync(context, actionResult.StatusCode, valueToReturn);
                }
                else
                {
                    await responseHandler.RespondWithAsync(context, 200, result ?? new { });
                }
            }
            catch (InvalidOperationException invEx)
            {
                await responseHandler.RespondWithAsync(context, 400, new { error = invEx.Message });
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error executing method: {ex}");
                await responseHandler.RespondWithAsync(context, 500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// ✅ NUEVO MÉTODO: Obtiene la IP real del cliente considerando proxies
        /// </summary>
        private string GetRealClientIP(HttpListenerRequest request)
        {
            // Headers de proxy en orden de prioridad
            string[] proxyHeaders = new[]
            {
                "CF-Connecting-IP",      // Cloudflare
                "True-Client-IP",        // Cloudflare Enterprise
                "X-Real-IP",             // nginx, Apache
                "X-Forwarded-For",       // Estándar (puede contener múltiples IPs)
                "X-Client-IP",           // Otros proxies
                "Forwarded"              // RFC 7239
            };

            // Buscar IP en headers de proxy
            foreach (var headerName in proxyHeaders)
            {
                var headerValue = request.Headers[headerName];

                if (!string.IsNullOrWhiteSpace(headerValue))
                {
                    // X-Forwarded-For puede contener: "client_ip, proxy1_ip, proxy2_ip"
                    // Tomamos la primera IP (la del cliente real)
                    var ip = headerValue.Split(',')[0].Trim();

                    // Validar que sea una IP válida
                    if (IPAddress.TryParse(ip, out _))
                    {
                        // Log para debugging (puedes comentar después)
                        AvalonFlowInstance.Log($"[IP Detection] IP obtenida de header '{headerName}': {ip}");
                        return ip;
                    }
                }
            }

            // Si no hay headers de proxy, usar RemoteEndPoint
            var remoteIP = request.RemoteEndPoint?.Address?.ToString() ?? "unknown";

            // Si es localhost, loggear para debugging
            if (remoteIP == "127.0.0.1" || remoteIP == "::1")
            {
                AvalonFlowInstance.Log($"[IP Detection] ⚠️ Detectado localhost - El proxy podría no estar enviando headers correctos");
                AvalonFlowInstance.Log($"[IP Detection] Headers disponibles: {string.Join(", ", request.Headers.AllKeys ?? new string[0])}");
            }

            return remoteIP;
        }

        private (MethodInfo method, Dictionary<string, string> routeParams) FindMatchingMethod(
            List<MethodInfo> methods, string httpMethod, string subPath)
        {
            Dictionary<string, string> routeParams = new();

            foreach (var methodInfo in methods)
            {
                var attr = methodInfo.GetCustomAttribute<AvalonRestAttribute>();
                if (!attr.Method.ToString().Equals(httpMethod, StringComparison.OrdinalIgnoreCase))
                    continue;

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

                if (templateParts.Length != requestParts.Length)
                    continue;

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
                    routeParams = tempParams;
                    return (methodInfo, routeParams);
                }
            }

            return (null, routeParams);
        }

        private async Task<AuthorizationResult> AuthorizeRequest(
            MethodInfo method,
            Type controllerType,
            object controllerInstance,
            HttpListenerContext context)
        {
            var allowAnonymous = method.GetCustomAttribute<AllowAnonymousAttribute>() != null;
            var authorizeAttr = method.GetCustomAttribute<AuthorizeAttribute>() ??
                                controllerType.GetCustomAttribute<AuthorizeAttribute>();

            if (allowAnonymous || authorizeAttr == null)
            {
                return new AuthorizationResult { IsAuthorized = true };
            }

            string scheme = authorizeAttr.AuthenticationScheme?.Split(',').FirstOrDefault()?.Trim() ?? "Bearer";

            if (!scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return new AuthorizationResult
                {
                    IsAuthorized = false,
                    StatusCode = 401,
                    ErrorMessage = "Unauthorized: Unsupported authentication scheme"
                };
            }

            string authHeader = GetHeaderValue(context.Request, "Authorization");

            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return new AuthorizationResult
                {
                    IsAuthorized = false,
                    StatusCode = 401,
                    ErrorMessage = "Unauthorized: Missing or invalid token"
                };
            }

            string token = authHeader["Bearer ".Length..].Trim();
            var userPrincipal = AvalonFlowInstance.ValidateJwtToken(token);

            if (userPrincipal == null)
            {
                return new AuthorizationResult
                {
                    IsAuthorized = false,
                    StatusCode = 401,
                    ErrorMessage = "Unauthorized: Invalid token"
                };
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
                    return new AuthorizationResult
                    {
                        IsAuthorized = false,
                        StatusCode = 403,
                        ErrorMessage = "Forbidden: Insufficient role"
                    };
                }
            }

            return new AuthorizationResult { IsAuthorized = true };
        }

        private string GetHeaderValue(HttpListenerRequest request, string headerName)
        {
            try
            {
                var headerValue = request.Headers[headerName];
                if (!string.IsNullOrEmpty(headerValue))
                {
                    return headerValue;
                }

                if (request.Headers.AllKeys != null)
                {
                    foreach (string key in request.Headers.AllKeys)
                    {
                        if (string.Equals(key, headerName, StringComparison.OrdinalIgnoreCase))
                        {
                            return request.Headers[key];
                        }
                    }
                }

                for (int i = 0; i < request.Headers.Count; i++)
                {
                    string key = request.Headers.GetKey(i);
                    if (string.Equals(key, headerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return request.Headers.Get(i);
                    }
                }

                var headerVariations = new[]
                {
                    headerName.ToLowerInvariant(),
                    headerName.ToUpperInvariant(),
                    headerName.Replace("_", "-"),
                    headerName.Replace("-", "_")
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

        private class AuthorizationResult
        {
            public bool IsAuthorized { get; set; }
            public int StatusCode { get; set; }
            public string ErrorMessage { get; set; }
        }
    }
}