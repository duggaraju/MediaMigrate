using Microsoft.Extensions.Logging;

namespace MediaMigrate.Contracts
{
    public record GlobalOptions(
        string Subscription,
        string ResourceGroup,
        string AccountName)
    {
        private readonly string _logFile = $"MigrationLog_{DateTime.Now:HH_mm_ss}.txt";

        public string LogFile => Path.Combine(LogDirectory, _logFile);

        public CloudType CloudType { get; set; } = CloudType.Azure;

        public string LogDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "MediaMigrate");

        public LogLevel LogLevel { get; set; } = LogLevel.Warning;

        public bool DaemonMode { get; set; } = false;
    }
}

