using MediaMigrate.Contracts;
using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Gcp
{
    internal class GCSFileUploader : IFileUploader
    {
        private readonly ILogger _logger;
        private readonly StorageClient _client;
        private readonly string _bucketName;
        private readonly string _prefix;

        public GCSFileUploader(StorageClient client, string bucketName, string prefix, ILogger logger)
        {
            _client = client;
            _bucketName = bucketName;
            _prefix = prefix;
            _logger = logger;
        }

        public async Task UploadAsync(
            string fileName,
            Stream content,
            ContentHeaders headers,
            IProgress<long> progress,
            CancellationToken cancellationToken)
        {
            var upload = new Google.Apis.Storage.v1.Data.Object
            {
                Bucket = _bucketName,
                Name = _prefix + fileName,
                ContentType = headers.ContentType,
                ContentLanguage = headers.ContentLanguage
            };
            var gcsProgress = new Progress<IUploadProgress>(p => progress.Report(p.BytesSent));
            await _client.UploadObjectAsync(upload, content, cancellationToken: cancellationToken, progress: gcsProgress);
        }
    }

    internal class GCSUploader : IUploader
    {
        private readonly ILogger _logger;
        private readonly StorageClient _client;

        public GCSUploader(ILogger<GCSUploader> logger)
        {
            _logger = logger;
            _client = StorageClient.Create();
        }

        public Uri GetDestinationUri(string container, string fileName)
        {
            throw new NotImplementedException();
        }

        public Task<IFileUploader> GetUploaderAsync(string container, string prefix, CancellationToken cancellationToken)
        {
            return Task.FromResult<IFileUploader>(new GCSFileUploader(_client, container, prefix, _logger));
        }
    }
}
