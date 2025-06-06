using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HttpOptionsAttribute : AvalonRestAttribute
    {
        public HttpOptionsAttribute(string path = "/") : base(AvalonHttpMethod.OPTIONS, path) { }
    }
}
