using Microsoft.Extensions.Logging;

namespace MediaMigrate.Contracts
{
    public record GlobalOptions(
        string Subscription,
        string ResourceGroup,
        string AccountName)
    {
        public const int DefaultBatchSize = 5;
        public const string PathSuffix = "MediaMigrate";


        // The Run ID for the current run.
        public static readonly string RunId = $"{DateTime.Now:HH_mm_ss}";
        private string _logDirectory = Path.Combine(Path.GetTempPath(), PathSuffix);

        public string LogFile => Path.Combine(LogDirectory, $"MigrationLog_{RunId}.txt");

        public CloudType CloudType { get; set; } = CloudType.Azure;

        public string LogDirectory 
        { 
            get
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);
                return _logDirectory;
            }
            set => _logDirectory = value; 
        }

        public LogLevel LogLevel { get; set; } = LogLevel.Warning;

        public bool DaemonMode { get; set; } = false;

        public int BatchSize { get; set; } = DefaultBatchSize;
    }
}

