using MediaMigrate.Contracts;
using MediaMigrate.Transform;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Media;
using Azure.ResourceManager.Media.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Threading.Channels;

namespace MediaMigrate.Ams
{
    record struct AssetStats()
    {
        private int _total = default;
        private int _encrypted = default;
        private int _streamable = default;
        private int _successful = default;
        private int _migrated = default;
        private int _skipped = default;
        private int _failed = default;
        private int _deleted = default;

        public readonly int Total => _total;

        public readonly int Encrypted => _encrypted;

        public readonly int Streamable => _streamable;

        public readonly int Successful => _successful;

        public readonly int Failed => _failed;

        public readonly int Skipped => _skipped;

        public readonly int Migrated => _migrated;

        public readonly int Deleted => _deleted;

        public void Update(MigrationResult result, bool deleteMigrated, MediaAssetStorageEncryptionFormat? format = null)
        {
            Interlocked.Increment(ref _total);
            if (format != MediaAssetStorageEncryptionFormat.None)
            {
                Interlocked.Increment(ref _encrypted);
            }
            if (result.Format != null)
            {
                Interlocked.Increment(ref _streamable);
            }
            switch (result.Status)
            {
                case MigrationStatus.Success:
                    Interlocked.Increment(ref _successful);
                    if (deleteMigrated)
                    {
                        Interlocked.Increment(ref _deleted);
                    }
                    break;

                case MigrationStatus.Skipped:
                    Interlocked.Increment(ref _skipped);
                    break;
                case MigrationStatus.AlreadyMigrated:
                    Interlocked.Increment(ref _migrated);
                    break;
                case MigrationStatus.Failure:
                    Interlocked.Increment(ref _failed);
                    break;
            }
        }
    }

    internal class AssetMigrator : BaseMigrator
    {
        private readonly ILogger _logger;
        private readonly TransformFactory _transformFactory;
        private readonly AssetOptions _options;
        private readonly IMigrationTracker<BlobContainerClient, AssetMigrationResult> _tracker;

        public AssetMigrator(
            GlobalOptions globalOptions,
            AssetOptions assetOptions,
            IAnsiConsole console,
            TokenCredential credential,
            IMigrationTracker<BlobContainerClient, AssetMigrationResult> tracker,
            ILogger<AssetMigrator> logger,
            TransformFactory transformFactory) :
            base(globalOptions, console, credential)
        {
            _options = assetOptions;
            _tracker = tracker;
            _logger = logger;
            _transformFactory = transformFactory;
        }

        public override async Task MigrateAsync(CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var account = await GetMediaAccountAsync(cancellationToken);
            _logger.LogInformation("Begin migration of assets for account: {name}", account.Data.Name);
            var totalAssets = await QueryMetricAsync(
account.Id.ToString(),
"AssetCount",
cancellationToken: cancellationToken);

            var status = Channel.CreateBounded<double>(1);
            var progress = ShowProgressAsync("Asset Migration", "Assets", totalAssets, status.Reader, cancellationToken);

            var stats = await MigrateAsync(account, status.Writer, cancellationToken);
            _logger.LogInformation("Finished migration of assets for account: {name}. Time taken: {time}", account.Data.Name, watch.Elapsed);
            await progress;
            WriteSummary(ref stats);
        }

        private async Task<AssetStats> MigrateAsync(MediaServicesAccountResource account, ChannelWriter<double> writer, CancellationToken cancellationToken)
        {
            var storage = await _resourceProvider.GetStorageAccountAsync(account, cancellationToken);
            var stats = new AssetStats();
            var orderBy = "properties/created";
            var assets = account.GetMediaAssets()
                .GetAllAsync(_options.GetFilter(), orderby: orderBy, cancellationToken: cancellationToken);

            await MigrateInParallel(assets, async (asset, cancellationToken) =>
            {
                var result = await MigrateAsync(account, storage, asset, cancellationToken);
                stats.Update(result, _options.DeleteMigrated, asset.Data.StorageEncryptionFormat);
                await writer.WriteAsync(stats.Total, cancellationToken);
            }, _globalOptions.BatchSize, cancellationToken);

            writer.Complete();
            return stats;
        }

        private void WriteSummary(ref AssetStats stats)
        {
            var table = new Table()
                .AddColumn("Assets")
                .AddColumn("Count")
                .AddRow("Total", $"{stats.Total}")
                .AddRow("[orange3]Encrypted[/]", $"[orange3]{stats.Encrypted}[/]")
                .AddRow("Streamable", $"{stats.Streamable}")
                .AddRow("[green]Already Migrated[/]", $"[green]{stats.Migrated}[/]")
                .AddRow("[gray]Skiped[/]", $"[gray]{stats.Skipped}[/]")
                .AddRow("[green]Successful[/]", $"[green]{stats.Successful}[/]")
                .AddRow("[red]Failed[/]", $"[red]{stats.Failed}[/]")
                .AddRow("[darkorange3]Deleted[/]", $"[darkorange3]{stats.Deleted}[/]");
            _console.Write(table);
        }

        public async Task<MigrationResult> MigrateAsync(
            MediaServicesAccountResource account,
            BlobServiceClient storage,
            MediaAssetResource asset,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Migrating asset: {name} ...", asset.Data.Name);
            var container = storage.GetContainer(asset);
            if (_options.SkipMigrated)
            {
                var status = await _tracker.GetMigrationStatusAsync(container, cancellationToken);
                if (status.Status == MigrationStatus.Success)
                {
                    _logger.LogDebug("Asset: {name} has already been migrated.", asset.Data.Name);
                    return MigrationStatus.AlreadyMigrated;
                }
            }

            try
            {
                var result = new AssetMigrationResult();
                StorageEncryptedAssetDecryptionInfo? info = null;
                if (asset.Data.StorageEncryptionFormat != MediaAssetStorageEncryptionFormat.None)
                {
                    _logger.LogInformation("Asset {name} is encrypted.", asset.Data.Name);
                    try
                    {
                        info = await asset.GetEncryptionKeyAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get the encryption key for asset {name}", asset.Data.Name);
                        result.Status = MigrationStatus.Failure;
                        return result;
                    }
                }

                var details = await asset.GetDetailsAsync(_logger, cancellationToken, info);
                _logger.LogTrace("Asset {asset} is in format: {format}.", asset.Data.Name, details.Manifest?.Format);
                result.Format = details.Manifest?.Format;

                var record = new AssetRecord(account, asset, info, details);
                foreach (var transform in _transformFactory.GetAssetTransforms(_options))
                {
                    result = (AssetMigrationResult)await transform.RunAsync(record, cancellationToken);
                    if (result.Status != MigrationStatus.Skipped)
                    {
                        break;
                    }
                }

                if (result.Status == MigrationStatus.Skipped)
                {
                    _logger.LogWarning("Skipping asset {name} because it is not in a supported format!!!", asset.Data.Name);
                }
                if (_options.MarkCompleted)
                {
                    await _tracker.UpdateMigrationStatus(container, result, cancellationToken);
                }
                if (_options.DeleteMigrated && result.Status == MigrationStatus.Success)
                {

                    _logger.LogWarning("Deleting asset {name} after migration", asset.Data.Name);
                    await asset.DeleteAsync(WaitUntil.Completed, cancellationToken);
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate asset {name}.", asset.Data.Name);
                return MigrationStatus.Failure;
            }
        }
    }
}

