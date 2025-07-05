using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow.Rest
{
    public class FileUploadCollection
    {
        private readonly Dictionary<string, List<IFormFile>> _files = new();

        public void Add(string key, IFormFile file)
        {
            if (!_files.ContainsKey(key))
                _files[key] = new List<IFormFile>();

            _files[key].Add(file);
        }

        public IFormFile? GetFile(string key)
        {
            return _files.TryGetValue(key, out var files) ? files.FirstOrDefault() : null;
        }

        public List<IFormFile> GetFiles(string key)
        {
            return _files.TryGetValue(key, out var files) ? files : new List<IFormFile>();
        }

        public List<IFormFile> GetAllFiles()
        {
            return _files.Values.SelectMany(f => f).ToList();
        }

        public Dictionary<string, List<IFormFile>> GetAllFilesByKey()
        {
            return _files.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}
