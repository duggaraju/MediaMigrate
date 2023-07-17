using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Local
{
    internal class LocalFileProvider : ICloudProvider
    {
        private readonly ILoggerFactory _loggerFactory;

        public LocalFileProvider(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public ISecretUploader GetSecretProvider(KeyOptions keyOptions)
        {
            throw new NotImplementedException();
        }

        public IUploader GetStorageProvider(StorageOptions options)
        {
            return new LocalUploader(options, _loggerFactory.CreateLogger<LocalUploader>());
        }
    }
}
