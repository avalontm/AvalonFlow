using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class ParameterResolutionContext
    {
        public string Body { get; set; }
        public Dictionary<string, string> FormData { get; set; }
        public Dictionary<string, MultipartFormDataParser.FormField> MultipartData { get; set; }
        public JsonDocument JsonDoc { get; set; }
        public bool IsJsonRequest { get; set; }
    }

}
