using Azure.Storage.Blobs.Specialized;
using FFMpegCore.Pipes;

namespace MediaMigrate.Pipes
{
    internal class BlobSink : IPipeSink
    {
        private readonly BlockBlobClient _blobClient;
        private readonly string _format;

        public BlobSink(BlockBlobClient blobClient, string format = "dash")
        {
            _blobClient = blobClient;
            _format = format;
        }

        // The ffmpeg format is dash.
        public string GetFormat() => _format;

        public async Task ReadAsync(Stream inputStream, CancellationToken cancellationToken)
        {
            await _blobClient.UploadAsync(inputStream, cancellationToken: cancellationToken);
        }
    }
}
