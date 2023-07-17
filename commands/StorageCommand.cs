using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using System.CommandLine;

namespace MediaMigrate.Commands
{
    class StorageCommand : BaseCommand<StorageOptions, StorageMigrator>
    {
        static readonly Option<string> _containerPrefix = new Option<string>(
            new[] { "-p", "--prefix" },
            () => "asset-",
            description: "The prefix for container names to filter")
        {
            IsRequired = false,
        };

        private static readonly Option<string> _pathTemplate = new(
    aliases: new[] { "--path-template", "-t" },
    () => "${AssetId}/",
    description: @"Path template to determine the final path in the storage where files are uploaded.
Can use ${ContainerName}.
e.g., videos/${ContainerName} will upload to a container named 'videos' with path begining with the asset name.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };


        const string CommandDescription = @"Directly migrate the assets from the storage account.
Doesn't require the Azure media services to be running.";

        public StorageCommand() : base("storage", CommandDescription)
        {
            _pathTemplate.AddValidator(result =>
            {
                var value = result.GetValueOrDefault<string>();
                if (!string.IsNullOrEmpty(value))
                {
                    var (ok, key) = TemplateMapper.Validate(value, TemplateType.Containers);
                    if (!ok)
                    {
                        result.ErrorMessage = $"Invalid template: {value}. Template key '{key}' is invalid.";
                    }
                }
            });

            this.AddMigrationOptions()
                .AddStorageOptions(_pathTemplate)
                .AddOption(_containerPrefix);
        }
    }
}
