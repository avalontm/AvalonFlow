using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromFileAttribute : Attribute
    {
        public string? Name { get; set; }

        public FromFileAttribute() { }

        public FromFileAttribute(string name)
        {
            Name = name;
        }
    }
}
