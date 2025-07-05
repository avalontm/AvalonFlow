using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AvalonFlow.Rest
{
    public class MultipartFormDataParser
    {
        private static readonly Regex BoundaryRegex = new(@"boundary=([^\s;]+)", RegexOptions.IgnoreCase);
        private static readonly Regex ContentDispositionRegex = new(@"Content-Disposition:\s*form-data;\s*name=""([^""]+)""(?:;\s*filename=""([^""]+)"")?", RegexOptions.IgnoreCase);
        private static readonly Regex ContentTypeRegex = new(@"Content-Type:\s*(.+)", RegexOptions.IgnoreCase);

        public class FormField
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string? FileName { get; set; }
            public string? ContentType { get; set; }
            public byte[]? FileData { get; set; }
            public bool IsFile => !string.IsNullOrEmpty(FileName);
        }

        public static async Task<Dictionary<string, FormField>> ParseAsync(Stream stream, string contentType)
        {
            // Extraer boundary correctamente
            var boundaryMatch = Regex.Match(contentType, @"boundary=(?<boundary>[^\s;]+)");
            if (!boundaryMatch.Success)
                throw new InvalidOperationException("No se encontró boundary en Content-Type");

            string boundary = "--" + boundaryMatch.Groups["boundary"].Value.Trim('"');
            var fields = new Dictionary<string, FormField>(StringComparer.OrdinalIgnoreCase);

            using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
            {
                string content = await reader.ReadToEndAsync();
                var parts = content.Split(new[] { boundary }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    var field = ParseFormField(part.Trim());
                    if (field != null && !string.IsNullOrEmpty(field.Name))
                    {
                        fields[field.Name] = field;
                    }
                }
            }

            return fields;
        }

        private static FormField ParseFormField(string partContent)
        {
            var field = new FormField();
            using (var reader = new StringReader(partContent))
            {
                string line;
                bool headersEnded = false;
                var dataLines = new List<string>();

                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        headersEnded = true;
                        continue;
                    }

                    if (!headersEnded)
                    {
                        // Procesar headers
                        if (line.StartsWith("Content-Disposition:"))
                        {
                            var match = Regex.Match(line, @"name=""([^""]+)""");
                            if (match.Success) field.Name = match.Groups[1].Value;

                            match = Regex.Match(line, @"filename=""([^""]+)""");
                            if (match.Success) field.FileName = match.Groups[1].Value;
                        }
                        else if (line.StartsWith("Content-Type:"))
                        {
                            field.ContentType = line.Substring("Content-Type:".Length).Trim();
                        }
                    }
                    else
                    {
                        // Procesar datos
                        dataLines.Add(line);
                    }
                }

                if (field.IsFile)
                {
                    var data = string.Join(Environment.NewLine, dataLines)
                        .TrimEnd('\r', '\n', '-');
                    field.FileData = Encoding.UTF8.GetBytes(data);
                }
                else
                {
                    field.Value = string.Join(Environment.NewLine, dataLines)
                        .TrimEnd('\r', '\n', '-');
                }
            }

            return field;
        }
    }
}