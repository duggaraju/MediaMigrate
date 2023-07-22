
using System.Net.Http.Headers;

namespace MediaMigrate.Contracts
{
    public interface IUploader
    {
        public Task<IFileUploader> GetUploaderAsync(
            string container,
            CancellationToken cancellationToken);

        Uri GetDestinationUri(string container, string fileName);
    }

    public record struct ContentHeaders(string? ContentType, string? ContentLanguage);

    public interface IFileUploader
    {
        Task UploadAsync(
            string fileName,
            Stream content,
            ContentHeaders headers,
            IProgress<long> progress,
            CancellationToken cancellationToken);
    }
}
