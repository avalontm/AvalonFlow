using System;

namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AvalonRestAttribute : Attribute
    {
        public AvalonHttpMethod Method { get; }
        public string Path { get; }

        public AvalonRestAttribute()
        {
            Method =  AvalonHttpMethod.GET;
            Path = "/".Trim().ToLowerInvariant();
        }

        public AvalonRestAttribute(AvalonHttpMethod method = AvalonHttpMethod.GET, string path = "/")
        {
            Method = method;
            Path = path.Trim().ToLowerInvariant();
        }

        public AvalonRestAttribute(string path = "/")
        {
            Method =  AvalonHttpMethod.GET;
            Path = path.Trim().ToLowerInvariant();
        }
    }
}
