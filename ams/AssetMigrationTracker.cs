using MediaMigrate.Contracts;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Ams
{
    internal class AssetMigrationTracker : IMigrationTracker<BlobContainerClient, AssetMigrationResult>
    {
        public const string StatusKey = "status";
        public const string UrlKey = "url";
        public const string ManifestKey = "manifest";

        private readonly ILogger _logger;

        public AssetMigrationTracker(ILogger<AssetMigrationTracker> logger)
        {
            _logger = logger;
        }

        public async Task<IAsyncDisposable?> BeginMigrationAsync(BlobContainerClient container, CancellationToken cancellationToken)
        {
            try
            {
                if (!await LeaseTracker.HasLeaseAsync(container, cancellationToken))
                {
                    return await LeaseTracker.AcquireAsync(container, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to aqcuire lease  on container {name}", container.Name);
                _logger.LogWarning("A lease is already present on container {name} so skipping it.", container.Name);
            }
            return null;
        }

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

        public async Task ResetMigrationStatus(BlobContainerClient container, CancellationToken cancellationToken)
        {
            var metadata = new Dictionary<string, string>();
            await container.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
        }

        sealed class LeaseTracker : IAsyncDisposable
        {
            static readonly string LeaseId = "MediaMigration" + Guid.NewGuid().ToString();
            static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

            private readonly BlobLeaseClient _lease;
            private readonly CancellationTokenSource _source;

            public LeaseTracker(BlobLeaseClient lease, CancellationToken cancellationToken)
            {
                _lease = lease;
                _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ = Task.Run(async () => await RenewAsync(_source.Token), _source.Token);
            }

            public static async Task<bool> HasLeaseAsync(BlobContainerClient container, CancellationToken cancellationToken)
            {
                BlobContainerProperties properties = await container.GetPropertiesAsync(cancellationToken: cancellationToken);
                return properties.LeaseState == LeaseState.Leased;
            }

            public static async Task<IAsyncDisposable> AcquireAsync(BlobContainerClient container, CancellationToken cancellationToken)
            {
                var lease = container.GetBlobLeaseClient(LeaseId);
                await lease.AcquireAsync(LeaseDuration, cancellationToken: cancellationToken);
                return new LeaseTracker(lease, cancellationToken);
            }

            private async Task RenewAsync(CancellationToken cancellationToken)
            {
                while(!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(LeaseDuration.Add(TimeSpan.FromSeconds(-5)), cancellationToken);
                    await _lease.RenewAsync(cancellationToken: cancellationToken);
                }
            }

            public async ValueTask DisposeAsync()
            {
                _source.Cancel();
                await _lease.ReleaseAsync();
                _source.Dispose();
            }
        }
    }
}
