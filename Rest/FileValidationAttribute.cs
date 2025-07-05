namespace AvalonFlow.Rest
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
    public class FileValidationAttribute : Attribute
    {
        public long MaxFileSize { get; set; } = 10 * 1024 * 1024; // 10MB
        public string AllowedExtensions { get; set; } = string.Empty;
        public string AllowedMimeTypes { get; set; } = string.Empty;
        public bool ValidateContent { get; set; } = true;

        public FileValidationOptions ToOptions()
        {
            return new FileValidationOptions
            {
                MaxFileSize = MaxFileSize,
                AllowedExtensions = AllowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                   .Select(e => e.Trim().ToLowerInvariant())
                                                   .ToArray(),
                AllowedMimeTypes = AllowedMimeTypes.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(t => t.Trim())
                                                  .ToArray(),
                ValidateContent = ValidateContent
            };
        }
    }
}
