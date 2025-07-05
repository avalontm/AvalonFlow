using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class FileValidationOptions
    {
        public long MaxFileSize { get; set; } = 10 * 1024 * 1024; // 10MB
        public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
        public string[] AllowedMimeTypes { get; set; } = Array.Empty<string>();
        public string[] ProhibitedExtensions { get; set; } = { ".exe", ".bat", ".cmd", ".com", ".pif", ".scr", ".vbs", ".js" };
        public bool ValidateContent { get; set; } = true;
    }

    public class FileValidator
    {
        private readonly FileValidationOptions _options;

        public FileValidator(FileValidationOptions options)
        {
            _options = options;
        }

        public async Task<FileValidationResult> ValidateAsync(IFormFile file)
        {
            var result = new FileValidationResult();

            // Validar tamaño
            if (file.Length > _options.MaxFileSize)
            {
                result.AddError($"File size ({file.Length / 1024.0 / 1024.0:F2}MB) exceeds maximum allowed size ({_options.MaxFileSize / 1024.0 / 1024.0:F2}MB)");
            }

            // Validar extensión
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (_options.ProhibitedExtensions.Contains(extension))
            {
                result.AddError($"File extension '{extension}' is not allowed for security reasons");
            }

            if (_options.AllowedExtensions.Any() && !_options.AllowedExtensions.Contains(extension))
            {
                result.AddError($"File extension '{extension}' is not allowed. Allowed extensions: {string.Join(", ", _options.AllowedExtensions)}");
            }

            // Validar MIME type
            if (_options.AllowedMimeTypes.Any() && !_options.AllowedMimeTypes.Contains(file.ContentType))
            {
                result.AddError($"File type '{file.ContentType}' is not allowed. Allowed types: {string.Join(", ", _options.AllowedMimeTypes)}");
            }

            // Validar contenido (básico)
            if (_options.ValidateContent)
            {
                await ValidateFileContentAsync(file, result);
            }

            return result;
        }

        private async Task ValidateFileContentAsync(IFormFile file, FileValidationResult result)
        {
            try
            {
                using var stream = file.OpenReadStream();
                var buffer = new byte[512]; // Leer primeros 512 bytes
                await stream.ReadAsync(buffer, 0, buffer.Length);

                // Validaciones básicas de contenido
                if (IsExecutableFile(buffer))
                {
                    result.AddError("File appears to be an executable and is not allowed");
                }

                // Validar que el contenido coincida con la extensión declarada
                if (!ValidateFileSignature(file.FileName, buffer))
                {
                    result.AddWarning("File content may not match the declared file type");
                }
            }
            catch (Exception ex)
            {
                result.AddError($"Error validating file content: {ex.Message}");
            }
        }

        private bool IsExecutableFile(byte[] buffer)
        {
            // Verificar firmas de archivos ejecutables
            if (buffer.Length < 4) return false;

            // PE header (Windows executables)
            if (buffer[0] == 0x4D && buffer[1] == 0x5A) return true;

            // ELF header (Linux executables)
            if (buffer[0] == 0x7F && buffer[1] == 0x45 && buffer[2] == 0x4C && buffer[3] == 0x46) return true;

            return false;
        }

        private bool ValidateFileSignature(string fileName, byte[] buffer)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            // Aquí puedes agregar más validaciones de firmas de archivos
            switch (extension)
            {
                case ".pdf":
                    return buffer.Length >= 4 && buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46;
                case ".jpg":
                case ".jpeg":
                    return buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xD8;
                case ".png":
                    return buffer.Length >= 8 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47;
                case ".gif":
                    return buffer.Length >= 6 && buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46;
                default:
                    return true; // No validation for unknown types
            }
        }
    }

    public class FileValidationResult
    {
        public bool IsValid => !Errors.Any();
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        public void AddError(string error)
        {
            Errors.Add(error);
        }

        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }
    }
}
