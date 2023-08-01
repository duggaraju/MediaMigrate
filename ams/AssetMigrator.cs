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
using MediaMigrate.Log;

namespace MediaMigrate.Ams
{
    internal class AssetMigrator : BaseMigrator
    {
        private readonly ILogger _logger;
        private readonly TransformFactory _transformFactory;
        private readonly AssetOptions _options;
        private readonly IMigrationTracker<BlobContainerClient, AssetMigrationResult> _tracker;
        private readonly ICloudProvider _cloudProvider;

        public AssetMigrator(
            GlobalOptions globalOptions,
            AssetOptions assetOptions,
            IAnsiConsole console,
            TokenCredential credential,
            IMigrationTracker<BlobContainerClient, AssetMigrationResult> tracker,
            ILogger<AssetMigrator> logger,
            TransformFactory transformFactory,
            ICloudProvider cloudProvider) :
            base(globalOptions, console, credential)
        {
            _options = assetOptions;
            _tracker = tracker;
            _logger = logger;
            _transformFactory = transformFactory;
            _cloudProvider = cloudProvider;
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

            var status = Channel.CreateBounded<AssetStats>(1);
            var progress = ShowProgressAsync("Asset Migration", "Assets", totalAssets, status.Reader, cancellationToken);

            var stats = await MigrateAsync(account, status.Writer, cancellationToken);
            _logger.LogInformation("Finished migration of assets for account: {name}. Time taken: {time}", account.Data.Name, watch.Elapsed);
            await progress;
            WriteSummary(ref stats);
        }

        private async Task<AssetStats> MigrateAsync(MediaServicesAccountResource account, ChannelWriter<AssetStats> writer, CancellationToken cancellationToken)
        {
            var stats = new AssetStats();
            var orderBy = "properties/created";
            var assets = account.GetMediaAssets()
                .GetAllAsync(_options.GetFilter(), orderby: orderBy, cancellationToken: cancellationToken);

            await MigrateInParallel(assets, async (asset, cancellationToken) =>
            {
                var result = await MigrateAsync(account, asset, cancellationToken);
                stats.Update(result, asset.Data.StorageEncryptionFormat, _options.DeleteMigrated);
                await writer.WriteAsync(stats, cancellationToken);
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

        public async Task<AssetMigrationResult> MigrateAsync(
            MediaServicesAccountResource account,
            MediaAssetResource asset,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Migrating asset: {name} ...", asset.Data.Name);
            var container = await _resourceProvider.GetStorageContainerAsync(account, asset, cancellationToken);
            if (_options.SkipMigrated)
            {
                var status = await _tracker.GetMigrationStatusAsync(container, cancellationToken);
                if (status.Status == MigrationStatus.Success)
                {
                    _logger.LogDebug("Asset: {name} has already been migrated.", asset.Data.Name);
                    return status;
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
                    result = await transform.RunAsync(record, cancellationToken);
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
                _logger.LogError("Failed to migrate asset {name}. {message}", asset.Data.Name, ex.Message);
                _logger.LogTrace(Events.Failure, ex, "Failed to migrate asset {name}.", asset.Data.Name);
                return MigrationStatus.Failure;
            }
        }
    }
}

