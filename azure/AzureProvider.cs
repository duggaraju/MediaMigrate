﻿using MediaMigrate.Contracts;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Azure
{
    internal class AzureProvider : ICloudProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly TokenCredential _credentials;

        public AzureProvider(
            ILoggerFactory loggerFactory,
            TokenCredential credentials)
        {
            _credentials = credentials;
            _loggerFactory = loggerFactory;
        }

        public IUploader GetStorageProvider(StorageOptions assetOptions)
            => new AzureStorageUploader(assetOptions, _credentials, _loggerFactory.CreateLogger<AzureStorageUploader>());

        public ISecretUploader GetSecretProvider(KeyOptions keyOptions)
            => new KeyVaultUploader(keyOptions, _credentials, _loggerFactory.CreateLogger<KeyVaultUploader>());
    }
}
