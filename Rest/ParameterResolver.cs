using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class ParameterResolver
    {
        private readonly int _maxBodySize;

        public ParameterResolver(int maxBodySize)
        {
            _maxBodySize = maxBodySize;
        }

        public async Task<object[]> ResolveParametersAsync(
            MethodInfo method,
            HttpListenerContext context,
            Dictionary<string, string> routeParams)
        {
            var parameters = method.GetParameters();
            var resolved = new List<object>();

            string body = null;
            Dictionary<string, string> formData = null;
            Dictionary<string, MultipartFormDataParser.FormField> multipartData = null;
            JsonDocument jsonDoc = null;
            bool isJsonRequest = false;

            if (context.Request.HasEntityBody)
            {
                try
                {
                    long maxBytesAllowed = (long)_maxBodySize * 1024 * 1024;

                    using var reader = new StreamReader(context.Request.InputStream);
                    var buffer = new char[65536];
                    var stringBuilder = new StringBuilder();
                    int totalBytesRead = 0;
                    int bytesRead;

                    while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        totalBytesRead += Encoding.UTF8.GetByteCount(buffer, 0, bytesRead);

                        if (totalBytesRead > maxBytesAllowed)
                        {
                            throw new InvalidOperationException($"Request body exceeds maximum allowed size of {_maxBodySize}MB");
                        }

                        stringBuilder.Append(buffer, 0, bytesRead);

                        if (totalBytesRead > 10 * 1024 * 1024)
                        {
                            var progressMB = totalBytesRead / (1024.0 * 1024.0);
                            if ((int)progressMB % 50 == 0)
                            {
                                AvalonFlowInstance.Log($"Reading request body progress: {progressMB:F1}MB");
                            }
                        }
                    }

                    body = stringBuilder.ToString();

                    string contentType = context.Request.ContentType ?? "";

                    isJsonRequest = contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                                   parameters.Any(p => p.GetCustomAttribute<FromBodyAttribute>() != null);

                    if (isJsonRequest && !string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            jsonDoc = JsonDocument.Parse(body);
                        }
                        catch (JsonException ex)
                        {
                            AvalonFlowInstance.Log($"Error parsing JSON: {ex.Message}");
                            throw new InvalidOperationException($"Invalid JSON format: {ex.Message}");
                        }
                    }
                    else if (!isJsonRequest && !string.IsNullOrWhiteSpace(body))
                    {
                        bool hasFormParam = parameters.Any(p =>
                            p.GetCustomAttribute<FromFormAttribute>() != null ||
                            p.GetCustomAttribute<FromFileAttribute>() != null);

                        if (hasFormParam)
                        {
                            if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                            {
                                using var bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(body));
                                multipartData = await MultipartFormDataParser.ParseAsync(bodyStream, contentType);
                                formData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                                foreach (var field in multipartData.Values)
                                {
                                    if (!field.IsFile)
                                    {
                                        formData[field.Name] = field.Value;
                                    }
                                }
                            }
                            else if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                            {
                                formData = ParseUrlEncodedData(body);
                            }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error reading request body: {ex.Message}");
                    throw new InvalidOperationException($"Error processing request body: {ex.Message}");
                }
            }

            var resolutionContext = new ParameterResolutionContext
            {
                Body = body,
                FormData = formData,
                MultipartData = multipartData,
                JsonDoc = jsonDoc,
                IsJsonRequest = isJsonRequest
            };

            foreach (var param in parameters)
            {
                try
                {
                    object resolvedValue = ResolveParameter(param, context, routeParams, resolutionContext);
                    resolved.Add(resolvedValue);
                }
                catch (Exception ex)
                {
                    AvalonFlowInstance.Log($"Error resolving parameter '{param.Name}': {ex.Message}");
                    throw;
                }
            }

            return resolved.ToArray();
        }

        private object ResolveParameter(
            ParameterInfo param,
            HttpListenerContext context,
            Dictionary<string, string> routeParams,
            ParameterResolutionContext ctx)
        {
            var fromBody = param.GetCustomAttribute<FromBodyAttribute>() != null;
            var fromHeader = param.GetCustomAttribute<FromHeaderAttribute>();
            var fromQuery = param.GetCustomAttribute<FromQueryAttribute>();
            var fromForm = param.GetCustomAttribute<FromFormAttribute>();
            var fromFile = param.GetCustomAttribute<FromFileAttribute>();

            if (fromFile != null && ctx.MultipartData != null)
            {
                return ResolveFileParameter(param, fromFile, ctx.MultipartData);
            }

            if (fromForm != null && !ctx.IsJsonRequest && (ctx.FormData != null || ctx.MultipartData != null))
            {
                return ResolveFormParameter(param, fromForm, ctx.FormData, ctx.MultipartData);
            }

            if (fromBody)
            {
                return ResolveBodyParameter(param, ctx.JsonDoc, ctx.Body);
            }

            if (fromHeader != null)
            {
                return ResolveHeaderParameter(param, fromHeader, context.Request);
            }

            if (fromQuery != null)
            {
                return ResolveQueryParameter(param, fromQuery, context.Request);
            }

            if (param.ParameterType == typeof(HttpListenerContext))
            {
                return context;
            }

            if (routeParams.TryGetValue(param.Name.ToLowerInvariant(), out var routeValue))
            {
                return Convert.ChangeType(routeValue, param.ParameterType);
            }

            return param.HasDefaultValue ? param.DefaultValue :
                   param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null;
        }

        private object ResolveFileParameter(
            ParameterInfo param,
            FromFileAttribute fromFile,
            Dictionary<string, MultipartFormDataParser.FormField> multipartData)
        {
            string fieldName = fromFile.Name ?? param.Name;

            if (!multipartData.TryGetValue(fieldName, out var fileField) || !fileField.IsFile)
            {
                return param.HasDefaultValue ? param.DefaultValue : null;
            }

            if (param.ParameterType == typeof(IFormFile))
            {
                var stream = fileField.FileData != null
                    ? new MemoryStream(fileField.FileData)
                    : new MemoryStream();
                stream.Position = 0;

                return new FormFile(
                    stream: stream,
                    name: fieldName,
                    fileName: fileField.FileName ?? Path.GetRandomFileName(),
                    contentType: fileField.ContentType ?? "application/octet-stream",
                    length: fileField.FileData?.Length ?? 0
                );
            }

            if (param.ParameterType == typeof(MultipartFormDataParser.FormField))
            {
                return fileField;
            }
            else if (param.ParameterType == typeof(byte[]))
            {
                return fileField.FileData ?? Array.Empty<byte>();
            }
            else if (param.ParameterType == typeof(string))
            {
                return fileField.FileName ?? "";
            }
            else if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
            {
                return CreateFileModel(param.ParameterType, fileField);
            }

            return fileField;
        }

        private object ResolveFormParameter(
            ParameterInfo param,
            FromFormAttribute fromForm,
            Dictionary<string, string> formData,
            Dictionary<string, MultipartFormDataParser.FormField> multipartData)
        {
            string fieldName = fromForm.Name ?? param.Name;

            if (formData != null && formData.TryGetValue(fieldName, out var formValue))
            {
                return Convert.ChangeType(formValue, param.ParameterType);
            }

            if (multipartData != null && multipartData.TryGetValue(fieldName, out var multipartField))
            {
                if (!multipartField.IsFile)
                {
                    return Convert.ChangeType(multipartField.Value, param.ParameterType);
                }
            }

            if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
            {
                return CreateFormModel(param.ParameterType, fromForm, formData, multipartData);
            }

            return param.HasDefaultValue ? param.DefaultValue : null;
        }

        private object ResolveBodyParameter(ParameterInfo param, JsonDocument jsonDoc, string body)
        {
            if (jsonDoc == null)
            {
                if (!string.IsNullOrWhiteSpace(body))
                {
                    throw new InvalidOperationException($"FromBody parameter '{param.Name}' requires valid JSON content");
                }
                return param.HasDefaultValue ? param.DefaultValue : null;
            }

            if (param.ParameterType == typeof(JsonElement))
            {
                return jsonDoc.RootElement.Clone();
            }
            else if (param.ParameterType == typeof(JsonDocument))
            {
                return jsonDoc;
            }

            try
            {
                return JsonSerializer.Deserialize(
                    jsonDoc.RootElement.GetRawText(),
                    param.ParameterType,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }
                ) ?? (param.HasDefaultValue ? param.DefaultValue : null);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid JSON format for parameter '{param.Name}': {ex.Message}");
            }
        }

        private object ResolveHeaderParameter(ParameterInfo param, FromHeaderAttribute fromHeader, HttpListenerRequest request)
        {
            string headerName = fromHeader.Name ?? param.Name;

            var possibleHeaderNames = new List<string>
            {
                headerName,
                headerName.Replace('_', '-'),
                headerName.Replace('-', '_')
            }.Distinct();

            string headerValue = null;

            foreach (var name in possibleHeaderNames)
            {
                headerValue = request.Headers[name];
                if (!string.IsNullOrEmpty(headerValue))
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(headerValue))
            {
                if (param.HasDefaultValue)
                    return param.DefaultValue;

                throw new InvalidOperationException($"Missing required header. Tried: {string.Join(", ", possibleHeaderNames)}");
            }

            try
            {
                return Convert.ChangeType(headerValue, param.ParameterType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cannot convert header value '{headerValue}' to type '{param.ParameterType.Name}': {ex.Message}");
            }
        }

        private object ResolveQueryParameter(ParameterInfo param, FromQueryAttribute fromQuery, HttpListenerRequest request)
        {
            string queryName = fromQuery.Name ?? param.Name;

            var possibleParamNames = new List<string>
            {
                queryName,
                queryName.Replace('_', '-'),
                queryName.Replace('-', '_')
            }.Distinct().ToList();

            string queryValue = null;

            foreach (var name in possibleParamNames)
            {
                queryValue = request.QueryString[name];
                if (!string.IsNullOrEmpty(queryValue))
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(queryValue))
            {
                return param.HasDefaultValue ? param.DefaultValue :
                       param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null;
            }

            try
            {
                return Convert.ChangeType(queryValue, param.ParameterType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cannot convert query parameter '{queryName}' value '{queryValue}' to type '{param.ParameterType.Name}': {ex.Message}");
            }
        }

        private object CreateFileModel(Type modelType, MultipartFormDataParser.FormField fileField)
        {
            var instance = Activator.CreateInstance(modelType);
            var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (!property.CanWrite) continue;

                switch (property.Name.ToLowerInvariant())
                {
                    case "filename":
                        property.SetValue(instance, fileField.FileName);
                        break;
                    case "contenttype":
                        property.SetValue(instance, fileField.ContentType);
                        break;
                    case "data":
                    case "content":
                    case "filedata":
                        if (property.PropertyType == typeof(byte[]))
                            property.SetValue(instance, fileField.FileData);
                        break;
                    case "size":
                    case "length":
                        if (property.PropertyType == typeof(int) || property.PropertyType == typeof(long))
                            property.SetValue(instance, Convert.ChangeType(fileField.FileData?.Length ?? 0, property.PropertyType));
                        break;
                }
            }

            return instance;
        }

        private object CreateFormModel(
            Type modelType,
            FromFormAttribute fromForm,
            Dictionary<string, string> formData,
            Dictionary<string, MultipartFormDataParser.FormField> multipartData)
        {
            var instance = Activator.CreateInstance(modelType);
            var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (!property.CanWrite) continue;

                string formKey = !string.IsNullOrEmpty(fromForm.Name)
                    ? $"{fromForm.Name}.{property.Name}"
                    : property.Name;

                string formValue = null;

                if (formData != null && formData.TryGetValue(formKey, out var fdValue))
                {
                    formValue = fdValue;
                }
                else if (multipartData != null && multipartData.TryGetValue(formKey, out var mpField) && !mpField.IsFile)
                {
                    formValue = mpField.Value;
                }

                if (formValue != null)
                {
                    try
                    {
                        Type targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                        object convertedValue = Convert.ChangeType(formValue, targetType);
                        property.SetValue(instance, convertedValue);
                    }
                    catch (Exception ex)
                    {
                        AvalonFlowInstance.Log($"Error converting form field '{formKey}': {ex.Message}");
                    }
                }
            }

            return instance;
        }

        private Dictionary<string, string> ParseUrlEncodedData(string formData)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pairs = formData.Split('&');

            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2)
                {
                    result[Uri.UnescapeDataString(keyValue[0])] = Uri.UnescapeDataString(keyValue[1]);
                }
            }

            return result;
        }
    }
}