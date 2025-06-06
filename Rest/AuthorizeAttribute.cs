using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class AuthorizeAttribute : Attribute
    {
        public string? AuthenticationScheme { get; set; }

        public AuthorizeAttribute()
        {
            AuthenticationScheme = "Bearer";
        }

        public AuthorizeAttribute(string scheme)
        {
            AuthenticationScheme = scheme;
        }
    }
}
