using System;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;

namespace AvalonFlow.Security
{
    public static class SecurityLogger
    {
        private const string LOG_DIRECTORY = "logs";
        private const string SECURITY_LOG_FILE = "security.log";
        private const string RATE_LIMIT_LOG_FILE = "rate_limit.log";
        private const string BLOCKED_IPS_FILE = "blocked_ips.log";

        private static readonly object _lockObject = new object();
        private static ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();

        static SecurityLogger()
        {
            EnsureLogDirectoryExists();
        }

        private static void EnsureLogDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(LOG_DIRECTORY))
                {
                    Directory.CreateDirectory(LOG_DIRECTORY);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creando directorio de logs: {ex.Message}");
            }
        }

        private static string GetLogFilePath(string fileName)
        {
            return Path.Combine(LOG_DIRECTORY, fileName);
        }

        public static void LogRateLimitViolation(string identifier, string endpoint, string ip, int currentCount, int maxAllowed)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Type = "RATE_LIMIT_VIOLATION",
                Identifier = identifier,
                Endpoint = endpoint,
                IP = ip,
                CurrentCount = currentCount,
                MaxAllowed = maxAllowed,
                Severity = "WARNING"
            };

            WriteLog(RATE_LIMIT_LOG_FILE, logEntry);
            WriteLog(SECURITY_LOG_FILE, logEntry);
        }

        public static void LogIPBlocked(string ip, string reason, DateTime blockedUntil, int violationCount)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Type = "IP_BLOCKED",
                IP = ip,
                Reason = reason,
                BlockedUntil = blockedUntil.ToString("yyyy-MM-dd HH:mm:ss"),
                ViolationCount = violationCount,
                Severity = "HIGH"
            };

            WriteLog(BLOCKED_IPS_FILE, logEntry);
            WriteLog(SECURITY_LOG_FILE, logEntry);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[SECURITY ALERT] IP Bloqueada: {ip} hasta {blockedUntil:yyyy-MM-dd HH:mm:ss}");
            Console.ResetColor();
        }

        public static void LogIPUnblocked(string ip, string reason)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Type = "IP_UNBLOCKED",
                IP = ip,
                Reason = reason,
                Severity = "INFO"
            };

            WriteLog(BLOCKED_IPS_FILE, logEntry);
            WriteLog(SECURITY_LOG_FILE, logEntry);
        }

        public static void LogSuspiciousActivity(string activity, string ip, string endpoint, string details = null)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Type = "SUSPICIOUS_ACTIVITY",
                Activity = activity,
                IP = ip,
                Endpoint = endpoint,
                Details = details,
                Severity = "WARNING"
            };

            WriteLog(SECURITY_LOG_FILE, logEntry);
        }

        public static void LogFailedLogin(string email, string ip, string endpoint)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Type = "FAILED_LOGIN",
                Email = email,
                IP = ip,
                Endpoint = endpoint,
                Severity = "MEDIUM"
            };

            WriteLog(SECURITY_LOG_FILE, logEntry);
        }

        public static void LogSuccessfulLogin(string email, string ip, string endpoint)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Type = "SUCCESSFUL_LOGIN",
                Email = email,
                IP = ip,
                Endpoint = endpoint,
                Severity = "INFO"
            };

            WriteLog(SECURITY_LOG_FILE, logEntry);
        }

        public static void LogWhitelistAction(string ip, string action, string performedBy = "System")
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Type = "WHITELIST_ACTION",
                IP = ip,
                Action = action,
                PerformedBy = performedBy,
                Severity = "INFO"
            };

            WriteLog(SECURITY_LOG_FILE, logEntry);
        }

        public static void LogBlacklistAction(string ip, string action, string performedBy = "System")
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Type = "BLACKLIST_ACTION",
                IP = ip,
                Action = action,
                PerformedBy = performedBy,
                Severity = "HIGH"
            };

            WriteLog(SECURITY_LOG_FILE, logEntry);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[SECURITY] Blacklist: {action} - IP: {ip}");
            Console.ResetColor();
        }

        private static void WriteLog(string fileName, object logEntry)
        {
            try
            {
                string logText = JsonSerializer.Serialize(logEntry) + Environment.NewLine;
                string filePath = GetLogFilePath(fileName);

                lock (_lockObject)
                {
                    File.AppendAllText(filePath, logText);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error escribiendo log de seguridad: {ex.Message}");
            }
        }

        public static void RotateLogs(int maxFileSizeMB = 10)
        {
            try
            {
                var logFiles = new[] { SECURITY_LOG_FILE, RATE_LIMIT_LOG_FILE, BLOCKED_IPS_FILE };
                long maxBytes = maxFileSizeMB * 1024 * 1024;

                foreach (var logFile in logFiles)
                {
                    string filePath = GetLogFilePath(logFile);

                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);

                        if (fileInfo.Length > maxBytes)
                        {
                            string backupPath = GetLogFilePath($"{Path.GetFileNameWithoutExtension(logFile)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                            File.Move(filePath, backupPath);

                            Console.WriteLine($"Log rotado: {logFile} -> {Path.GetFileName(backupPath)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rotando logs: {ex.Message}");
            }
        }

        public static void CleanOldLogs(int daysToKeep = 30)
        {
            try
            {
                if (!Directory.Exists(LOG_DIRECTORY))
                    return;

                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
                var files = Directory.GetFiles(LOG_DIRECTORY, "*.log");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoffDate)
                    {
                        File.Delete(file);
                        Console.WriteLine($"Log antiguo eliminado: {Path.GetFileName(file)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error limpiando logs antiguos: {ex.Message}");
            }
        }
    }
}