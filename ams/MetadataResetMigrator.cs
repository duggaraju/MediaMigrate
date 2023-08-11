using Azure.Core;
using Azure.ResourceManager.Media;
using Azure.Storage.Blobs;
using MediaMigrate.Contracts;
using MediaMigrate.Log;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Threading.Channels;

namespace MediaMigrate.Ams
{
    internal class AssetMetadataResetMigrator : BaseMigrator
    {
        private readonly ILogger _logger;
        private readonly QueryOptions _options;
        private readonly IMigrationTracker<BlobContainerClient, AssetMigrationResult> _tracker;

        public AssetMetadataResetMigrator(
            GlobalOptions options,
            QueryOptions queryOptions,
            IAnsiConsole console,
            TokenCredential credential,
            IMigrationTracker<BlobContainerClient, AssetMigrationResult> tracker,
            ILogger<AssetMetadataResetMigrator> logger) : 
            base(options, console, credential)
        {
            _options = queryOptions;
            _logger = logger;
            _tracker = tracker;
        }

        public override async Task MigrateAsync(CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var account = await GetMediaAccountAsync(cancellationToken);
            _logger.LogInformation("Begin reset of asset migration metadata for account: {name}", account.Data.Name);
            var totalAssets = await QueryMetricAsync(
                account.Id.ToString(),
                "AssetCount",
                cancellationToken: cancellationToken);

            var status = Channel.CreateBounded<AssetStats>(1);
            var progress = ShowProgressAsync("Asset metadata reset", "Assets", totalAssets, status.Reader, cancellationToken);

            var stats = new AssetStats();
            var orderBy = "properties/created";
            var assets = account.GetMediaAssets()
                .GetAllAsync(_options.GetFilter(), orderby: orderBy, cancellationToken: cancellationToken);
            await MigrateInParallel(assets, async (asset, cancelllationToken) =>
            {
                await ResetAsync(account, asset, cancellationToken);
                stats.Update(MigrationStatus.Success, false, false);
                await status.Writer.WriteAsync(stats, cancellationToken);
            }, _globalOptions.BatchSize, cancellationToken);

            _logger.LogInformation("Finished reset of asset migration metadata for account: {name}. Time taken: {time}", account.Data.Name, watch.Elapsed);
            status.Writer.Complete();
            await progress;
        }

        private async ValueTask ResetAsync(
            MediaServicesAccountResource account,
            MediaAssetResource asset,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("Reset metadata for asset {name}", asset.Data.Name);
            try
            {
                var container = await _resourceProvider.GetStorageContainerAsync(account, asset, cancellationToken);
                await _tracker.ResetMigrationStatus(container, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to reset the state of asset {name}", asset.Data.Name);
                _logger.LogTrace(Events.Failure, ex, "Resetting the state failed {name}", asset.Data.Name);
            }
        }
    }

    class StorageMetadataResetMigrator : BaseMigrator
    {
        private readonly StorageQueryOptions _options;
        private readonly ILogger _logger;
        private readonly IMigrationTracker<BlobContainerClient, AssetMigrationResult> _tracker;

        public StorageMetadataResetMigrator(
            GlobalOptions options,
            StorageQueryOptions queryOptions,
            IAnsiConsole console,
            TokenCredential credential,
            IMigrationTracker<BlobContainerClient, AssetMigrationResult> tracker,
            ILogger<StorageMetadataResetMigrator> logger) :
            base(options, console, credential)
        {
            _options = queryOptions;
            _tracker = tracker;
            _logger = logger;
        }

        public override async Task MigrateAsync(CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var (storageClient, accountId) = await _resourceProvider.GetStorageAccount(cancellationToken);
            _logger.LogInformation("Begin reset of container migration metadata for account: {name}", storageClient.AccountName);
            double totalContainers = await GetStorageBlobMetricAsync(accountId, cancellationToken);

            var status = Channel.CreateBounded<AssetStats>(1);
            var writer = status.Writer;

            var stats = new AssetStats();
            var containers = storageClient.GetBlobContainersAsync(
                prefix: _options.ContainerPrefix, cancellationToken: cancellationToken);

            var progress = ShowProgressAsync("Container metadata reset", "containers", totalContainers, status.Reader, cancellationToken);
            await MigrateInParallel(containers, async (item, cancelllationToken) =>
            {
                var container = storageClient.GetBlobContainerClient(item.Name);
                await _tracker.ResetMigrationStatus(container, cancellationToken);
                stats.Update(MigrationStatus.Success, false, false);
                await status.Writer.WriteAsync(stats, cancellationToken);
            }, _globalOptions.BatchSize, cancellationToken);

            status.Writer.Complete();
            _logger.LogInformation("Finished reset of container migration metadata for account: {name}. Time taken: {time}", storageClient.AccountName, watch.Elapsed);
            await progress;
        }
    }
}
