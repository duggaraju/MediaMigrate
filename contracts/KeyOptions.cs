namespace MediaMigrate.Contracts
{
    public record KeyOptions(
        Uri KeyVaultUrl) : QueryOptions
    {
        public string? KeyTemplate { get; set; }
    }
}
