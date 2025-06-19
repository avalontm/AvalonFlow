using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    // Añade esta clase para manejar el rate limiting
    internal class RateLimiter
    {
        private readonly Dictionary<string, Queue<DateTime>> _requestTimes = new();
        private readonly int _maxRequests;
        private readonly TimeSpan _timeWindow;
        private readonly object _lock = new();

        public RateLimiter(int maxRequests, TimeSpan timeWindow)
        {
            _maxRequests = maxRequests;
            _timeWindow = timeWindow;
        }

        public bool IsAllowed(string clientId)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                if (!_requestTimes.TryGetValue(clientId, out var times))
                {
                    times = new Queue<DateTime>();
                    _requestTimes[clientId] = times;
                }

                // Eliminar peticiones fuera de la ventana de tiempo
                while (times.Count > 0 && now - times.Peek() > _timeWindow)
                {
                    times.Dequeue();
                }

                if (times.Count >= _maxRequests)
                {
                    return false;
                }

                times.Enqueue(now);
                return true;
            }
        }
    }
}
