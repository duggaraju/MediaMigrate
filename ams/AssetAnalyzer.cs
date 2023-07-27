using MediaMigrate.Contracts;
using MediaMigrate.Report;
using MediaMigrate.Transform;
using Azure.Core;
using Azure.ResourceManager.Media;
using Azure.ResourceManager.Media.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Threading.Channels;
using System.Collections.Concurrent;

namespace MediaMigrate.Ams
{
    public record struct AnalysisResult(
        string AssetName,
        MediaAssetStorageEncryptionFormat? Encryption,
        MigrationStatus Status,
        string? Format,
        Uri? Uri,
        string? ManifestName)
    {
        public AnalysisResult(MediaAssetResource asset) :
            this(asset.Data.Name, asset.Data.StorageEncryptionFormat, MigrationStatus.NotMigrated, null, null, null)
        {}
    }

    internal class AssetAnalyzer : BaseMigrator
    {
        const string StyleSheetName = "style.css";
        private readonly ILogger _logger;
        private readonly AnalysisOptions _analysisOptions;
        private readonly IMigrationTracker<BlobContainerClient, AssetMigrationResult> _tracker;

        public AssetAnalyzer(
            GlobalOptions globalOptions,
            AnalysisOptions analysisOptions,
            IAnsiConsole console,
            IMigrationTracker<BlobContainerClient, AssetMigrationResult> tracker,
            TokenCredential credential,
            ILogger<AssetAnalyzer> logger)
            : base(globalOptions, console, credential)
        {
            _analysisOptions = analysisOptions;
            _tracker = tracker;
            _logger = logger;
        }

