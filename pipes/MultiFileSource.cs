using MediaMigrate.Contracts;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Pipes
{
    // A stream of media track that is spread across multiple files.
    public class MultiFileSource : IPipeSource
    {
        private readonly BlobContainerClient _container;
        private readonly ILogger _logger;
        private readonly MediaStream _track;
        private readonly string _trackPrefix;

        public MultiFileSource(
            BlobContainerClient container,
            Track track,
            ClientManifest manifest,
            ILogger logger)
        {
            _container = container;
            _logger = logger;
            (_track, _) = manifest.GetStream(track);
            _trackPrefix = track.Source;
        }

        public async Task WriteAsync(Stream stream, CancellationToken cancellationToken)
        {
            string? chunkName = null;
            try
            {
                _logger.LogDebug("Begin downloading track: {name}", _trackPrefix);
                chunkName = $"{_trackPrefix}/header";
                var blob = _container.GetBlockBlobClient(chunkName);
                await blob.DownloadToAsync(stream, cancellationToken);

                // Report progress every 10%.
                var i = 0;
                var increment = _track.ChunkCount / 10;
                foreach (var chunk in _track.GetChunks())
                {
                    ++i;
                    if (i % increment == 0)
                    {
                        _logger.LogDebug("Downloaded {i} / {total} blobs for track {stream}", i, _track.ChunkCount, _trackPrefix);
                    }

                    chunkName = $"{_trackPrefix}/{chunk}";
                    blob = _container.GetBlockBlobClient(chunkName);
                    if (await blob.ExistsAsync(cancellationToken))
                    {
                        _logger.LogTrace("Downloading Chunk for stream: {name} time={time}", _trackPrefix, chunk);
                        await blob.DownloadToAsync(stream, cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("Missing Chunk at time {time} for stream {stream}. Ignoring gap by skipping to next.", chunk, _trackPrefix);
                    }
                }
                _logger.LogDebug("Finished downloading track {prefix}", _trackPrefix);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to download chunk {chunkName} for live stream: {name}. Error: {ex}", chunkName, _trackPrefix, ex);
                throw;
            }
        }

        // Multi file streams are always smooth/cmaf.
        public string GetStreamArguments() => "-f mp4";
    }
}
