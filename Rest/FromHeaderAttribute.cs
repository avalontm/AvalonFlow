namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromHeaderAttribute : Attribute
    {
        public string? Name { get; }

        public FromHeaderAttribute(string? name = null)
        {
            Name = name;
        }
    }

}
