using System;
using System.Collections.Generic;

namespace AvalonFlow.Security
{
    public class RateLimitConfig
    {
        public int DefaultMaxRequests { get; set; } = 100;
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
                // Endpoints de autenticación (más restrictivos)
                ["/api/auth/store"] = new EndpointLimit
                {
                    MaxRequests = 5,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Login tienda"
                },
                ["/api/auth/user"] = new EndpointLimit
                {
                    MaxRequests = 5,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Login usuario"
                },
                ["/api/auth/register-user"] = new EndpointLimit
                {
                    MaxRequests = 3,
                    TimeWindow = TimeSpan.FromMinutes(5),
                    Description = "Registro usuario"
                },
                ["/api/auth/register-store"] = new EndpointLimit
                {
                    MaxRequests = 3,
                    TimeWindow = TimeSpan.FromMinutes(5),
                    Description = "Registro tienda"
                },
                ["/api/auth/forgot-password"] = new EndpointLimit
                {
                    MaxRequests = 3,
                    TimeWindow = TimeSpan.FromMinutes(10),
                    Description = "Recuperar contraseña"
                },
                ["/api/auth/reset-password"] = new EndpointLimit
                {
                    MaxRequests = 5,
                    TimeWindow = TimeSpan.FromMinutes(10),
                    Description = "Reset contraseña"
                },

                // Endpoints de archivos (restrictivos por tamaño)
                ["/api/upload"] = new EndpointLimit
                {
                    MaxRequests = 10,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "Subida de archivos"
                },

                // APIs generales (límite normal)
                ["/api/"] = new EndpointLimit
                {
                    MaxRequests = 100,
                    TimeWindow = TimeSpan.FromMinutes(1),
                    Description = "API general"
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