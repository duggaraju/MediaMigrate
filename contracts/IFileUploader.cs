
namespace MediaMigrate.Contracts
{
    public interface IUploader
    {
        public Task<IFileUploader> GetUploaderAsync(
            string container,
            CancellationToken cancellationToken);

        Uri GetDestinationUri(string container, string fileName);
    }

    public interface IFileUploader
    {
        Task UploadAsync(
            string fileName,
            Stream content,
            IProgress<long> progress,
            CancellationToken cancellationToken);
    }
}
