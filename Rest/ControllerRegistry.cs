using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AvalonFlow.Rest
{
    public class ControllerRegistry
    {
        private readonly Dictionary<string, Type> _controllers = new();

        public void RegisterAllControllers()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                RegisterControllersFromAssembly(assembly);
            }
        }

        private void RegisterControllersFromAssembly(Assembly assembly)
        {
            var controllerTypes = assembly.GetTypes()
                .Where(t =>
                    t.GetCustomAttribute<AvalonControllerAttribute>() != null &&
                    typeof(IAvalonController).IsAssignableFrom(t) &&
                    !t.IsAbstract && t.IsClass);

            foreach (var type in controllerTypes)
            {
                var attr = type.GetCustomAttribute<AvalonControllerAttribute>();
                var controllerName = type.Name.ToLowerInvariant().Replace("controller", "");

                string route = attr.Route.ToLowerInvariant();
                route = route.Replace("[controller]", controllerName).Trim('/');

                _controllers[route] = type;
            }
        }

        public (Type controllerType, string subPath)? FindController(string requestPath)
        {
            if (string.IsNullOrEmpty(requestPath))
                return null;

            requestPath = requestPath.ToLowerInvariant().TrimEnd('/');
            string[] requestSegments = string.IsNullOrEmpty(requestPath)
                ? new string[0]
                : requestPath.Split('/');

            var sortedControllers = _controllers
                .OrderByDescending(kvp => kvp.Key.Split('/').Length)
                .ThenByDescending(kvp => kvp.Key.Length);

            foreach (var controllerEntry in sortedControllers)
            {
                string controllerRoute = controllerEntry.Key;
                string[] controllerSegments = controllerRoute.Split('/');

                if (requestSegments.Length >= controllerSegments.Length)
                {
                    bool isMatch = true;

                    for (int i = 0; i < controllerSegments.Length; i++)
                    {
                        if (!requestSegments[i].Equals(controllerSegments[i], StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = false;
                            break;
                        }
                    }

                    if (isMatch)
                    {
                        var remainingSegments = requestSegments.Skip(controllerSegments.Length);
                        string subPath = remainingSegments.Any()
                            ? "/" + string.Join("/", remainingSegments)
                            : "/";

                        return (controllerEntry.Value, subPath);
                    }
                }
            }

            return null;
        }

        public IEnumerable<string> GetRegisteredRoutes()
        {
            return _controllers.Keys;
        }

        public int GetControllerCount()
        {
            return _controllers.Count;
        }
    }
}