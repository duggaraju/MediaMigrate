using MediaMigrate.Ams;
using MediaMigrate.Azure;
using MediaMigrate.Contracts;
using Azure.ResourceManager.Media.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Transform
{
    public record AssetDetails(
        string AssetName,
        BlobContainerClient Container,
        StorageEncryptedAssetDecryptionInfo? DecryptionInfo,
        Manifest? Manifest,
        ClientManifest? ClientManifest);

    internal abstract class StorageTransform : ITransform<AssetDetails, AssetMigrationResult>
    {
        protected readonly StorageOptions _options;
        private readonly TemplateMapper _templateMapper;
        protected readonly ILogger _logger;
        protected readonly IUploader _uploader;

        public StorageTransform(
            StorageOptions options,
            TemplateMapper templateMapper,
            IUploader uploader,
            ILogger logger)
        {
            _options = options;
            _templateMapper = templateMapper;
            _uploader = uploader;
            _logger = logger;
        }

        public Task<AssetMigrationResult> RunAsync(AssetDetails details, CancellationToken cancellationToken)
        {
            var outputPath = _templateMapper.ExpandPathTemplate(details.Container, _options.PathTemplate);
            return RunAsync(details, outputPath, cancellationToken);
        }

        public async Task<AssetMigrationResult> RunAsync(
            AssetDetails details,
            (string Container, string Path) outputPath,
            CancellationToken cancellationToken)
        {
            var result = new AssetMigrationResult();
            if (details.Manifest != null && details.Manifest.IsLive)
            {
                _logger.LogWarning("Skipping asset {asset} which is from a running live event. Rerun the migration after the live event is stopped.", details.AssetName);
                result.Status = MigrationStatus.Skipped;
                return result;
            }

            if (IsSupported(details))
            {
                var path = await TransformAsync(details, outputPath, cancellationToken);
                result.Status = MigrationStatus.Success;
                result.Uri = _uploader.GetDestinationUri(outputPath.Container, path);
            }

            return result;
        }

        protected abstract bool IsSupported(AssetDetails details);

        protected abstract Task<string> TransformAsync(
            AssetDetails details,
            (string Container, string Prefix) outputPath,
            CancellationToken cancellationToken = default);

        protected async Task UploadBlobAsync(
            BlockBlobClient blob,
            Decryptor? decryptor,
            IFileUploader fileUploader,
            string prefix,
            CancellationToken cancellationToken)
        {
            var blobName = $"{prefix}{blob.Name}";
            // hack optimization for direct blob copy.
            if (decryptor == null && fileUploader is AzureFileUploader uploader)
            {
                await uploader.UploadBlobAsync(blobName, blob, cancellationToken);
            }
            else
            {
                using BlobDownloadStreamingResult result = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
                var source = result.Content;
                var headers = new ContentHeaders(result.Details.ContentType, result.Details.ContentLanguage);
                var progress = new Progress<long>(progress =>
                {
                    _logger.LogTrace("Upload progress for {name}: {progress}", blobName, progress);
                });
                if (decryptor != null)
                {
                    source = decryptor.GetDecryptingReadStream(source, blob.Name);
                }
                using (source)
                    await fileUploader.UploadAsync(blobName, source, headers, progress, cancellationToken);
            }
        }
    }
}
