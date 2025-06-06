using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class ActionResult
    {
        public int StatusCode { get; }
        public object? Value { get; }

        public ActionResult(int statusCode, object? value = null)
        {
            StatusCode = statusCode;
            Value = value;
        }
    }

}
