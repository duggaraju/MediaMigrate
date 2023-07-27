
namespace MediaMigrate.Contracts
{

    public record PackagingOptions : MigrationOptions
    {
        public Packager Packager { get; set; } = Packager.Shaka;

        public bool PackageSingleFile { get; set; } = false;

        const int DefaultSegmentDuration = 6;
        public int? SegmentDuration { get; set; } = DefaultSegmentDuration;

        public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), GlobalOptions.PathSuffix, $"Run{GlobalOptions.RunId}");
    }

    public record StorageOptions(string StoragePath) : PackagingOptions
    {
        public string PathTemplate { get; set; } = "${ContainerName}";

        public bool OverWrite { get; set; } = true;

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
