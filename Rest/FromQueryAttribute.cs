namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class FromQueryAttribute : Attribute
    {
        public string? Name { get; }

        public FromQueryAttribute() { }

        public FromQueryAttribute(string name)
        {
            Name = name;
        }
    }

}
