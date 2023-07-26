using MediaMigrate.Ams;
using MediaMigrate.Contracts;

namespace MediaMigrate.Commands
{
    internal class AssetResetCommand : BaseCommand<QueryOptions, AssetMetadataResetMigrator>
    {
        public AssetResetCommand() : base("reset", "Reset asset migration metadata")
        {
            this.AddQueryOptions();
        }
    }

    internal class StorageResetCommand : BaseCommand<StorageQueryOptions, StorageMetadataResetMigrator>
    {
        public StorageResetCommand() : base("reset", "Reset asset migration metadata")
        {
            this.AddStorageQueryOptions();
        }
    }
}
