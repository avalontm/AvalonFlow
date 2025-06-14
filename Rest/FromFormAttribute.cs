namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromFormAttribute : Attribute
    {
        public string Name { get; set; }

        public FromFormAttribute() { }

        public FromFormAttribute(string name)
        {
            Name = name;
        }
    }
}
