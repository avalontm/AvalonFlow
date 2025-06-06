namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class HttpPostAttribute : AvalonRestAttribute
    {
        public HttpPostAttribute(string path = "/") : base(AvalonHttpMethod.POST, path) { }
    }
}
