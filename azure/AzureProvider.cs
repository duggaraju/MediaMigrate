using MediaMigrate.Contracts;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace MediaMigrate.Azure
{
    internal class AzureProvider : ICloudProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly TokenCredential _credentials;
        private readonly IMemoryCache _cache;

        public AzureProvider(
            ILoggerFactory loggerFactory,
            IMemoryCache cache,
            TokenCredential credentials)
        {
            _credentials = credentials;
            _cache = cache;
            _loggerFactory = loggerFactory;
        }

        public IUploader GetStorageProvider(StorageOptions assetOptions)
            => new AzureStorageUploader(assetOptions, _cache, _credentials, _loggerFactory.CreateLogger<AzureStorageUploader>());

        public ISecretUploader GetSecretProvider(KeyOptions keyOptions)
            => new KeyVaultUploader(keyOptions, _credentials, _loggerFactory.CreateLogger<KeyVaultUploader>());
    }
}
