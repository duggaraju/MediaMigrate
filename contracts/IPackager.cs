using MediaMigrate.Transform;

namespace MediaMigrate.Contracts
{
    internal interface IPackager
    {
        public Task RunAsync(
            AssetDetails assetDetails,
            string workingDirectory,
            IFileUploader fileUploader,
            CancellationToken cancellationToken);
    }

    interface IPackagerFactory
    {
        IPackager GetPackager(StorageOptions options, Manifest? manifest);
    }
}
