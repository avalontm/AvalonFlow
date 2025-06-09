namespace AvalonFlow
{
    public static class AvalonServiceRegistry
    {
        private static readonly Dictionary<Type, object> _services = new();

        public static void RegisterSingleton<T>(T instance)
        {
            _services[typeof(T)] = instance!;
        }

        public static T Resolve<T>()
        {
            return (T)_services[typeof(T)];
        }

        public static bool TryResolve<T>(out T? instance)
        {
            if (_services.TryGetValue(typeof(T), out var obj))
            {
                instance = (T)obj;
                return true;
            }
            instance = default;
            return false;
        }
    }

}
