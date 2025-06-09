using System.Text.Json;

namespace AvalonFlow
{
    public static class JsonElementExtensions
    {
        public static string GetString(this JsonElement json, string propertyName, string defaultValue = "")
        {
            if (json.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }

            return defaultValue;
        }

        public static int GetInt32(this JsonElement json, string propertyName, int defaultValue = 0)
        {
            if (json.TryGetProperty(propertyName, out var prop) && prop.TryGetInt32(out var value))
            {
                return value;
            }

            return defaultValue;
        }

        public static bool GetBoolean(this JsonElement json, string propertyName, bool defaultValue = false)
        {
            if (json.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.True)
                return true;
            if (prop.ValueKind == JsonValueKind.False)
                return false;

            return defaultValue;
        }

        public static double GetDouble(this JsonElement json, string propertyName, double defaultValue = 0)
        {
            if (json.TryGetProperty(propertyName, out var prop) && prop.TryGetDouble(out var value))
            {
                return value;
            }

            return defaultValue;
        }

        public static JsonElement? GetElement(this JsonElement json, string propertyName)
        {
            if (json.TryGetProperty(propertyName, out var prop))
            {
                return prop;
            }

            return null;
        }

        public static T? GetObject<T>(this JsonElement json, string propertyName)
        {
            if (json.TryGetProperty(propertyName, out var prop))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(prop.GetRawText());
                }
                catch
                {
                    return default;
                }
            }
            return default;
        }
    }
}
