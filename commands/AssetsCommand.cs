using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using System.CommandLine;

namespace MediaMigrate.Commands
{
    internal class AssetsCommand : BaseCommand<AssetOptions, AssetMigrator>
    {
        private readonly Option<string[]> _assetNames = new(
            aliases: new[] { "--asset-names", "-a" },
            description: @"Select specific asset(s) by name. You can specify multiple assets")
        {
            AllowMultipleArgumentsPerToken = true
        };

        private static readonly Option<string> _pathTemplate = new(
            aliases: new[] { "--path-template", "-t" },
            () => "${AssetId}/",
            description: @"Path template to determine the final path in the storage where files are uploaded.
Can use ${AssetName} ${AssetId} ${ContainerName} or ${LocatorId}.
e.g., videos/${AssetName} will upload to a container named 'videos' with path beginning with the asset name.");

        const string CommandDescription = @"Migrate Assets
Examples:
mediamigrate assets -s <subscription id> -g <resource group> -n <account name> -a <storage account> -t path-template
This migrates the assets to a different storage account in your subscription.";

        public AssetsCommand() : base("assets", CommandDescription)
        {
            AddValidator(result =>
            {
                var value = result.GetValueForOption(_pathTemplate)!;
                var (ok, key) = TemplateMapper.Validate(value, TemplateType.Asset);
                if (!ok)
                {
                    result.ErrorMessage = $"Invalid template: {value}. Template key '{key}' is invalid.";
                }
            });

            this.AddQueryOptions()
                .AddMigrationOptions()
                .AddPackagingOptions()
                .AddStorageOptions()
                .AddOptions(_pathTemplate, _assetNames)
                .AddCommand(new AssetResetCommand());
        }
    }
}
