using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HttpPatchAttribute : AvalonRestAttribute
    {
        public HttpPatchAttribute(string path = "/"): base(AvalonHttpMethod.PATCH, path) { }
    }
}
