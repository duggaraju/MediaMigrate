using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using System.CommandLine;

namespace MediaMigrate.Commands
{
    class StorageCommand : BaseCommand<StorageOptions, StorageMigrator>
    {
        private static readonly Option<string> _pathTemplate = new(
            aliases: new[] { "--path-template", "-t" },
            () => "${ContainerName}/",
            description: @"Path template to determine the final path in the storage where files are uploaded.
Can use ${ContainerName}.
e.g., videos/${ContainerName} will upload to a container named 'videos' with path beginning with the asset name.");


        const string CommandDescription = @"Directly migrate the assets from the storage account.
Doesn't require the Azure media services to be running.";

        public StorageCommand() : base("storage", CommandDescription)
        {
            AddValidator(result =>
            {
                var value = result.GetValueForOption(_pathTemplate);
                if (!string.IsNullOrEmpty(value))
                {
                    var (ok, key) = TemplateMapper.Validate(value, TemplateType.Container);
                    if (!ok)
                    {
                        result.ErrorMessage = $"Invalid template: {value}. Template key '{key}' is invalid.";
                    }
                }
            });

            this.AddMigrationOptions()
                .AddPackagingOptions()
                .AddStorageOptions()
                .AddStorageQueryOptions()
                .AddOptions(_pathTemplate)
                .AddBatchOption()
                .AddCommand(new StorageResetCommand());
        }
    }
}
