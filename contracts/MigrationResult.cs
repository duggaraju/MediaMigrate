
namespace MediaMigrate.Contracts
{
    class MigrationResult
    {
        public MigrationResult(MigrationStatus status)
        {
            Status = status;
        }

        public MigrationStatus Status { get; set; }

        public static implicit operator MigrationResult(MigrationStatus status) => new(status);
    }

    class AssetMigrationResult : MigrationResult
    {
        public Uri? Uri { get; set; }

        public string? ManifestName { get; set; }

        public string? Format { get; set; }

        public AssetMigrationResult(
            MigrationStatus status = MigrationStatus.Skipped,
            Uri? uri = null,
            string? manifestName = null) : base(status)
        {
            Uri = uri;
            ManifestName = manifestName;
        }

        public static implicit operator AssetMigrationResult(MigrationStatus status) => new(status);
    }
}
