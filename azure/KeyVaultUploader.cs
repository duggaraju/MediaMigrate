using MediaMigrate.Contracts;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Azure
{
    internal class KeyVaultUploader : ISecretUploader
    {
        private readonly ILogger _logger;
        private readonly SecretClient _secretClient;
        private readonly KeyOptions _keyOptions;

        public KeyVaultUploader(
            KeyOptions options,
            TokenCredential credential,
            ILogger<KeyVaultUploader> logger)
        {
            _keyOptions = options;
            _logger = logger;
            _secretClient = new SecretClient(options.KeyVaultUrl, credential);
        }

        public async Task UploadAsync(
            string secretName,
            string secretValue,
            IDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Saving secret {name} to key valut {vault}", secretName, _keyOptions.KeyVaultUrl);
            var secret = new KeyVaultSecret(secretName, secretValue);
            foreach (var (name, value) in metadata)
            {
                secret.Properties.Tags.Add(name, value);
                await _secretClient.SetSecretAsync(secret, cancellationToken);
            }
        }
    }
}
