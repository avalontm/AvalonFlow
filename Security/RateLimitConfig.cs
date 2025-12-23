using System;
using System.Collections.Generic;
using System.Linq;

namespace AvalonFlow.Security
{
    public class RateLimitConfig
    {
        // CONFIGURACIÓN PARA PRODUCCIÓN - Escala Media
        public int DefaultMaxRequests { get; set; } = 500;
        public TimeSpan DefaultTimeWindow { get; set; } = TimeSpan.FromMinutes(1);
        public int BlockDurationMinutes { get; set; } = 15;
        public int MaxViolationsBeforeBlock { get; set; } = 3;

        public Dictionary<string, EndpointLimit> EndpointLimits { get; set; } = new();
        public HashSet<string> WhitelistedIPs { get; set; } = new();
        public HashSet<string> BlacklistedIPs { get; set; } = new();

        public RateLimitConfig()
        {
            ConfigureDefaultLimits();
        }

        private void ConfigureDefaultLimits()
        {
            EndpointLimits = new Dictionary<string, EndpointLimit>(StringComparer.OrdinalIgnoreCase)
            {
                ["/api/auth/store"] = new EndpointLimit
                {
                    MaxRequests = 50,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Login tienda - Previene fuerza bruta"
                },
                ["/api/auth/user"] = new EndpointLimit
                {
                    MaxRequests = 50,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Login usuario - Previene fuerza bruta"
                },
                ["/api/auth/register-user"] = new EndpointLimit
                {
                    MaxRequests = 10,
                    TimeWindow = TimeSpan.FromMinutes(5),
                    Description = "Registro usuario - Previene spam"
                },
                ["/api/auth/register-store"] = new EndpointLimit
                {
                    MaxRequests = 10,
                    TimeWindow = TimeSpan.FromMinutes(5),
                    Description = "Registro tienda - Previene spam"
                },
                ["/api/auth/forgot-password"] = new EndpointLimit
                {
                    MaxRequests = 20,
                    TimeWindow = TimeSpan.FromMinutes(10),
                    Description = "Recuperar contraseña - Previene abuso"
                },
                ["/api/auth/reset-password"] = new EndpointLimit
                {
                    MaxRequests = 20,
                    TimeWindow = TimeSpan.FromMinutes(10),
                    Description = "Reset contraseña"
                },

                // ENDPOINTS DE ESCRITURA/MODIFICACIÓN (Moderadamente restrictivos)
                ["/api/upload"] = new EndpointLimit
                {
                    MaxRequests = 50,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Subida de archivos"
                },
                ["/api/order"] = new EndpointLimit
                {
                    MaxRequests = 100,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Creación de órdenes"
                },
                ["/api/payment"] = new EndpointLimit
                {
                    MaxRequests = 100,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Procesamiento de pagos"
                },

                // ENDPOINTS DE LECTURA PÚBLICA (Permisivos - Escala Media)
                ["/api/store"] = new EndpointLimit
                {
                    MaxRequests = 1500, // 25 peticiones por segundo
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Consulta de tiendas - Alto tráfico esperado"
                },
                ["/api/product"] = new EndpointLimit
                {
                    MaxRequests = 2000, // 33 peticiones por segundo
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Consulta de productos - Alto tráfico esperado"
                },
                ["/api/category"] = new EndpointLimit
                {
                    MaxRequests = 1000,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Consulta de categorías"
                },

                // ENDPOINTS DE BÚSQUEDA (Moderados)
                ["/api/search"] = new EndpointLimit
                {
                    MaxRequests = 200,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Búsquedas - Puede ser costoso en BD"
                },

                // API GENERAL (Límite base)
                ["/api/"] = new EndpointLimit
                {
                    MaxRequests = 500, // 8.3 peticiones por segundo
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "API general - Catch-all para endpoints no especificados"
                }
            };
        }

        public EndpointLimit GetLimitForPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return GetDefaultLimit();

            path = path.ToLowerInvariant().TrimEnd('/');

            // Buscar coincidencia exacta
            if (EndpointLimits.TryGetValue(path, out var exactLimit))
                return exactLimit;

            // Buscar coincidencia parcial (más específica primero)
            var sortedKeys = EndpointLimits.Keys
                .OrderByDescending(k => k.Length)
                .ToList();

            foreach (var key in sortedKeys)
            {
                if (path.StartsWith(key.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    return EndpointLimits[key];
                }
            }

            return GetDefaultLimit();
        }

        private EndpointLimit GetDefaultLimit()
        {
            return new EndpointLimit
            {
                MaxRequests = DefaultMaxRequests,
                TimeWindow = DefaultTimeWindow,
                Description = "Límite por defecto"
            };
        }

        public void AddWhitelistedIP(string ip)
        {
            if (!string.IsNullOrWhiteSpace(ip))
                WhitelistedIPs.Add(ip.Trim());
        }

        public void AddBlacklistedIP(string ip)
        {
            if (!string.IsNullOrWhiteSpace(ip))
                BlacklistedIPs.Add(ip.Trim());
        }

        public bool IsWhitelisted(string ip)
        {
            return WhitelistedIPs.Contains(ip);
        }

        public bool IsBlacklisted(string ip)
        {
            return BlacklistedIPs.Contains(ip);
        }
    }

    public class EndpointLimit
    {
        public int MaxRequests { get; set; }
        public TimeSpan TimeWindow { get; set; }
        public string Description { get; set; }
    }
}