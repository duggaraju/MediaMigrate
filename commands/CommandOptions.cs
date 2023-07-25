using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace MediaMigrate.Commands
{
    static class CommandOptions
    {
        private static readonly Option<DateOnly?> _createdBefore = new(
            aliases: new[] { "--created-before" },
            description: @"Query entities created before given date.");

        private static readonly Option<DateOnly?> _createdAfter = new(
            aliases: new[] { "--created-after" },
            description: @"Query entities created after given date.");

        private static readonly Option<string?> _filter = new Option<string?>(
            aliases: new[] { "--filter", "-f" },
            description: @"An ODATA condition to filter the resources.
e.g.: ""name eq 'asset1'"" to match an asset with name 'asset1'.
Visit https://learn.microsoft.com/en-us/azure/media-services/latest/filter-order-page-entities-how-to for more information.");

        public static Command AddQueryOptions(this Command command)
        {
            command.AddOption(_createdAfter);
            command.AddOption(_createdBefore);
            command.AddOption(_filter);
            return command;
        }

        private static readonly Option<string> _storageAccount = new(
            aliases: new[] { "--storage-path", "-o" },
            description: @"The output storage account or path to upload the migrated assets.
This is specific to the cloud you are migrating to.
e.g: For Azure specify the storage account name or the URL <https://accountname.blob.core.windows.net>")
        {
            IsRequired = true,
            Arity = ArgumentArity.ExactlyOne
        };

        private static readonly Option<bool> _overwrite = new(
            aliases: new[] { "-y", "--overwrite" },
            () => false,
            description: @"Overwrite the files in the destination.");

        public static Command AddStorageOptions(this Command command, Option<string> pathTemplate)
        {
            command.AddOption(_storageAccount);
            command.AddOption(_overwrite);
            command.AddOption(pathTemplate);
            return command;
        }

        private static readonly Option<Packager> _packagerType = new(
            aliases: new[] { "--packager" },
            () => Packager.Shaka,
            description: "The packager to use.")
        {
            IsRequired = false
        };

        private static readonly Option<string?> _workingDirectory = new(
            aliases: new[] { "-w", "--working-directory" },
            () => Path.Combine(Path.GetTempPath(), "MediaMigrate"),
            description: @"The working directory to use for migration.")
        {
            IsRequired = false
        };

        private static readonly Option<bool> _markComplete = new(
            aliases: new[] { "-m", "--mark-complete" },
            () => true,
            description: @"Mark completed assets by writing metadata on the container");

        private static readonly Option<bool> _skipMigrated = new(
            aliases: new[] { "--skip-migrated" },
            () => true,
            description: @"Skip assets that have been migrated already.");

        private static readonly Option<bool> _copyNonStreamable = new(
            aliases: new[] { "--copy-nonstreamable" },
            () => true,
            description: @"Copy non-stremable assets (Assets without .ism file) as is.");

        private static readonly Option<bool> _deleteMigrated = new(
            aliases: new[] { "--delete-migrated" },
            () => false,
            description: @"Delete the asset after migration.");

        private static readonly Option<int> _batchSize = new(
            aliases: new[] { "--batch-size", "-b" },
            () => GlobalOptions.DefaultBatchSize,
            description: @"Batch size for parallel processing.");

        const int DefaultSegmentDurationInSeconds = 6;

        public static Command AddMigrationOptions(this Command command)
        {
            command.AddOption(_markComplete);
            command.AddOption(_skipMigrated);
            command.AddOption(_packagerType);
            command.AddOption(_workingDirectory);
            command.AddOption(_copyNonStreamable);
            command.AddOption(_batchSize);
            return command;
        }

        private static readonly Option<LogLevel> _logLevel = new Option<LogLevel>(
            aliases: new[] { "--log-level", "-v" },
#if DEBUG
            getDefaultValue: () => LogLevel.Debug,
#else
            getDefaultValue: () => LogLevel.Warning,
#endif
            description: "The log level for logging"
            );

        private static readonly Option<string?> _logDirectory = new Option<string?>(
            aliases: new[] { "-l", "--log-directory" },
            getDefaultValue: () => Path.GetTempPath(),
            description: @"The directory where the logs are written. Defaults to the temporary directory"
            );

        private static readonly Option<bool> _daemonMode = new Option<bool>(
            aliases: new[] { "-d", "--daemon-mode" },
            getDefaultValue: () => false,
            description: @"Run in daemon mode and do not use ASCII graphics."
            );

        private static readonly Option<string> _subscription = new Option<string>(
            aliases: new[] { "--subscription", "-s" },
            description: "The azure subscription to use")
        {
            IsRequired = true,
            Arity = ArgumentArity.ExactlyOne
        };

        private static readonly Option<string> _resourceGroup = new Option<string>(
            aliases: new[] { "--resource-group", "-g" },
            description: "The resource group of the account beging migrated")
        {
            IsRequired = true,
            Arity = ArgumentArity.ExactlyOne
        };

        private static readonly Option<string> _accoutName = new Option<string>(
            aliases: new[] { "--account-name", "-n" },
            description: @"The target Azure Media Services or Azure Storage Account being migrated.")
        {
            IsRequired = true,
            Arity = ArgumentArity.ExactlyOne
        };

        private static readonly Option<CloudType> _cloudType = new Option<CloudType>(
            aliases: new[] { "--cloud-type", "-c" },
            () => CloudType.Azure,
            description: @"The destination cloud you are migrating to.
For Azure refer to https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet
For AWS refer to https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/creds-locate.html
For GCP refer to https://cloud.google.com/docs/authentication/application-default-credentials
Depending on the type of authentcation you may have to set some environment variables.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        public static Command AddGlobalOptions(this Command command)
        {
            command.AddGlobalOption(_logLevel);
            command.AddGlobalOption(_logDirectory);
            command.AddGlobalOption(_subscription);
            command.AddGlobalOption(_resourceGroup);
            command.AddGlobalOption(_accoutName);
            command.AddGlobalOption(_cloudType);
            command.AddGlobalOption(_daemonMode);
            command.AddBatchOption();
            return command;
        }

        public static Command AddBatchOption(this Command command)
        {
            _batchSize.AddValidator(result =>
            {
                var value = result.GetValueOrDefault<int>();
                if (value < 1 || value > 10)
                {
                    result.ErrorMessage = "Invalid batch size. Only values [1..10] are supported";
                }
            });

            command.AddOption(_batchSize);
            return command;
        }
    }
}
