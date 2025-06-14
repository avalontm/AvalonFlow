using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class MultipartFormDataParser
    {
        public class FormField
        {
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
            public string? ContentType { get; set; }
            public string? FileName { get; set; }
            public byte[]? FileData { get; set; }
            public bool IsFile => !string.IsNullOrEmpty(FileName);
        }

        public static async Task<Dictionary<string, FormField>> ParseAsync(Stream inputStream, string contentType)
        {
            var result = new Dictionary<string, FormField>(StringComparer.OrdinalIgnoreCase);

            // Extraer el boundary del Content-Type
            string boundary = ExtractBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                throw new InvalidOperationException("Invalid multipart/form-data: boundary not found");
            }

            // Leer todo el contenido del stream
            byte[] data;
            using (var ms = new MemoryStream())
            {
                await inputStream.CopyToAsync(ms);
                data = ms.ToArray();
            }

            // Parsear el contenido multipart
            await ParseMultipartData(data, boundary, result);

            return result;
        }

        private static string ExtractBoundary(string contentType)
        {
            const string boundaryPrefix = "boundary=";
            int boundaryIndex = contentType.IndexOf(boundaryPrefix, StringComparison.OrdinalIgnoreCase);

            if (boundaryIndex == -1)
                return "";

            string boundary = contentType.Substring(boundaryIndex + boundaryPrefix.Length);

            // Remover comillas si existen
            if (boundary.StartsWith("\"") && boundary.EndsWith("\""))
            {
                boundary = boundary.Substring(1, boundary.Length - 2);
            }

            // Remover cualquier parámetro adicional después del boundary
            int semicolonIndex = boundary.IndexOf(';');
            if (semicolonIndex != -1)
            {
                boundary = boundary.Substring(0, semicolonIndex);
            }

            return boundary.Trim();
        }

        private static async Task ParseMultipartData(byte[] data, string boundary, Dictionary<string, FormField> result)
        {
            byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
            byte[] endBoundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "--");

            int position = 0;

            // Buscar el primer boundary
            int boundaryStart = FindBoundary(data, boundaryBytes, position);
            if (boundaryStart == -1)
            {
                throw new InvalidOperationException("Invalid multipart data: no boundary found");
            }

            position = boundaryStart + boundaryBytes.Length;

            while (position < data.Length)
            {
                // Saltar CRLF después del boundary
                if (position + 1 < data.Length && data[position] == '\r' && data[position + 1] == '\n')
                {
                    position += 2;
                }
                else if (position < data.Length && data[position] == '\n')
                {
                    position += 1;
                }

                // Buscar el siguiente boundary
                int nextBoundaryStart = FindBoundary(data, boundaryBytes, position);
                int endBoundaryStart = FindBoundary(data, endBoundaryBytes, position);

                int partEnd;
                bool isLastPart = false;

                if (endBoundaryStart != -1 && (nextBoundaryStart == -1 || endBoundaryStart < nextBoundaryStart))
                {
                    partEnd = endBoundaryStart;
                    isLastPart = true;
                }
                else if (nextBoundaryStart != -1)
                {
                    partEnd = nextBoundaryStart;
                }
                else
                {
                    break; // No más partes
                }

                // Parsear esta parte
                byte[] partData = new byte[partEnd - position];
                Array.Copy(data, position, partData, 0, partData.Length);

                var field = await ParseSinglePart(partData);
                if (field != null && !string.IsNullOrEmpty(field.Name))
                {
                    result[field.Name] = field;
                }

                if (isLastPart)
                    break;

                position = partEnd + boundaryBytes.Length;
            }
        }

        private static int FindBoundary(byte[] data, byte[] boundary, int startIndex)
        {
            for (int i = startIndex; i <= data.Length - boundary.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < boundary.Length; j++)
                {
                    if (data[i + j] != boundary[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }

        private static async Task<FormField?> ParseSinglePart(byte[] partData)
        {
            if (partData.Length == 0)
                return null;

            var field = new FormField();

            // Encontrar la separación entre headers y contenido (doble CRLF)
            int headerEndIndex = FindHeaderEnd(partData);
            if (headerEndIndex == -1)
                return null;

            // Extraer headers
            string headersString = Encoding.UTF8.GetString(partData, 0, headerEndIndex);
            ParseHeaders(headersString, field);

            // Extraer contenido
            int contentStart = headerEndIndex;
            // Saltar el doble CRLF
            if (contentStart + 3 < partData.Length &&
                partData[contentStart] == '\r' && partData[contentStart + 1] == '\n' &&
                partData[contentStart + 2] == '\r' && partData[contentStart + 3] == '\n')
            {
                contentStart += 4;
            }
            else if (contentStart + 1 < partData.Length &&
                     partData[contentStart] == '\n' && partData[contentStart + 1] == '\n')
            {
                contentStart += 2;
            }

            int contentLength = partData.Length - contentStart;

            // Remover CRLF final si existe
            if (contentLength >= 2 &&
                partData[partData.Length - 2] == '\r' &&
                partData[partData.Length - 1] == '\n')
            {
                contentLength -= 2;
            }
            else if (contentLength >= 1 && partData[partData.Length - 1] == '\n')
            {
                contentLength -= 1;
            }

            if (field.IsFile)
            {
                // Es un archivo
                field.FileData = new byte[contentLength];
                Array.Copy(partData, contentStart, field.FileData, 0, contentLength);
            }
            else
            {
                // Es un campo de texto
                field.Value = Encoding.UTF8.GetString(partData, contentStart, contentLength);
            }

            return field;
        }

        private static int FindHeaderEnd(byte[] data)
        {
            // Buscar doble CRLF (\r\n\r\n) o doble LF (\n\n)
            for (int i = 0; i < data.Length - 3; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' &&
                    data[i + 2] == '\r' && data[i + 3] == '\n')
                {
                    return i;
                }
            }

            // Buscar doble LF como fallback
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == '\n' && data[i + 1] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }

        private static void ParseHeaders(string headersString, FormField field)
        {
            string[] headerLines = headersString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in headerLines)
            {
                if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseContentDisposition(line, field);
                }
                else if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                {
                    field.ContentType = line.Substring("Content-Type:".Length).Trim();
                }
            }
        }

        private static void ParseContentDisposition(string header, FormField field)
        {
            // Ejemplo: Content-Disposition: form-data; name="field_name"; filename="file.txt"

            string[] parts = header.Split(';');

            foreach (string part in parts)
            {
                string trimmedPart = part.Trim();

                if (trimmedPart.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                {
                    field.Name = ExtractQuotedValue(trimmedPart.Substring(5));
                }
                else if (trimmedPart.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                {
                    field.FileName = ExtractQuotedValue(trimmedPart.Substring(9));
                }
            }
        }

        private static string ExtractQuotedValue(string value)
        {
            value = value.Trim();

            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }
}