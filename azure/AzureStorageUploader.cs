using MediaMigrate.Contracts;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Azure
{
    class AzureFileUploader : IFileUploader
    {
        private readonly ILogger _logger;
        private readonly BlobContainerClient _container;
        private readonly StorageOptions _options;

        public AzureFileUploader(BlobContainerClient container, StorageOptions otions, ILogger logger)
        {
            _container = container;
            _options = otions;
            _logger = logger;
        }

        public async Task UploadAsync(string fileName, Stream content, IProgress<long> progress, CancellationToken cancellationToken)
        {
            _logger.LogTrace(
                "Uploading to {fileName} in container {container} of account: {account}...",
                fileName, _container.Name, _container.AccountName);
            var blob = _container.GetBlockBlobClient(fileName);
            var options = new BlobUploadOptions
            {
                ProgressHandler = progress,
                Conditions = new BlobRequestConditions
                {
                    IfNoneMatch = _options.OverWrite ? null : ETag.All
                }
            };
            ;
            await blob.UploadAsync(content, options, cancellationToken: cancellationToken);
        }

        public async Task UploadBlobAsync(
            string fileName,
            BlockBlobClient blob,
            CancellationToken cancellationToken)
        {
            var outputBlob = _container.GetBlockBlobClient(fileName);
            var operation = await outputBlob.StartCopyFromUriAsync(blob.Uri, cancellationToken: cancellationToken);
            await operation.WaitForCompletionAsync(cancellationToken);
        }
    }

    internal class AzureStorageUploader : IUploader
    {
        private readonly StorageOptions _options;
        private readonly ILogger _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public AzureStorageUploader(
            StorageOptions options,
            TokenCredential credential,
            ILogger<AzureStorageUploader> logger)
        {
            _options = options;
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

        public async Task<IFileUploader> GetUploaderAsync(string containerName, CancellationToken cancellationToken)
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            if (!await container.ExistsAsync(cancellationToken))
            {
                await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }

            return new AzureFileUploader(container, _options, _logger);
        }
    }
}
