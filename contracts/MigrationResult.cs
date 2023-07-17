
namespace MediaMigrate.Contracts
{
    class MigrationResult
    {
        public MigrationResult(MigrationStatus status)
        {
            Status = status;
        }

        public string? ManifestName { get; set; }

        public string? Format { get; set; }

        public MigrationStatus Status { get; set; }

        public static implicit operator MigrationResult(MigrationStatus status)
            => new MigrationResult(status);
    }

    class AssetMigrationResult : MigrationResult
    {
        public Uri? Uri { get; set; }

        public AssetMigrationResult(
            MigrationStatus status = MigrationStatus.Skipped, Uri? uri = null, string? manifestName = null) : base(status)
        {
            Uri = uri;
            ManifestName = manifestName;
        }
    }
}
