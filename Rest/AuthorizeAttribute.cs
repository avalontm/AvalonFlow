using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class AuthorizeAttribute : Attribute
    {
        public string? Roles { get; set; }
        public string? AuthenticationScheme { get; set; } = "Bearer";

        public AuthorizeAttribute() { }

        public AuthorizeAttribute(string roles)
        {
            Roles = roles;
        }
    }
}
