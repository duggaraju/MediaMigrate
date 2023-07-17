
namespace MediaMigrate.Contracts
{
    interface ICloudProvider
    {
        IUploader GetStorageProvider(StorageOptions assetOptions);

        ISecretUploader GetSecretProvider(KeyOptions keyOptions);
    }
}
