using Microsoft.Extensions.Logging;

namespace MediaMigrate.Contracts
{
    public record GlobalOptions(
        string Subscription,
        string ResourceGroup,
        string AccountName)
    {
        public const int DefaultBatchSize = 5;
        public readonly string RunId = $"{DateTime.Now:HH_mm_ss}";

        public string LogFile => Path.Combine(LogDirectory, $"MigrationLog_{RunId}.txt");

        public CloudType CloudType { get; set; } = CloudType.Azure;

        public string LogDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "MediaMigrate");

        public LogLevel LogLevel { get; set; } = LogLevel.Warning;

        public bool DaemonMode { get; set; } = false;

        public int BatchSize { get; set; } = DefaultBatchSize;
    }
}

