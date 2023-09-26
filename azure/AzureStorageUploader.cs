using MediaMigrate.Contracts;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Azure.Storage.Sas;
using Microsoft.Extensions.Caching.Memory;

namespace MediaMigrate.Azure
{
    class AzureFileUploader : IFileUploader
    {
        private readonly ILogger _logger;
        private readonly BlobContainerClient _container;
        private readonly StorageOptions _options;
        private readonly string _prefix;
        private readonly IMemoryCache _cache;

        public AzureFileUploader(IMemoryCache cache, BlobContainerClient container, string prefix, StorageOptions options, ILogger logger)
        {
            _cache = cache;
            _container = container;
            _options = options;
            _logger = logger;
            _prefix = prefix;
        }

        public async Task UploadAsync(
            string fileName,
            Stream content,
            ContentHeaders headers,
            IProgress<long> progress,
            CancellationToken cancellationToken)
        {
            var filePath = _prefix + fileName;
            _logger.LogTrace(
                "Uploading to {fileName} in container {container} of account: {account}...",
                filePath, _container.Name, _container.AccountName);
            var blob = _container.GetBlockBlobClient(filePath);
            var options = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = headers.ContentType,
                    ContentLanguage = headers.ContentLanguage
                },
                ProgressHandler = progress,
                Conditions = new BlobRequestConditions
                {
                    IfNoneMatch = _options.OverWrite ? null : ETag.All
                }
            };
            ;
            await blob.UploadAsync(content, options, cancellationToken: cancellationToken);
            _logger.LogDebug("Finished uploading {name} to {file}", filePath, fileName);
        }

        public async Task UploadBlobAsync(
            string fileName,
            BlockBlobClient blob,
            CancellationToken cancellationToken)
        {
            var outputBlob = _container.GetBlockBlobClient(_prefix + fileName);
            var uri = await GetCopyUriAsync(blob, cancellationToken);
            var operation = await outputBlob.StartCopyFromUriAsync(uri, cancellationToken: cancellationToken);
            await operation.WaitForCompletionAsync(cancellationToken);
            _logger.LogDebug("Finished uploading {name} to {file}", blob.Name, fileName);
        }

        private async Task<UserDelegationKey?> GetUserDelegationKey(BlobServiceClient client, CancellationToken cancellationToken)
        {
            return await _cache.GetOrCreateAsync(client.AccountName, async entry =>
            {
                var now = DateTimeOffset.UtcNow;
                entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(2));
                var response = await client.GetUserDelegationKeyAsync(now.AddSeconds(-10), now.AddHours(2), cancellationToken);
                return response.Value;
            });
        }

        private async Task<Uri> GetCopyUriAsync(BlockBlobClient blob, CancellationToken cancellationToken)
        {
            if (blob.CanGenerateSasUri)
            {
                return blob.Uri;
            }

            var client = blob.GetParentBlobContainerClient().GetParentBlobServiceClient();
            var delegationKey = await GetUserDelegationKey(client, cancellationToken) 
                ?? throw new InvalidOperationException("Could not create delgation key"); ;

            // Create a SAS token for the blob resource
            var sasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = blob.BlobContainerName,
                BlobName = blob.Name,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow,
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(5)
            };

            // Specify the necessary permissions
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            // Add the SAS token to the blob URI
            var uriBuilder = new BlobUriBuilder(blob.Uri)
            {
                // Specify the user delegation key
                Sas = sasBuilder.ToSasQueryParameters(delegationKey, client.AccountName)
            };

            return uriBuilder.ToUri();
        }
    }

    internal class AzureStorageUploader : IUploader
    {
        private readonly StorageOptions _options;
        private readonly ILogger _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IMemoryCache _cache;

        public AzureStorageUploader(
            StorageOptions options,
            IMemoryCache cache,
            TokenCredential credential,
            ILogger<AzureStorageUploader> logger)
        {
            _options = options;
            _cache = cache;
            _logger = logger;
            if (!Uri.TryCreate(options.StoragePath, UriKind.Absolute, out var storageUri))
            {
                storageUri = new Uri($"https://{options.StoragePath}.blob.core.windows.net");
            }
            _blobServiceClient = new BlobServiceClient(storageUri, credential);
        }

        public Uri GetDestinationUri(string container, string fileName)
        {
            return new Uri(_blobServiceClient.Uri, $"/{container}/{fileName}");
        }

        public async Task<IFileUploader> GetUploaderAsync(string containerName, string prefix, CancellationToken cancellationToken)
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            if (!await container.ExistsAsync(cancellationToken))
            {
                await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }

            return new AzureFileUploader(_cache, container, prefix, _options, _logger);
        }
    }
}
