namespace AvalonFlow.Rest
{
    public interface IFormFile
    {
        string Name { get; }
        string FileName { get; }
        string ContentType { get; }
        long Length { get; }
        Stream OpenReadStream();
        void CopyTo(Stream target);
        Task CopyToAsync(Stream target, CancellationToken cancellationToken = default);
    }

    public class FormFile : IFormFile, IDisposable
    {
        private readonly Stream _stream;
        private bool _disposed = false;

        public FormFile(Stream stream, string name, string fileName, string contentType, long length)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
            Length = length;
        }

        public string Name { get; }
        public string FileName { get; }
        public string ContentType { get; }
        public long Length { get; }

        public Stream OpenReadStream()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FormFile));
            _stream.Position = 0;
            return _stream;
        }

        public void CopyTo(Stream target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (_disposed) throw new ObjectDisposedException(nameof(FormFile));

            _stream.Position = 0;
            _stream.CopyTo(target);
        }

        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (_disposed) throw new ObjectDisposedException(nameof(FormFile));

            _stream.Position = 0;
            await _stream.CopyToAsync(target, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _stream.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

