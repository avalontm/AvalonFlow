using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HttpPutAttribute : AvalonRestAttribute
    {
        public HttpPutAttribute(string path = "/") : base(AvalonHttpMethod.PUT, path) { }
    }
}
