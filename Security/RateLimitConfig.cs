using System;
using System.Collections.Generic;
using System.Linq;

namespace AvalonFlow.Security
{
    public class RateLimitConfig
    {
        public int DefaultMaxRequests { get; set; } = 500;
        public TimeSpan DefaultTimeWindow { get; set; } = TimeSpan.FromMinutes(1);

        public int BlockDurationMinutes { get; set; } = 30; // Aumentado de 15 a 30 minutos
        public int MaxViolationsBeforeBlock { get; set; } = 10; // Aumentado de 3 a 10 violaciones

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
                // ENDPOINTS DE AUTENTICACIÓN (Más permisivos para uso normal)
                ["/api/auth/store"] = new EndpointLimit
                {
                    MaxRequests = 100, // Aumentado de 50 a 100
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Login tienda - Previene fuerza bruta"
                },
                ["/api/auth/user"] = new EndpointLimit
                {
                    MaxRequests = 100, // Aumentado de 50 a 100
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Login usuario - Previene fuerza bruta"
                },
                ["/api/auth/register-user"] = new EndpointLimit
                {
                    MaxRequests = 30, // Aumentado de 10 a 30
                    TimeWindow = TimeSpan.FromMinutes(5),
                    Description = "Registro usuario - Previene spam"
                },
                ["/api/auth/register-store"] = new EndpointLimit
                {
                    MaxRequests = 30, // Aumentado de 10 a 30
                    TimeWindow = TimeSpan.FromMinutes(5),
                    Description = "Registro tienda - Previene spam"
                },
                ["/api/auth/forgot-password"] = new EndpointLimit
                {
                    MaxRequests = 30, // Aumentado de 20 a 30
                    TimeWindow = TimeSpan.FromMinutes(10),
                    Description = "Recuperar contraseña - Previene abuso"
                },
                ["/api/auth/reset-password"] = new EndpointLimit
                {
                    MaxRequests = 30, // Aumentado de 20 a 30
                    TimeWindow = TimeSpan.FromMinutes(10),
                    Description = "Reset contraseña"
                },

                // ENDPOINTS DE ESCRITURA/MODIFICACIÓN
                ["/api/upload"] = new EndpointLimit
                {
                    MaxRequests = 100, // Aumentado de 50 a 100
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Subida de archivos"
                },
                ["/api/order"] = new EndpointLimit
                {
                    MaxRequests = 200, // Aumentado de 100 a 200
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Creación de órdenes"
                },
                ["/api/payment"] = new EndpointLimit
                {
                    MaxRequests = 200, // Aumentado de 100 a 200
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Procesamiento de pagos"
                },

                // ENDPOINTS DE LECTURA PÚBLICA (Permisivos - Escala Media)
                ["/api/store"] = new EndpointLimit
                {
                    MaxRequests = 1500,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Consulta de tiendas - Alto tráfico esperado"
                },
                ["/api/product"] = new EndpointLimit
                {
                    MaxRequests = 2000,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Consulta de productos - Alto tráfico esperado"
                },
                ["/api/category"] = new EndpointLimit
                {
                    MaxRequests = 1000,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Consulta de categorías"
                },

                // ENDPOINTS DE BÚSQUEDA
                ["/api/search"] = new EndpointLimit
                {
                    MaxRequests = 300, // Aumentado de 200 a 300
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Búsquedas - Puede ser costoso en BD"
                },

                // API GENERAL (Límite base)
                ["/api/"] = new EndpointLimit
                {
                    MaxRequests = 500,
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