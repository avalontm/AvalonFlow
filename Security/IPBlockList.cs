using AvalonFlow.Security;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AvalonFlow.Security
{
    public class IPBlockList
    {
        private const string BLOCK_FILE = "blocked_ips.json";
        private readonly ConcurrentDictionary<string, BlockedIPInfo> _blockedIPs;
        private readonly object _fileLock = new object();

        public IPBlockList()
        {
            _blockedIPs = new ConcurrentDictionary<string, BlockedIPInfo>();
            LoadBlockedIPs();
            StartCleanupTask();
        }

        public bool IsBlocked(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            if (_blockedIPs.TryGetValue(ip, out var blockInfo))
            {
                if (DateTime.UtcNow < blockInfo.BlockedUntil)
                {
                    return true;
                }
                else
                {
                    UnblockIP(ip, "Tiempo de bloqueo expirado");
                    return false;
                }
            }

            return false;
        }

        public void BlockIP(string ip, TimeSpan duration, string reason, int violationCount = 1)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            var blockedUntil = DateTime.UtcNow.Add(duration);

            var blockInfo = new BlockedIPInfo
            {
                IP = ip,
                BlockedAt = DateTime.UtcNow,
                BlockedUntil = blockedUntil,
                Reason = reason,
                ViolationCount = violationCount
            };

            _blockedIPs.AddOrUpdate(ip, blockInfo, (key, existing) =>
            {
                existing.BlockedUntil = blockedUntil;
                existing.ViolationCount = violationCount;
                existing.Reason = reason;
                return existing;
            });

            SaveBlockedIPs();
            SecurityLogger.LogIPBlocked(ip, reason, blockedUntil, violationCount);
        }

        public void UnblockIP(string ip, string reason)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            if (_blockedIPs.TryRemove(ip, out _))
            {
                SaveBlockedIPs();
                SecurityLogger.LogIPUnblocked(ip, reason);
            }
        }

        public BlockedIPInfo GetBlockInfo(string ip)
        {
            _blockedIPs.TryGetValue(ip, out var blockInfo);
            return blockInfo;
        }

        public List<BlockedIPInfo> GetAllBlockedIPs()
        {
            return _blockedIPs.Values.ToList();
        }

        public int GetBlockedCount()
        {
            return _blockedIPs.Count;
        }

        public void ClearExpiredBlocks()
        {
            var now = DateTime.UtcNow;
            var expiredIPs = _blockedIPs
                .Where(kvp => kvp.Value.BlockedUntil < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var ip in expiredIPs)
            {
                UnblockIP(ip, "Bloqueo expirado (limpieza automática)");
            }

            if (expiredIPs.Count > 0)
            {
                Console.WriteLine($"[IPBlockList] {expiredIPs.Count} bloqueos expirados removidos");
            }
        }

        private void LoadBlockedIPs()
        {
            try
            {
                if (File.Exists(BLOCK_FILE))
                {
                    lock (_fileLock)
                    {
                        string json = File.ReadAllText(BLOCK_FILE);
                        var loadedIPs = JsonSerializer.Deserialize<List<BlockedIPInfo>>(json);

                        if (loadedIPs != null)
                        {
                            var now = DateTime.UtcNow;
                            foreach (var blockInfo in loadedIPs)
                            {
                                if (blockInfo.BlockedUntil > now)
                                {
                                    _blockedIPs.TryAdd(blockInfo.IP, blockInfo);
                                }
                            }

                            Console.WriteLine($"[IPBlockList] {_blockedIPs.Count} IPs bloqueadas cargadas desde archivo");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cargando IPs bloqueadas: {ex.Message}");
            }
        }

        private void SaveBlockedIPs()
        {
            try
            {
                lock (_fileLock)
                {
                    var blockedList = _blockedIPs.Values.ToList();
                    string json = JsonSerializer.Serialize(blockedList, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(BLOCK_FILE, json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando IPs bloqueadas: {ex.Message}");
            }
        }

        private void StartCleanupTask()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5));
                    ClearExpiredBlocks();
                }
            });
        }
    }

    public class BlockedIPInfo
    {
        public string IP { get; set; }
        public DateTime BlockedAt { get; set; }
        public DateTime BlockedUntil { get; set; }
        public string Reason { get; set; }
        public int ViolationCount { get; set; }
    }
}