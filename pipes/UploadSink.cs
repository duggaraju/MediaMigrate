using MediaMigrate.Contracts;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Pipes
{
    internal class UploadSink : IPipeSink
    {
        private readonly IFileUploader _uploader;
        private readonly string _filename;
        private readonly ILogger _logger;
        private readonly IProgress<long> _progress;
        private readonly ContentHeaders _contentHeaders;

        public UploadSink(
            IFileUploader uploader,
            string filename,
            ContentHeaders headers,
            IProgress<long> progress,
            ILogger logger)
        {
            _uploader = uploader;
            _filename = filename;
            _contentHeaders = headers;
            _progress = progress;
            _logger = logger;
        }

        // The ffmpeg format is dash.
        public string GetFormat() => "dash";

        public async Task ReadAsync(Stream inputStream, CancellationToken cancellationToken)
        {
            try
            {
                await _uploader.UploadAsync(_filename, inputStream, _contentHeaders, _progress, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload {blob}", _filename);
                throw;
            }
        }

        public async Task UploadAsync(string filename, CancellationToken cancellationToken)
        {
            using var file = File.OpenRead(filename);
            await _uploader.UploadAsync(_filename, file, _contentHeaders, _progress, cancellationToken);
        }
    }
}
