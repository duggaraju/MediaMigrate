using MediaMigrate.Contracts;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace MediaMigrate.Ams
{
    internal class AssetMigrationTracker : IMigrationTracker<BlobContainerClient, AssetMigrationResult>
    {
        public const string StatusKey = "status";
        public const string UrlKey = "url";
        public const string ManifestKey = "manifest";

        public async Task<AssetMigrationResult> GetMigrationStatusAsync(BlobContainerClient container, CancellationToken cancellationToken)
        {
            BlobContainerProperties properties = await container.GetPropertiesAsync(cancellationToken: cancellationToken);
            if (!properties.Metadata.TryGetValue(StatusKey, out var value) ||
                !Enum.TryParse<MigrationStatus>(value, out var status))
            {
                status = MigrationStatus.NotMigrated;
            }
            Uri? uri = null;
            if (properties.Metadata.TryGetValue(UrlKey, out var uriValue) && !string.IsNullOrEmpty(uriValue))
            {
                uri = new Uri(uriValue, UriKind.Absolute);
            }

            properties.Metadata.TryGetValue(ManifestKey, out var manifest);
            return new AssetMigrationResult(status, uri, manifest);
        }

        public async Task UpdateMigrationStatus(BlobContainerClient container, AssetMigrationResult result, CancellationToken cancellationToken)
        {
            var metadata = new Dictionary<string, string>
            {
                { StatusKey, result.Status.ToString() },
                { UrlKey, result.Uri?.ToString() ?? string.Empty },
                { ManifestKey, result.ManifestName ?? string.Empty }
            };
            await container.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
        }
    }
}
