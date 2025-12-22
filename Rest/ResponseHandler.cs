using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class ResponseHandler
    {
        private readonly string _corsAllowedOrigins;
        private readonly string _corsAllowedMethods;
        private readonly string _corsAllowedHeaders;
        private bool _relaxedCspForDocs = true;

        public ResponseHandler(string corsAllowedOrigins, string corsAllowedMethods, string corsAllowedHeaders)
        {
            _corsAllowedOrigins = corsAllowedOrigins;
            _corsAllowedMethods = corsAllowedMethods;
            _corsAllowedHeaders = corsAllowedHeaders;
        }

        public void AddCorsHeaders(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = _corsAllowedOrigins;
            response.Headers["Access-Control-Allow-Methods"] = _corsAllowedMethods;
            response.Headers["Access-Control-Allow-Headers"] = _corsAllowedHeaders;
            response.Headers["Access-Control-Allow-Credentials"] = "true";
        }

        private void AddSecurityHeaders(HttpListenerResponse response, bool isDocsEndpoint = false)
        {
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            response.Headers.Add("X-Frame-Options", "DENY");
            response.Headers.Add("X-XSS-Protection", "1; mode=block");
            response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            response.Headers.Add("Referrer-Policy", "no-referrer");
            response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=()");

            if (isDocsEndpoint && _relaxedCspForDocs)
            {
                response.Headers.Add("Content-Security-Policy",
                    "default-src 'self'; " +
                    "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com https://cdn.jsdelivr.net; " +
                    "style-src 'self' 'unsafe-inline' https://unpkg.com https://cdn.jsdelivr.net; " +
                    "img-src 'self' data: https: http:; " +
                    "font-src 'self' data: https://unpkg.com; " +
                    "connect-src 'self' https://unpkg.com http://localhost:5000;");
            }
            else
            {
                response.Headers.Add("Content-Security-Policy", "default-src 'self'");
            }
        }

        public async Task RespondWithAsync(HttpListenerContext context, int statusCode, object value)
        {
            var response = context.Response;
            response.StatusCode = statusCode;
            try
            {
                AddCorsHeaders(response);

                string path = context.Request.Url?.AbsolutePath?.ToLowerInvariant() ?? "";
                bool isDocsEndpoint = path.Contains("/docs") || path.Contains("/swagger");

                if (!response.Headers.AllKeys.Any(k => k.Equals("Content-Security-Policy", StringComparison.OrdinalIgnoreCase)))
                {
                    AddSecurityHeaders(response, isDocsEndpoint);
                }
                else
                {
                    response.Headers.Add("X-Content-Type-Options", "nosniff");
                    response.Headers.Add("X-Frame-Options", "DENY");
                    response.Headers.Add("X-XSS-Protection", "1; mode=block");
                    response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
                    response.Headers.Add("Referrer-Policy", "no-referrer");
                    response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=()");
                }

                if (value is ContentResult contentResult)
                {
                    response.ContentType = contentResult.ContentType;
                    byte[] buffer = contentResult.ContentEncoding.GetBytes(contentResult.Content);
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    return;
                }

                if (value is ActionResult actionResult)
                {
                    value = actionResult.Value;
                }

                switch (value)
                {
                    case FileActionResult file:
                        await HandleFileResponse(response, file);
                        break;

                    case StreamFileActionResult streamFile:
                        await HandleStreamFileResponse(response, streamFile);
                        break;

                    case Stream stream:
                        await HandleGenericStreamResponse(response, stream);
                        break;

                    case string str when !response.ContentType.StartsWith("application/json"):
                        response.ContentType = "text/plain";
                        byte[] strBuffer = Encoding.UTF8.GetBytes(str);
                        await response.OutputStream.WriteAsync(strBuffer, 0, strBuffer.Length);
                        break;

                    default:
                        if (value != null)
                        {
                            await HandleJsonResponse(response, value);
                        }
                        else
                        {
                            response.ContentLength64 = 0;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                AvalonFlowInstance.Log($"Error writing response: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    var error = new { error = "Internal server error", details = ex.Message };
                    await HandleJsonResponse(response, error);
                }
                catch
                {
                }
            }
            finally
            {
                try
                {
                    if (response.OutputStream.CanWrite)
                    {
                        await response.OutputStream.FlushAsync();
                    }
                    response.Close();
                }
                catch
                {
                }
            }
        }

        private async Task HandleFileResponse(HttpListenerResponse response, FileActionResult file)
        {
            response.ContentType = file.ContentType ?? "application/octet-stream";
            response.AddHeader("Content-Disposition", $"attachment; filename=\"{file.FileName}\"");
            response.ContentLength64 = file.Content.Length;

            await response.OutputStream.WriteAsync(file.Content, 0, file.Content.Length);
        }

        private async Task HandleStreamFileResponse(HttpListenerResponse response, StreamFileActionResult streamFile)
        {
            response.ContentType = streamFile.ContentType ?? "application/octet-stream";

            if (!string.IsNullOrEmpty(streamFile.FileName))
            {
                var dispositionType = streamFile.IsAttachment ? "attachment" : "inline";
                response.AddHeader("Content-Disposition",
                    $"{dispositionType}; filename=\"{streamFile.FileName}\"");
            }

            try
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
                long totalBytes = 0;

                while ((bytesRead = await streamFile.ContentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytes += bytesRead;

                    if (response.ContentLength64 == 0)
                    {
                        try
                        {
                            response.ContentLength64 = streamFile.ContentStream.Length;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            finally
            {
                streamFile.ContentStream.Close();
            }
        }

        private async Task HandleGenericStreamResponse(HttpListenerResponse response, Stream stream)
        {
            response.ContentType = "application/octet-stream";

            try
            {
                byte[] buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                }
            }
            finally
            {
                stream.Close();
            }
        }

        private async Task HandleJsonResponse(HttpListenerResponse response, object value)
        {
            response.ContentType = "application/json; charset=utf-8";

            var responseJson = JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var buffer = Encoding.UTF8.GetBytes(responseJson);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
    }
}