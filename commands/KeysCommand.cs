using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using System.CommandLine;

namespace MediaMigrate.Commands
{
    internal class KeysCommand : BaseCommand<KeyOptions, KeysMigrator>
    {
        private static readonly Option<Uri> _keyVaultUri = new Option<Uri>(
            aliases: new[] { "--key-vault-url", "-k" },
            description: @"The vault for migrating keys.
Specific to the cloud you are migrating to.
For Azure it is <https://valutname.azure.net>")
        {
            IsRequired = true,
            Arity = ArgumentArity.ExactlyOne
        };

        private static readonly Option<string?> _secretTemplate = new Option<string?>(
            aliases: new[] { "--secret-template", "-t" },
            () => "${KeyId}",
            description: @"Template for the name in the vault with which the key is stored.
Can use ${KeyId} ${KeyName} in the template.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        private static readonly Option<int> _batchSize = new(
            aliases: new[] { "--batch-size", "-b" },
            () => 1,
            description: @"Batch size for processing.");

        public KeysCommand() : base("keys", "Migrate asset encryption keys")
        {
            _secretTemplate.AddValidator(result =>
            {
                var value = result.GetValueOrDefault<string>();
                if (!string.IsNullOrEmpty(value))
                {
                    var (ok, key) = TemplateMapper.Validate(value, TemplateType.Keys);
                    if (!ok)
                    {
                        result.ErrorMessage = $"Invalid template: {value}. Template key '{key}' is invalid.";
                    }
                }
            });
            AddOption(_keyVaultUri);
            AddOption(_secretTemplate);
            AddOption(_batchSize);
            this.AddQueryOptions();
        }
    }
}
