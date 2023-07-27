using MediaMigrate.Contracts;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager.Media;
using Spectre.Console;
using System.Threading.Channels;

namespace MediaMigrate.Ams
{
    abstract class BaseMigrator
    {
        public const int PAGE_SIZE = 1024;

        protected readonly AzureResourceProvider _resourceProvider;
        protected readonly GlobalOptions _globalOptions;
        protected readonly MetricsQueryClient _metricsQueryClient;
        protected readonly IAnsiConsole _console;

        public BaseMigrator(
            GlobalOptions options,
            IAnsiConsole console,
            TokenCredential credential)
        {
            _globalOptions = options;
            _console = console;
            _resourceProvider = new AzureResourceProvider(credential, options);
            _metricsQueryClient = new MetricsQueryClient(credential);
        }

        public abstract Task MigrateAsync(CancellationToken cancellationToken);

        protected Task<MediaServicesAccountResource> GetMediaAccountAsync(CancellationToken cancellationToken)
        {
            return _resourceProvider.GetMediaAccountAsync(cancellationToken); ;
        }


        protected async Task MigrateInParallel<T>(
            IAsyncEnumerable<T> values,
            Func<T, CancellationToken, ValueTask> processItem,
            int batchSize = 5,
            CancellationToken cancellationToken = default)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = batchSize,
                CancellationToken = cancellationToken
            };
            await Parallel.ForEachAsync(values, cancellationToken, processItem);
        }

        protected async Task<double> GetStorageBlobMetricAsync(ResourceIdentifier accountId, CancellationToken cancellationToken)
        {
            return await QueryMetricAsync(
                $"{accountId}/blobServices/default",
                "ContainerCount",
                cancellationToken);
        }

        protected async Task<double> QueryMetricAsync(
            string resourceId,
            string metricName,
            CancellationToken cancellationToken)
        {
            var options = new MetricsQueryOptions
            {
                Granularity = TimeSpan.FromHours(1),
                TimeRange = new QueryTimeRange(TimeSpan.FromHours(6))
            };
            MetricsQueryResult queryResult = await _metricsQueryClient.QueryResourceAsync(
                resourceId,
                new[] { metricName },
                options,
                cancellationToken: cancellationToken);
            var metric = queryResult.Metrics[0];
            var series = metric.TimeSeries[metric.TimeSeries.Count - 1];
            var averageTotal = series.Values.LastOrDefault(v => v.Average != null)?.Average ?? 0.0;
            return averageTotal;
    }

        protected async Task ShowProgressAsync<T>(
            string description,
            string unit,
            double totalValue,
            ChannelReader<T> reader,
            CancellationToken cancellationToken) where T : IStats
        {
            var statusColumn = new StatusColumn(unit);
            await _console
                .Progress()
                .AutoRefresh(true)
                .AutoClear(true)
                .HideCompleted(true)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    statusColumn,
                    new PercentageColumn(),
                    new ElapsedTimeColumn(),
                    new SpinnerColumn())
            .StartAsync(async context =>
            {
                var task = context.AddTask(description, maxValue: totalValue);
                await foreach (var value in reader.ReadAllAsync(cancellationToken))
                {
                    statusColumn.Update(value);
                    if (value.Total > task.MaxValue)
                    {
                        task.MaxValue = value.Total;
                    }

                    task.Value = value.Total;
                }
            });
        }
    }
}