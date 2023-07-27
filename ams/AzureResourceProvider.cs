
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
        protected readonly ArmClient _armClient;

        private readonly Dictionary<string, BlobServiceClient> _storageAccounts = new();

        public AzureResourceProvider(TokenCredential credential, GlobalOptions options)
        {
            _globalOptions = options;
            _credentials = credential;
            _armClient = new ArmClient(credential);
            var resourceGroupId = ResourceGroupResource.CreateResourceIdentifier(
                options.Subscription,
                options.ResourceGroup);
            _resourceGroup = _armClient.GetResourceGroupResource(resourceGroupId);
        }

        public async Task<MediaServicesAccountResource> GetMediaAccountAsync(
            CancellationToken cancellationToken)
        {
            MediaServicesAccountResource account = await _resourceGroup.GetMediaServicesAccountAsync(
            _globalOptions.AccountName, cancellationToken);
            foreach (var storage in account.Data.StorageAccounts)
            {
                var storageAccount = 
                    await _armClient.GetStorageAccountResource(storage.Id).GetAsync(cancellationToken: cancellationToken);
                _storageAccounts.Add(storage.Id.Name, GetStorageAccount(storageAccount));
            }
            return account;
        }

        public async ValueTask<BlobContainerClient> GetStorageContainerAsync(
            MediaServicesAccountResource account,
            MediaAssetResource asset,
            CancellationToken cancellationToken)
        {
            if (!_storageAccounts.TryGetValue(asset.Data.StorageAccountName, out var storageAccount))
            {
                var storage = account.Data.StorageAccounts.Single(a => a.Id.Name == asset.Data.StorageAccountName);
                var resource = _armClient.GetStorageAccountResource(storage.Id);
                resource = await resource.GetAsync(cancellationToken: cancellationToken);
                storageAccount = GetStorageAccount(resource);
                return GetStorageAccount(resource).GetBlobContainerClient(asset.Data.Container);
            }
            return storageAccount.GetBlobContainerClient(asset.Data.Container);
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
