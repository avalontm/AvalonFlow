using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace AvalonFlow.Rest
{
    public class ServerLogger
    {
        private readonly int _port;

        public ServerLogger(int port)
        {
            _port = port;
        }

        public void LogServerAddresses()
        {
            try
            {
                string hostName = Dns.GetHostName();
                IPAddress[] hostAddresses = Dns.GetHostAddresses(hostName);
                var activeListeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();

                AvalonFlowInstance.Log($"Host: {hostName}");
                AvalonFlowInstance.Log("Direcciones IP locales:");

                foreach (var ip in hostAddresses)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        AvalonFlowInstance.Log($"- {ip}");
                    }
                }

                AvalonFlowInstance.Log("\nInterfaces de red activas:");
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        AvalonFlowInstance.Log($"- {ni.Name} ({ni.Description})");
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                AvalonFlowInstance.Log($"  - {ip.Address}");
                            }
                        }
                    }
                }

                AvalonFlowInstance.Log("\nPuertos escuchando:");
                foreach (var listener in activeListeners)
                {
                    if (listener.Port == _port)
                    {
                        AvalonFlowInstance.Log($"- {listener.Address}:{listener.Port} ({(listener.Address.Equals(IPAddress.Any) ? "PUBLICO (0.0.0.0)" : (listener.Address.Equals(IPAddress.Loopback) ? "LOCALHOST" : "INTERFAZ ESPECIFICA"))})");
                    }
                }

                CheckPublicAccessibility();
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error al registrar direcciones: {ex.Message}");
            }
        }

        private void CheckPublicAccessibility()
        {
            try
            {
                bool isPublic = false;
                var activeListeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();

                foreach (var listener in activeListeners)
                {
                    if (listener.Port == _port && listener.Address.Equals(IPAddress.Any))
                    {
                        isPublic = true;
                        break;
                    }
                }

                if (isPublic)
                {
                    AvalonFlowInstance.Log("\nESTADO DE ACCESO: PUBLICO (0.0.0.0)");
                    AvalonFlowInstance.Log("El servidor esta configurado para aceptar conexiones desde cualquier red.");
                    AvalonFlowInstance.Log("ADVERTENCIA: Asegurate de tener proteccion adecuada (firewall, autenticacion)");
                }
                else
                {
                    AvalonFlowInstance.Log("\nESTADO DE ACCESO: PRIVADO");
                    AvalonFlowInstance.Log("El servidor solo acepta conexiones locales o de interfaces especificas.");
                }
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error al verificar accesibilidad publica: {ex.Message}");
            }
        }

        public void LogRequest(HttpListenerRequest request, HttpListenerResponse response, TimeSpan duration)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                ClientIP = request.RemoteEndPoint?.Address?.ToString(),
                Method = request.HttpMethod,
                Url = request.Url?.AbsoluteUri,
                StatusCode = response.StatusCode,
                DurationMs = duration.TotalMilliseconds,
                UserAgent = request.UserAgent,
                ContentLength = request.ContentLength64
            };

            AvalonFlowInstance.Log(JsonSerializer.Serialize(logEntry));
        }
    }
}