        private async Task<AnalysisResult> AnalyzeAsync(MediaServicesAccountResource account, MediaAssetResource asset, CancellationToken cancellationToken)
        {
            var result = new AnalysisResult(asset);
            _logger.LogDebug("Analyzing asset: {asset}", asset.Data.Name);
            try
            {
                var container = await _resourceProvider.GetStorageContainerAsync(account, asset, cancellationToken);
                if (!await container.ExistsAsync(cancellationToken))
                {

                    _logger.LogWarning("Container {name} missing for asset {asset}", container.Name, asset.Data.Name);
                    result.Status = MigrationStatus.Failure;
                    return result;
                }

                var migrationStatus = await _tracker.GetMigrationStatusAsync(container, cancellationToken);
                result.Status = migrationStatus.Status == MigrationStatus.Success ? MigrationStatus.AlreadyMigrated : migrationStatus.Status;
                result.Uri = migrationStatus.Uri;

                if (_analysisOptions.AnalysisType == AnalysisType.Detailed)
                {
                    StorageEncryptedAssetDecryptionInfo? info = null;
                    if (asset.Data.StorageEncryptionFormat != MediaAssetStorageEncryptionFormat.None)
                    {
                        info = await asset.GetEncryptionKeyAsync(cancellationToken);
                    }
                    var assetDetails = await container.GetDetailsAsync(
                        _logger, cancellationToken, asset.Data.Name, info, false);
                    result.Format = assetDetails.Manifest?.Format;
                }
                else
                {
                    var hasManifest = await container.LookupManifestFileAsync(cancellationToken);
                    result.Format = hasManifest == null ? null : string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze asset {name}", asset.Data.Name);
                result.Status = MigrationStatus.Failure;
            }
            return result;
        }

        private static void AggregateResult(AnalysisResult result, AssetStats statistics, ConcurrentDictionary<string, int> assetTypes)
        {
            statistics.Update(result.Status, result.Format != null, false, result.Encryption);
            var format = string.IsNullOrEmpty(result.Format) ? "unknown" : result.Format!;
            assetTypes.AddOrUpdate(format, 1, (format, count) => Interlocked.Increment(ref count));
        }

        public override async Task MigrateAsync(CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();

            var assetsByYear = new ConcurrentDictionary<int, int>();

            _logger.LogInformation("Begin analysis of assets for account: {name}", _globalOptions.AccountName);
            var account = await GetMediaAccountAsync(cancellationToken);
            double totalAssets = await QueryMetricAsync(account.Id.ToString(), "AssetCount", cancellationToken);
            ReportGenerator? reportGenerator = null;

            if (_analysisOptions.AnalysisType == AnalysisType.Report)
            {
                var filename = Path.Combine(_globalOptions.LogDirectory, _analysisOptions.ReportFileName);
                var file = File.OpenWrite(filename);
                var styleSheet = Path.Combine(_globalOptions.LogDirectory, StyleSheetName);
                reportGenerator = new ReportGenerator(filename, file);
                if (!File.Exists(styleSheet))
                {
                    await reportGenerator.WriteStyleSheetAsync(styleSheet);
                }
                reportGenerator.WriteHeader(StyleSheetName);
            }
            var assets = account.GetMediaAssets()
                .GetAllAsync(_analysisOptions.GetFilter(), cancellationToken: cancellationToken);
            var statistics = new AssetStats();
            var assetTypes = new ConcurrentDictionary<string, int>();

            var channel = Channel.CreateBounded<AssetStats>(1);
            var progress = ShowProgressAsync("Analyzing Assets", "Assets", totalAssets, channel.Reader, cancellationToken);
            var writer = channel.Writer;
            var currentYear = DateTimeOffset.Now.Year;
            await MigrateInParallel(assets, async (asset, cancellationToken) =>
            {
                var year = asset.Data.CreatedOn?.Year ?? currentYear;
                assetsByYear.AddOrUpdate(year, 1, (key, value) => Interlocked.Increment(ref value));
                var result = await AnalyzeAsync(account, asset, cancellationToken);
                AggregateResult(result, statistics, assetTypes);
                await writer.WriteAsync(statistics, cancellationToken);
                reportGenerator?.WriteRow(result);
            }, _analysisOptions.BatchSize, cancellationToken);

            writer.Complete();
            await progress;
            _logger.LogDebug("Finished analysis of assets for account: {name}. Time taken {elapsed}", _globalOptions.AccountName, watch.Elapsed);
            WriteSummary(statistics);
            if (_analysisOptions.AnalysisType == AnalysisType.Detailed)
            {
                WriteDetails(assetTypes, assetsByYear);
            }

            if (reportGenerator != null)
            {
                reportGenerator.WriteTrailer();
                reportGenerator.Dispose();
                _logger.LogInformation("Summary report written to {file}", reportGenerator.FileName);
            }
        }

        private void WriteSummary(AssetStats statistics)
        {
            var table = new Table()
                .Title("[yellow]Asset Summary[/]")
                .HideHeaders()
                .AddColumn(string.Empty)
                .AddColumn(string.Empty)
                .AddRow("[yellow]Total[/]", $"{statistics.Total}")
                .AddRow("[darkgreen]Streamable[/]", $"{statistics.Streamable}")
                .AddRow("[grey]Encrypted[/]", $"{statistics.Encrypted}")
                .AddRow("[grey]Not Migrated[/]", $"{statistics.NotMigrated}")
                .AddRow("[green]Migrated[/]", $"{statistics.Migrated}")
                .AddRow("[red]Failed[/]", $"{statistics.Failed}")
                .AddRow("[darkorange]Skipped[/]", $"{statistics.Skipped}");
            _console.Write(table);
        }

        private void WriteDetails(IDictionary<string, int> assetTypes, IDictionary<int, int> assetsByYear)
        {
            var dates = new Table()
                .Title("[yellow]Assets by Year[/]")
                .AddColumn("Year")
                .AddColumn("Count");
            foreach (var (key, value) in assetsByYear)
            {
                dates.AddRow(
                Markup.FromInterpolated($"[darkorange]{key}[/]"),
                Markup.FromInterpolated($"{value}"));
            }
            _console.Write(dates);

            var formats = new Table()
                .Title("[yellow]Asset Formats[/]")
                .AddColumn("Format")
                .AddColumn("Count");
            foreach (var (key, value) in assetTypes)
            {
                formats.AddRow($"[green]{key}[/]", $"[grey]{value}[/]");
            }
            _console.Write(formats);
        }
    }
}
