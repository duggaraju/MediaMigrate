using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Local
{
    internal class LocalFileUploader : IFileUploader
    {
        private readonly StorageOptions _options;
        private readonly ILogger _logger;
        private readonly string _basePath;

        public LocalFileUploader(StorageOptions options, string container, ILogger logger)
        {
            _options = options;
            _logger = logger;
            _basePath = Path.Combine(_options.StoragePath, container);
            Directory.CreateDirectory(_basePath);
        }

        public async Task UploadAsync(
            string fileName,
            Stream content,
            IProgress<long> progress,
            CancellationToken cancellationToken)
        {
            var subDir = Path.GetDirectoryName(fileName) ?? string.Empty;
            if (subDir[0] == '/' || subDir[0] == '\\')
            {
                subDir = subDir.Substring(1);
            }

            var baseDir = Path.Combine(_basePath, subDir);
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            var filePath = Path.Combine(baseDir, Path.GetFileName(fileName));
            _logger.LogDebug("Uploading to file {file}", filePath);
            using var file = File.OpenWrite(filePath);
            await content.CopyToAsync(file, 8192, cancellationToken);
            _logger.LogDebug("Finshed uploading to file {file}", filePath);
        }
    }

    internal class LocalUploader : IUploader
    {
        private readonly StorageOptions _options;
        private readonly ILogger _logger;

        public LocalUploader(StorageOptions options, ILogger<LocalUploader> logger)
        {
            _options = options;
            _logger = logger;
            Directory.CreateDirectory(_options.StoragePath);
        }

        public Uri GetDestinationUri(string container, string fileName)
        {
            if (fileName[0] == '/' || fileName[0] == '\\')
            {
                fileName = fileName.Substring(1);
            }
            return new Uri(Path.Combine(_options.StoragePath, container, fileName));
        }

        public Task<IFileUploader> GetUploaderAsync(string container, CancellationToken cancellationToken)
        {
            return Task.FromResult<IFileUploader>(new LocalFileUploader(_options, container, _logger));
        }
    }
}
