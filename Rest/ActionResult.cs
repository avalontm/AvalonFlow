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
