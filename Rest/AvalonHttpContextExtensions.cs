using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public static class AvalonHttpContextExtensions
    {
        public static async Task<FileUploadCollection> GetFormFilesAsync(this AvalonHttpContext context)
        {
            var collection = new FileUploadCollection();

            // Aquí integrarías con el parsing de multipart que ya tienes
            // Este es un ejemplo de cómo podrías implementarlo

            return collection;
        }

        public static async Task<IFormFile?> GetFormFileAsync(this AvalonHttpContext context, string key)
        {
            var files = await context.GetFormFilesAsync();
            return files.GetFile(key);
        }
    }
}
