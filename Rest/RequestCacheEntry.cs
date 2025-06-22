using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class RequestCacheEntry
    {
        public DateTime Timestamp { get; set; }
        public string ResponseHash { get; set; }
        public int StatusCode { get; set; }
    }

    public class IdempotencyKeyAttribute : Attribute
    {
        public bool Enabled { get; set; } = true;
    }
}
