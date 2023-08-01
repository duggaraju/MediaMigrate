using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using System.CommandLine;

namespace MediaMigrate.Commands
{
    internal class KeysCommand : BaseCommand<KeyOptions, KeysMigrator>
    {
        private static readonly Option<Uri> _keyVaultUri = new(
            aliases: new[] { "--key-vault-url", "-k" },
            description: @"The vault for migrating keys.
Specific to the cloud you are migrating to.
For Azure it is <https://valutname.azure.net>")
        {
            IsRequired = true,
        };

        private static readonly Option<string> _secretTemplate = new(
            aliases: new[] { "--secret-template", "-t" },
            () => "${AssetId}",
            description: @"Template for the name in the vault with which the key is stored.
Can use ${KeyId} ${KeyName} in the template.");

        public KeysCommand() : base("keys", "Migrate asset encryption keys")
        {
            AddValidator(result =>
            {
                var value = result.GetValueForOption(_secretTemplate)!;
                var (ok, key) = TemplateMapper.Validate(value, TemplateType.Key);
                if (!ok)
                {
                    result.ErrorMessage = $"Invalid template: {value}. Template key '{key}' is invalid.";
                }
            });
            this.AddQueryOptions();
            AddOption(_keyVaultUri);
            AddOption(_secretTemplate);
        }
    }
}
