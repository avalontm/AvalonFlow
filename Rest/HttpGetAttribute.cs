using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HttpGetAttribute : AvalonRestAttribute
    {
        public HttpGetAttribute(string path = "/") : base(AvalonHttpMethod.GET, path) { }
    }
}
