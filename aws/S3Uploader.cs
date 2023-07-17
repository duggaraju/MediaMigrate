
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Aws
{
    internal class S3FileUploader : IFileUploader
    {
        private readonly ILogger _logger;
        private readonly AmazonS3Client _client;
        private readonly string _bucketName;

        public S3FileUploader(AmazonS3Client client, string bucketName, ILogger logger)
        {
            _client = client;
            _bucketName = bucketName;
            _logger = logger;
        }

        public async Task UploadAsync(
            string fileName,
            Stream content,
            IProgress<long> progress,
            CancellationToken cancellationToken)
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = fileName,
                InputStream = content,
                AutoResetStreamPosition = false,
                AutoCloseStream = false
            };
            await _client.PutObjectAsync(request, cancellationToken);
        }
    }

    internal class S3Uploader : IUploader
    {
        private readonly ILogger<S3Uploader> _logger;
        private readonly AmazonS3Client _client;

        public S3Uploader(StorageOptions options, AWSCredentials credentials, ILogger<S3Uploader> logger)
        {
            _logger = logger;
            _client = new AmazonS3Client(credentials, new AmazonS3Config());
        }

        public Uri GetDestinationUri(string container, string fileName)
        {
            throw new NotImplementedException();
        }

        public Task<IFileUploader> GetUploaderAsync(string container, CancellationToken cancellationToken)
        {
            return Task.FromResult<IFileUploader>(new S3FileUploader(_client, container, _logger));
        }
    }
}
