namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Class)]
    public class AvalonControllerAttribute : Attribute
    {
        public string Route { get; }

        public AvalonControllerAttribute(string routeTemplate = "api/[controller]")
        {
            Route = routeTemplate;
        }
    }

}
