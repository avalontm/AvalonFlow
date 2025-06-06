namespace AvalonFlow.Websocket
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class AvalonFlowAttribute : Attribute
    {
        public string EventName { get; }

        public AvalonFlowAttribute(string eventName)
        {
            EventName = eventName;
        }
    }

}
