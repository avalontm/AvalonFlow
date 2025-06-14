using System.Text.Json;

namespace AvalonFlow
{
    public static class JsonElementExtensions
    {
        public static string GetString(this JsonElement json, string propertyName, string defaultValue = "")
        {
            try
            {
                // Obtener el JSON raw y parsearlo
                string rawJson = json.GetRawText();
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;

                if (root.TryGetProperty(propertyName, out var prop))
                {
                    // Manejar diferentes tipos de valores
                    switch (prop.ValueKind)
                    {
                        case JsonValueKind.String:
                            return prop.GetString() ?? defaultValue;
                        case JsonValueKind.Number:
                            return prop.ToString();
                        case JsonValueKind.True:
                            return "true";
                        case JsonValueKind.False:
                            return "false";
                        case JsonValueKind.Null:
                            return defaultValue;
                        default:
                            return prop.GetRawText();
                    }
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static int GetInt32(this JsonElement json, string propertyName, int defaultValue = 0)
        {
            try
            {
                // Obtener el JSON raw y parsearlo
                string rawJson = json.GetRawText();
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;

                if (root.TryGetProperty(propertyName, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
                    {
                        return value;
                    }

                    // Intentar convertir desde string
                    if (prop.ValueKind == JsonValueKind.String)
                    {
                        var stringValue = prop.GetString();
                        if (int.TryParse(stringValue, out var parsedValue))
                        {
                            return parsedValue;
                        }
                    }
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static bool GetBoolean(this JsonElement json, string propertyName, bool defaultValue = false)
        {
            try
            {
                // Obtener el JSON raw y parsearlo
                string rawJson = json.GetRawText();
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;

                if (root.TryGetProperty(propertyName, out var prop))
                {
                    switch (prop.ValueKind)
                    {
                        case JsonValueKind.True:
                            return true;
                        case JsonValueKind.False:
                            return false;
                        case JsonValueKind.String:
                            var stringValue = prop.GetString()?.ToLowerInvariant();
                            return stringValue == "true" || stringValue == "1" || stringValue == "yes";
                        case JsonValueKind.Number:
                            return prop.TryGetInt32(out var intValue) && intValue != 0;
                    }
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static double GetDouble(this JsonElement json, string propertyName, double defaultValue = 0)
        {
            try
            {
                // Obtener el JSON raw y parsearlo
                string rawJson = json.GetRawText();
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;

                if (root.TryGetProperty(propertyName, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var value))
                    {
                        return value;
                    }

                    // Intentar convertir desde string
                    if (prop.ValueKind == JsonValueKind.String)
                    {
                        var stringValue = prop.GetString();
                        if (double.TryParse(stringValue, out var parsedValue))
                        {
                            return parsedValue;
                        }
                    }
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static JsonElement? GetElement(this JsonElement json, string propertyName)
        {
            try
            {
                // Obtener el JSON raw y parsearlo
                string rawJson = json.GetRawText();
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;

                if (root.TryGetProperty(propertyName, out var prop))
                {
                    return prop;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static T? GetObject<T>(this JsonElement json, string propertyName, JsonSerializerOptions? options = null)
        {
            try
            {
                // Obtener el JSON raw y parsearlo
                string rawJson = json.GetRawText();
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;

                if (root.TryGetProperty(propertyName, out var prop))
                {
                    return JsonSerializer.Deserialize<T>(prop.GetRawText(), options);
                }
                return default;
            }
            catch
            {
                return default;
            }
        }

        // Método adicional para obtener valores con case-insensitive
        public static string GetStringIgnoreCase(this JsonElement json, string propertyName, string defaultValue = "")
        {
            try
            {
                // Obtener el JSON raw y parsearlo
                string rawJson = json.GetRawText();
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;

                // Primero intentar con el nombre exacto
                if (root.TryGetProperty(propertyName, out var prop))
                {
                    return GetStringValue(prop, defaultValue);
                }

                // Si no funciona, buscar case-insensitive
                foreach (var property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return GetStringValue(property.Value, defaultValue);
                    }
                }

                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string GetStringValue(JsonElement element, string defaultValue)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? defaultValue;
                case JsonValueKind.Number:
                    return element.ToString();
                case JsonValueKind.True:
                    return "true";
                case JsonValueKind.False:
                    return "false";
                case JsonValueKind.Null:
                    return defaultValue;
                default:
                    return element.GetRawText();
            }
        }
    }
}