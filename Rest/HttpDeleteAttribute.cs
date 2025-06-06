using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HttpDeleteAttribute : AvalonRestAttribute
    {
        public HttpDeleteAttribute(string path = "/") : base(AvalonHttpMethod.DELETE, path) { }
    }
}
