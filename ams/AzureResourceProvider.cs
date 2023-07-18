﻿
using MediaMigrate.Contracts;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Media;
using Azure.ResourceManager.Resources;
using Azure.Storage.Blobs;

namespace MediaMigrate.Ams
{
    class AzureResourceProvider
    {
        protected readonly ResourceGroupResource _resourceGroup;
        protected readonly GlobalOptions _globalOptions;
        protected readonly TokenCredential _credentials;

        public AzureResourceProvider(TokenCredential credential, GlobalOptions options)
        {
            _globalOptions = options;
            _credentials = credential;
            var armClient = new ArmClient(credential);
            var resourceGroupId = ResourceGroupResource.CreateResourceIdentifier(
                options.Subscription,
                options.ResourceGroup);
            _resourceGroup = armClient.GetResourceGroupResource(resourceGroupId);
        }

        public async Task<MediaServicesAccountResource> GetMediaAccountAsync(
            CancellationToken cancellationToken)
        {
            return await _resourceGroup.GetMediaServicesAccountAsync(
            _globalOptions.AccountName, cancellationToken);
        }

        public async Task<BlobServiceClient> GetStorageAccountAsync(
            MediaServicesAccountResource account,
            CancellationToken cancellationToken)
        {
            var storage = account.Data.StorageAccounts[0];
            var resource = await _resourceGroup.GetStorageAccountAsync(storage.Id.Name, cancellationToken: cancellationToken);
            return GetStorageAccount(resource);
        }

        public async Task<(BlobServiceClient, ResourceIdentifier)> GetStorageAccount(CancellationToken cancellationToken)
        {
            StorageAccountResource storage =
            await _resourceGroup.GetStorageAccountAsync(_globalOptions.AccountName, cancellationToken: cancellationToken);
            return (GetStorageAccount(storage), storage.Id);
        }

        private BlobServiceClient GetStorageAccount(StorageAccountResource storage)
        {
            var uri = storage.Data.PrimaryEndpoints.BlobUri!;
            return new BlobServiceClient(uri, _credentials);
        }
    }
}
