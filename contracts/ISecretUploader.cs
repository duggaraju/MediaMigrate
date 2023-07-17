namespace MediaMigrate.Contracts
{
    internal interface ISecretUploader
    {
        Task UploadAsync(
            string secretName,
            string secretValue,
            IDictionary<string, string> metadata,
            CancellationToken cancellationToken);
    }
}
