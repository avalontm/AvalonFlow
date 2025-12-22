using AvalonFlow.Security;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AvalonFlow.Security
{
    public class EnhancedRateLimiter
    {
        private readonly ConcurrentDictionary<string, ClientRequestInfo> _clients;
        private readonly RateLimitConfig _config;
        private readonly IPBlockList _blockList;

        public EnhancedRateLimiter(RateLimitConfig config)
        {
            _config = config ?? new RateLimitConfig();
            _clients = new ConcurrentDictionary<string, ClientRequestInfo>();
            _blockList = new IPBlockList();

            StartCleanupTask();

            Console.WriteLine("[EnhancedRateLimiter] Inicializado con configuración:");
            Console.WriteLine($"  - Límite por defecto: {_config.DefaultMaxRequests} peticiones/{_config.DefaultTimeWindow.TotalMinutes} min");
            Console.WriteLine($"  - Endpoints configurados: {_config.EndpointLimits.Count}");
            Console.WriteLine($"  - IPs en whitelist: {_config.WhitelistedIPs.Count}");
            Console.WriteLine($"  - IPs en blacklist: {_config.BlacklistedIPs.Count}");
        }

        public bool IsAllowed(string ip, string endpoint, string userAgent = null, string token = null)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                SecurityLogger.LogSuspiciousActivity("Request sin IP", "unknown", endpoint);
                return false;
            }

            // 1. Verificar whitelist
            if (_config.IsWhitelisted(ip))
            {
                return true;
            }

            // 2. Verificar blacklist permanente
            if (_config.IsBlacklisted(ip))
            {
                SecurityLogger.LogSuspiciousActivity("IP en blacklist intentó acceder", ip, endpoint);
                return false;
            }

            // 3. Verificar bloqueo temporal
            if (_blockList.IsBlocked(ip))
            {
                var blockInfo = _blockList.GetBlockInfo(ip);
                return false;
            }

            // 4. Generar identificador único
            string identifier = GenerateIdentifier(ip, userAgent, token);

            // 5. Obtener límite para el endpoint
            var limit = _config.GetLimitForPath(endpoint);

            // 6. Verificar límite
            var clientInfo = _clients.GetOrAdd(identifier, new ClientRequestInfo
            {
                IP = ip,
                Endpoint = endpoint
            });

            lock (clientInfo.Lock)
            {
                var now = DateTime.UtcNow;

                // Limpiar peticiones antiguas
                clientInfo.Requests.RemoveAll(time => (now - time) > limit.TimeWindow);

                // Verificar límite
                if (clientInfo.Requests.Count >= limit.MaxRequests)
                {
                    // Registrar violación
                    clientInfo.ViolationCount++;

                    SecurityLogger.LogRateLimitViolation(
                        identifier,
                        endpoint,
                        ip,
                        clientInfo.Requests.Count,
                        limit.MaxRequests
                    );

                    // Verificar si se debe bloquear
                    if (clientInfo.ViolationCount >= _config.MaxViolationsBeforeBlock)
                    {
                        var blockDuration = CalculateBlockDuration(clientInfo.ViolationCount);
                        _blockList.BlockIP(
                            ip,
                            blockDuration,
                            $"Exceso de peticiones en {endpoint}",
                            clientInfo.ViolationCount
                        );

                        // Limpiar información del cliente después de bloquear
                        _clients.TryRemove(identifier, out _);
                    }

                    return false;
                }

                // Agregar petición actual
                clientInfo.Requests.Add(now);
                clientInfo.LastRequestTime = now;

                return true;
            }
        }

        private string GenerateIdentifier(string ip, string userAgent, string token)
        {
            var parts = new List<string> { ip };

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                parts.Add(userAgent.GetHashCode().ToString());
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                parts.Add(token.Substring(0, Math.Min(10, token.Length)));
            }

            return string.Join("_", parts);
        }

        private TimeSpan CalculateBlockDuration(int violationCount)
        {
            return violationCount switch
            {
                <= 3 => TimeSpan.FromMinutes(_config.BlockDurationMinutes),
                <= 5 => TimeSpan.FromMinutes(_config.BlockDurationMinutes * 2),
                <= 10 => TimeSpan.FromHours(1),
                <= 20 => TimeSpan.FromHours(6),
                _ => TimeSpan.FromDays(1)
            };
        }

        public RateLimitStatus GetStatus(string ip, string endpoint, string userAgent = null, string token = null)
        {
            if (_config.IsWhitelisted(ip))
            {
                return new RateLimitStatus
                {
                    IsAllowed = true,
                    IsWhitelisted = true,
                    Message = "IP en whitelist"
                };
            }

            if (_config.IsBlacklisted(ip))
            {
                return new RateLimitStatus
                {
                    IsAllowed = false,
                    IsBlacklisted = true,
                    Message = "IP en blacklist permanente"
                };
            }

            if (_blockList.IsBlocked(ip))
            {
                var blockInfo = _blockList.GetBlockInfo(ip);
                return new RateLimitStatus
                {
                    IsAllowed = false,
                    IsBlocked = true,
                    BlockedUntil = blockInfo?.BlockedUntil,
                    BlockReason = blockInfo?.Reason,
                    Message = $"IP bloqueada hasta {blockInfo?.BlockedUntil:yyyy-MM-dd HH:mm:ss}"
                };
            }

            string identifier = GenerateIdentifier(ip, userAgent, token);
            var limit = _config.GetLimitForPath(endpoint);

            if (_clients.TryGetValue(identifier, out var clientInfo))
            {
                lock (clientInfo.Lock)
                {
                    var now = DateTime.UtcNow;
                    var recentRequests = clientInfo.Requests.Count(time => (now - time) <= limit.TimeWindow);

                    return new RateLimitStatus
                    {
                        IsAllowed = recentRequests < limit.MaxRequests,
                        CurrentRequests = recentRequests,
                        MaxRequests = limit.MaxRequests,
                        TimeWindow = limit.TimeWindow,
                        RemainingRequests = Math.Max(0, limit.MaxRequests - recentRequests),
                        ResetTime = clientInfo.Requests.Any()
                            ? clientInfo.Requests.Min().Add(limit.TimeWindow)
                            : now,
                        Message = $"{recentRequests}/{limit.MaxRequests} peticiones usadas"
                    };
                }
            }

            return new RateLimitStatus
            {
                IsAllowed = true,
                CurrentRequests = 0,
                MaxRequests = limit.MaxRequests,
                TimeWindow = limit.TimeWindow,
                RemainingRequests = limit.MaxRequests,
                ResetTime = DateTime.UtcNow.Add(limit.TimeWindow),
                Message = "Sin peticiones recientes"
            };
        }

        public void AddToWhitelist(string ip)
        {
            _config.AddWhitelistedIP(ip);
            _blockList.UnblockIP(ip, "Agregado a whitelist");
            SecurityLogger.LogWhitelistAction(ip, "ADDED");
            Console.WriteLine($"[RateLimiter] IP agregada a whitelist: {ip}");
        }

        public void RemoveFromWhitelist(string ip)
        {
            _config.WhitelistedIPs.Remove(ip);
            SecurityLogger.LogWhitelistAction(ip, "REMOVED");
            Console.WriteLine($"[RateLimiter] IP removida de whitelist: {ip}");
        }

        public void AddToBlacklist(string ip)
        {
            _config.AddBlacklistedIP(ip);
            _blockList.BlockIP(ip, TimeSpan.FromDays(3650), "Blacklist permanente", 999);
            SecurityLogger.LogBlacklistAction(ip, "ADDED");
            Console.WriteLine($"[RateLimiter] IP agregada a blacklist: {ip}");
        }

        public void RemoveFromBlacklist(string ip)
        {
            _config.BlacklistedIPs.Remove(ip);
            _blockList.UnblockIP(ip, "Removido de blacklist");
            SecurityLogger.LogBlacklistAction(ip, "REMOVED");
            Console.WriteLine($"[RateLimiter] IP removida de blacklist: {ip}");
        }

        public void UnblockIP(string ip)
        {
            _blockList.UnblockIP(ip, "Desbloqueado manualmente");
            Console.WriteLine($"[RateLimiter] IP desbloqueada manualmente: {ip}");
        }

        public List<BlockedIPInfo> GetBlockedIPs()
        {
            return _blockList.GetAllBlockedIPs();
        }

        public Dictionary<string, object> GetStatistics()
        {
            var blockedIPs = _blockList.GetAllBlockedIPs();

            return new Dictionary<string, object>
            {
                ["activeClients"] = _clients.Count,
                ["blockedIPs"] = blockedIPs.Count,
                ["whitelistedIPs"] = _config.WhitelistedIPs.Count,
                ["blacklistedIPs"] = _config.BlacklistedIPs.Count,
                ["configuredEndpoints"] = _config.EndpointLimits.Count,
                ["blockedIPsList"] = blockedIPs.Select(b => new
                {
                    b.IP,
                    blockedAt = b.BlockedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    blockedUntil = b.BlockedUntil.ToString("yyyy-MM-dd HH:mm:ss"),
                    b.Reason,
                    b.ViolationCount
                }).ToList()
            };
        }

        private void StartCleanupTask()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));
                    CleanupOldClients();
                }
            });
        }

        private void CleanupOldClients()
        {
            try
            {
                var now = DateTime.UtcNow;
                var oldClients = _clients
                    .Where(kvp => (now - kvp.Value.LastRequestTime) > TimeSpan.FromHours(1))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in oldClients)
                {
                    _clients.TryRemove(key, out _);
                }

                if (oldClients.Count > 0)
                {
                    Console.WriteLine($"[RateLimiter] {oldClients.Count} clientes inactivos removidos");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en limpieza de clientes: {ex.Message}");
            }
        }
    }

    public class ClientRequestInfo
    {
        public string IP { get; set; }
        public string Endpoint { get; set; }
        public List<DateTime> Requests { get; set; } = new List<DateTime>();
        public int ViolationCount { get; set; } = 0;
        public DateTime LastRequestTime { get; set; } = DateTime.UtcNow;
        public object Lock { get; } = new object();
    }

    public class RateLimitStatus
    {
        public bool IsAllowed { get; set; }
        public bool IsWhitelisted { get; set; }
        public bool IsBlacklisted { get; set; }
        public bool IsBlocked { get; set; }
        public int CurrentRequests { get; set; }
        public int MaxRequests { get; set; }
        public int RemainingRequests { get; set; }
        public TimeSpan TimeWindow { get; set; }
        public DateTime ResetTime { get; set; }
        public DateTime? BlockedUntil { get; set; }
        public string BlockReason { get; set; }
        public string Message { get; set; }
    }
}