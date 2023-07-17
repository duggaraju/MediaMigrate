namespace MediaMigrate.Contracts
{
    public record MigrationOptions
    {
        public bool MarkCompleted { get; set; } = true;

        public bool SkipMigrated { get; set; } = true;

        public bool DeleteMigrated { get; set; } = false;

        public int BatchSize { get; set; } = 1;

        public bool CopyNonStreamable { get; set; } = true;
    }
}
