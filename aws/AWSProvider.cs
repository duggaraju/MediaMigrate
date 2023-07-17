using Amazon.Runtime;
using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Aws
{
    internal class AWSProvider : ICloudProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly AWSCredentials _credentials;

        public AWSProvider(
            ILoggerFactory loggerFactory,
            AWSCredentials credentials)
        {
            _loggerFactory = loggerFactory;
            _credentials = credentials;
        }

        public IUploader GetStorageProvider(StorageOptions options)
            => new S3Uploader(options, _credentials, _loggerFactory.CreateLogger<S3Uploader>());

        public ISecretUploader GetSecretProvider(KeyOptions keyOptions) => throw new NotImplementedException();
    }
}
