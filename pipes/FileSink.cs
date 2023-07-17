
using FFMpegCore.Pipes;

namespace MediaMigrate.Pipes
{
    /// <summary>
    /// write a file to a sink.
    /// </summary>
    class FileSource
    {
        private readonly IPipeSink _sink;
        private readonly string _filePath;

        public FileSource(string filePath, IPipeSink sink)
        {
            _filePath = filePath;
            _sink = sink;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var file = File.OpenRead(_filePath);
            await _sink.ReadAsync(file, cancellationToken);
        }
    }

    /// <summary>
    /// read to a file from a source.
    /// </summary>
    class FileSink
    {
        private readonly IPipeSource _source;
        private readonly string _filePath;

        public FileSink(string filePath, IPipeSource source)
        {
            _filePath = filePath;
            _source = source;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            // If downloading to file optimize by paralle downloading.
            if (_source is BlobSource blobSource)
            {
                await blobSource.DownloadAsync(_filePath, cancellationToken);
            }
            else
            {
                using var file = File.OpenWrite(_filePath);
                await _source.WriteAsync(file, cancellationToken);
            }
        }
    }
}
