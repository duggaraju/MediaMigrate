using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace MediaMigrate.Commands
{
    static class CommandOptions
    {
        public static Command AddOptions(this Command command, params Option[] options)
        {
            Array.ForEach(options, option => command.AddOption(option));
            return command;
        }

        #region AssetQueryOptions
        private static readonly Option<DateTime?> _createdBefore = new(
            aliases: new[] { "--created-before" },
            description: @"Query entities created before given date.");

        private static readonly Option<DateTime?> _createdAfter = new(
            aliases: new[] { "--created-after" },
            description: @"Query entities created after given date.");

        private static readonly Option<string?> _filter = new(
            aliases: new[] { "--filter", "-f" },
            description: @"An ODATA condition to filter the resources.
e.g.: ""name eq 'asset1'"" to match an asset with name 'asset1'.
Visit https://learn.microsoft.com/en-us/azure/media-services/latest/filter-order-page-entities-how-to for more information.");

        public static Command AddQueryOptions(this Command command)
        {
            command.AddValidator(result =>
            {
                if (result.FindResultFor(_filter) != null && 
                result.FindResultFor(_createdAfter) != null || result.FindResultFor(_createdBefore) != null)
                {
                    result.ErrorMessage = $"Conflicting Options. Both date range and filter cannot be specified together. Specify only one.";
                }
            });
            return command.AddOptions(_createdAfter, _createdBefore, _filter);
        }
        #endregion

        #region StorageQueryOptions
        static readonly Option<string> _containerPrefix = new(
            new[] { "-p", "--container-prefix" },
            () => "asset-",
            description: "The prefix for container names to filter")
        {
            IsRequired = false,
        };

        public static Command AddStorageQueryOptions(this Command command)
        {
            command.AddOption(_containerPrefix);
            return command;
        }
        #endregion

        private static readonly Option<string> _storageAccount = new(
            aliases: new[] { "--storage-path", "-o" },
            description: @"The output storage account or path to upload the migrated assets.
This is specific to the cloud you are migrating to.
e.g: For Azure specify the storage account name or the URL <https://accountname.blob.core.windows.net>")
        {
            IsRequired = true,
        };

        private static readonly Option<bool> _overwrite = new(
            aliases: new[] { "-y", "--overwrite" },
            () => false,
            description: @"Overwrite the files in the destination.");

        public static Command AddStorageOptions(this Command command)
        {
            return command.AddOptions(_storageAccount, _overwrite);
        }

        private static readonly Option<bool> _markComplete = new(
            aliases: new[] { "-m", "--mark-complete" },
            () => true,
            description: @"Mark completed assets by writing metadata on the container");

        private static readonly Option<bool> _skipMigrated = new(
            aliases: new[] { "--skip-migrated" },
            () => true,
            description: @"Skip assets that have been migrated already.");

        private static readonly Option<bool> _deleteMigrated = new(
            aliases: new[] { "--delete-migrated" },
            () => false,
            description: @"Delete the asset after migration.");

        public static Command AddMigrationOptions(this Command command)
        {
            return command.AddOptions(
                _markComplete,
                _skipMigrated,
                _deleteMigrated);
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
            description: @"The working directory to use for migration.")
        {
            IsRequired = false
        };

        private static readonly Option<bool> _copyNonStreamable = new(
            aliases: new[] { "--copy-nonstreamable" },
            () => true,
            description: @"Copy non-stremable assets (Assets without .ism file) as is.");

        private static readonly Option<bool> _encryptContent = new(
            aliases: new[] { "-e", "--encrypt-content" },
            () => false,
            description: @"Encrypt the content while packaging. The key and the key id will be saved to the vault specified.");

        const int DefaultSegmentDurationInSeconds = 6;
        private static readonly Option<int?> _segmentDuration = new(
            aliases: new[] { "--segment-duration" },
            () => DefaultSegmentDurationInSeconds,
            description: "The segment duration to use for streaming");

        private static readonly Option<Uri?> _keyVaultUri = new(
            aliases: new[] { "--key-vault-uri", "-k" },
            description: @"The vault for saving encryption keys.
    Specific to the cloud you are migrating to.
    For Azure it is <https://valutname.azure.net>");

        private static readonly Option<string> _keyUri = new(
            aliases: new[] { "--key-uri", "-u" },
            description: @"The URI for the key to put in the manifest.  This should be a template");

        private static readonly Option<bool> _usePipes = new(
            aliases: new[] { "--use-pipes", "-p" },
            () => true,
            description: @"Use pipes for storage. Default is true but can be disabled if you hit errors.
            For example MP4 content with 'moov' box at the end cannot be used with pipes");

        private static readonly Option<string> _manifestName = new(
            aliases: new[] { "--manifest-name" },
            description: @"The manifest name to use for the package content. By default the input .ism file name is used as manifest name which can be overridden by this option.");

        public static Command AddPackagingOptions(this Command command)
        {
            command.AddValidator(result =>
            {
                var encrypt = result.GetValueForOption<bool>(_encryptContent);
                if (encrypt && result.FindResultFor(_keyVaultUri) == null)
                {
                    result.ErrorMessage = "Encryption is enabled but a vault to store the keys has not been specified.";
                }

                if (encrypt && result.FindResultFor(_keyUri) == null)
                {
                    result.ErrorMessage = "Encryption is enabled but key URI is not specified.";
                }

                if (encrypt && result.GetValueForOption(_packagerType) != Packager.Shaka)
                {
                    result.ErrorMessage = "Content encryption is only supported with shaka packager";
                }

                var uri = result.GetValueForOption(_keyUri);
                if (uri != null)
                {
                    var(_, mesg) = TemplateMapper.Validate(uri, TemplateType.KeyUri, needKey: true);
                    result.ErrorMessage = mesg;
                }
            });

            command.AddOptions(
                _packagerType,
                _workingDirectory,
                _copyNonStreamable,
                _segmentDuration,
                _encryptContent,
                _keyVaultUri,
                _keyUri,
                _usePipes,
                _manifestName);
            return command;
        }

        private static readonly Option<LogLevel> _logLevel = new(
            aliases: new[] { "--log-level", "-v" },
#if DEBUG
            getDefaultValue: () => LogLevel.Debug,
#else
            getDefaultValue: () => LogLevel.Information,
#endif
            description: "The log level for logging"
            );

        private static readonly Option<string?> _logDirectory = new(
            aliases: new[] { "-l", "--log-directory" },
            description: @"The directory where the logs are written. Defaults to the temporary directory"
            );

        private static readonly Option<bool> _daemonMode = new(
            aliases: new[] { "-d", "--daemon-mode" },
            getDefaultValue: () => false,
            description: @"Run in daemon mode and do not use ASCII graphics."
            );

        private static readonly Option<string> _subscription = new(
            aliases: new[] { "--subscription", "-s" },
            description: "The azure subscription to use")
        {
            IsRequired = true,
        };

        private static readonly Option<string> _resourceGroup = new(
            aliases: new[] { "--resource-group", "-g" },
            description: "The resource group of the account beging migrated")
        {
            IsRequired = true
        };

        private static readonly Option<string> _accountName = new(
            aliases: new[] { "--account-name", "-n" },
            description: @"The target Azure Media Services or Azure Storage Account being migrated.")
        {
            IsRequired = true
        };

        private static readonly Option<CloudType> _cloudType = new(
            aliases: new[] { "--cloud-type", "-c" },
            () => CloudType.Azure,
            description: @"The destination cloud you are migrating to.
For Azure refer to https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet
For AWS refer to https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/creds-locate.html
For GCP refer to https://cloud.google.com/docs/authentication/application-default-credentials
Depending on the type of authentcation you may have to set some environment variables.");

        public static Command AddGlobalOptions(this Command command)
        {
            command.AddGlobalOption(_logLevel);
            command.AddGlobalOption(_logDirectory);
            command.AddGlobalOption(_subscription);
            command.AddGlobalOption(_resourceGroup);
            command.AddGlobalOption(_accountName);
            command.AddGlobalOption(_cloudType);
            command.AddGlobalOption(_daemonMode);
            return command;
        }

        public static Command AddBatchOption(this Command command, int defaultBatchSize = 5, int maxBatchSize = 10)
        {
            Option<int> batchSize = new(
                aliases: new[] { "--batch-size", "-b" },
                () => defaultBatchSize,
                description: @"Batch size for parallel processing.");

            command.AddValidator(result =>
            {
                var value = result.GetValueForOption(batchSize);
                if (value < 1 || value > maxBatchSize)
                {
                    result.ErrorMessage = $"Invalid batch size. Only values 0 or [1..{maxBatchSize}] are supported";
                }
            });

            command.AddOption(batchSize);
            return command;
        }
    }
}
