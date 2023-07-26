
namespace MediaMigrate.Contracts
{
    public record StorageOptions(string StoragePath) : MigrationOptions
    {
        public string PathTemplate { get; set; } = "${ContainerName}";

        public Packager Packager { get; set; } = Packager.Shaka;

        const int DefaultSegmentDuration = 6;
        public int? SegmentDuration { get; set; } = DefaultSegmentDuration;

        public bool OverWrite { get; set; } = true;

        public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), GlobalOptions.PathSuffix, $"Run{GlobalOptions.RunId}");

        public string ContainerPrefix { get; set; } = "asset-";

        public bool ShowChart { get; set; } = false;
    }

    public record AssetOptions(
        string StoragePath) : StorageOptions(StoragePath)
{
        public string[]? AssetNames { get; set; }

        public string? Filter { get; set; }

        public DateTime? CreatedAfter { get; set; }

        public DateTime? CreatedBefore { get; set; }

        public string? GetFilter()
{
            return new QueryOptions
            {
                Entities = AssetNames,
                CreatedAfter = CreatedAfter,
                CreatedBefore = CreatedBefore,
                Filter = Filter
            }.GetFilter();
        }
    }
}
