namespace MediaMigrate.Contracts
{
    public record KeyOptions(
        Uri KeyVaultUrl,
        int BatchSize) : QueryOptions
    {
        public string? KeyTemplate { get; set; }
    }
}
