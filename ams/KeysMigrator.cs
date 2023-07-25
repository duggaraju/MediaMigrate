using MediaMigrate.Contracts;
using Azure.Core;
using Azure.ResourceManager.Media;
using Azure.ResourceManager.Media.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Threading.Channels;

namespace MediaMigrate.Ams
{
    record struct KeyStats(int Encrypted, int Successful, int Failed)
    {
        private int _total = default;
        private int _encrypted = default;
        private int _successful = default;
        private int _failed = default;

        public readonly int Total => _total;

        public void Update(MigrationStatus result, MediaAssetStorageEncryptionFormat? format)
        {
            Interlocked.Increment(ref _total);
            if (format != MediaAssetStorageEncryptionFormat.None)
            {
                Interlocked.Increment(ref _encrypted);
            }

            switch (result)
            {
                case MigrationStatus.Failure:
                    Interlocked.Increment(ref _failed);
                    break;
                case MigrationStatus.Success:
                    Interlocked.Increment(ref _successful);
                    break;
            }
        }
    }

    internal class KeysMigrator : BaseMigrator
    {
        private readonly ILogger _logger;
        private readonly KeyOptions _keyOptions;
        private readonly ISecretUploader _secretUplaoder;
        private readonly TemplateMapper _templateMapper;

        public KeysMigrator(
            GlobalOptions globalOptions,
            KeyOptions keyOptions,
            IAnsiConsole console,
            ILogger<AccountMigrator> logger,
            TemplateMapper templateMapper,
            ICloudProvider cloudProvider,
            TokenCredential credential) :
            base(globalOptions, console, credential)
        {
            _logger = logger;
            _keyOptions = keyOptions;
            _templateMapper = templateMapper;
            _secretUplaoder = cloudProvider.GetSecretProvider(keyOptions);
        }

        public override async Task MigrateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Begin migration of keys for account: {name}", _globalOptions.AccountName);
            var account = await GetMediaAccountAsync(cancellationToken);

            var orderBy = "properties/created";
            var assets = account.GetMediaAssets()
                .GetAllAsync(_keyOptions.GetFilter(), orderby: orderBy, cancellationToken: cancellationToken);

            var stats = new KeyStats();
            var channel = Channel.CreateBounded<double>(1);
            var progress = ShowProgressAsync("Migrate encryption keys", "Assets", 1.0, channel.Reader, cancellationToken);

            await MigrateInParallel(assets, async (asset, cancellationToken) =>
            {
                var result = await MigrateAssetAsync(asset, cancellationToken);
                stats.Update(result, asset.Data.StorageEncryptionFormat);
                await channel.Writer.WriteAsync(stats.Total);
            }, _globalOptions.BatchSize, cancellationToken);

            _logger.LogInformation("Finished migration of keys for account: {name}", _globalOptions.AccountName);
            channel.Writer.Complete();
            await progress;
            WriteSummary(ref stats);
        }

        private void WriteSummary(ref KeyStats stats)
        {
            var table = new Table()
                .AddColumn("Assets Summary")
                .AddColumn("Count")
                .AddRow("Total", $"{stats.Total}")
                .AddRow("[orange3]Encrypted[/]", $"[orange3]{stats.Encrypted}[/]")
                .AddRow("[green]Successful[/]", $"[green]{stats.Successful}[/]")
                .AddRow("[red]Failed[/]", $"[red]{stats.Failed}[/]");
            _console.Write(table);
        }

        private async Task<MigrationStatus> MigrateAssetAsync(MediaAssetResource asset, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Try migrating keys for asset: {name}", asset.Data.Name);
            if (asset.Data.StorageEncryptionFormat == MediaAssetStorageEncryptionFormat.None)
            {
                return MigrationStatus.Skipped;
            }
            try
            {
                StorageEncryptedAssetDecryptionInfo encryption =
                await asset.GetEncryptionKeyAsync(cancellationToken);
                _logger.LogInformation("Migrating encryption key for asset {id}", asset.Data.Name);
                var secretName = _templateMapper.ExpandKeyTemplate(asset, _keyOptions.KeyTemplate);
                var metadata = encryption.AssetFileEncryptionMetadata.ToDictionary(info => info.AssetFileName, info => info.InitializationVector);
                await _secretUplaoder.UploadAsync(
                    secretName,
                    Convert.ToBase64String(encryption.Key),
                    metadata,
                    cancellationToken);
                return MigrationStatus.Success;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to migrate keys for asset {name}", asset.Data.Name);
                return MigrationStatus.Failure;
            }
        }

        private async Task MigrateLocatorAsync(StreamingLocatorResource locator, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Try migrating keys for locator: {locator} = {id}", locator.Data.Name, locator.Data.StreamingLocatorId);
            try
            {
                var keys = locator.GetContentKeysAsync(cancellationToken: cancellationToken);
                await foreach (var key in keys)
                {

                    _logger.LogInformation("Migrating content key {id}", key.Id);
                    var secretName = _templateMapper.ExpandKeyTemplate(key, _keyOptions.KeyTemplate);
                    await _secretUplaoder.UploadAsync(
                    secretName,
                    key.Value,
                    new Dictionary<string, string>(),
                    cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to migrate asset {name}. Error: {ex}", locator.Data.Name, ex);
            }
        }
    }
}
