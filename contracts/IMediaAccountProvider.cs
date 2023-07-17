using Azure.ResourceManager.Media;

namespace MediaMigrate.contracts
{
    internal interface IMediaAccountProvider
    {
        Task<MediaServicesAccountResource> GetMediaAccountAsync(CancellationToken cancellationToken);
    }
}
