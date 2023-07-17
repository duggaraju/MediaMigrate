using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Gcp
{
    internal class GCSProvider : ICloudProvider
    {
        private readonly ILoggerFactory _loggerFactory;

        public GCSProvider(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public ISecretUploader GetSecretProvider(KeyOptions keyOptions)
        {
            throw new NotImplementedException();
        }

        public IUploader GetStorageProvider(StorageOptions options)
        {
            return new GCSUploader(_loggerFactory.CreateLogger<GCSUploader>());
        }
    }
}
