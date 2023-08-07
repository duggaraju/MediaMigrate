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

        private async Task<BinaryData?> DownloadChunkAsync(long chunk, CancellationToken cancellationToken)
        {
            var chunkName = $"{_trackPrefix}/{chunk}";
            var blob = _container.GetBlockBlobClient(chunkName);
            if (await blob.ExistsAsync(cancellationToken))
            {
                _logger.LogTrace("Downloading Chunk for stream: {name} time={time}", _trackPrefix, chunk);
                var result = await blob.DownloadContentAsync(cancellationToken);
                return result.Value.Content;
            }
            else
            {
                _logger.LogWarning("Missing Chunk at time {time} for stream {stream}. Ignoring gap by skipping to next.", chunk, _trackPrefix);
                return null;
            }
        }

        private async Task DownloadChunkToAsync(long chunk, Stream stream, CancellationToken cancellationToken)
        {
            var chunkName = $"{_trackPrefix}/{chunk}";
            var blob = _container.GetBlockBlobClient(chunkName);
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

        public async Task WriteAsync(Stream stream, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Begin downloading track: {name}", _trackPrefix);
            var header = $"{_trackPrefix}/header";
            var blob = _container.GetBlockBlobClient(header);
            await blob.DownloadToAsync(stream, cancellationToken);

            // Report progress every 10%.
            var increment = _track.ChunkCount / 10;
            var currentIncrement = 0;
            var i = 0;
            foreach (var chunks in _track.GetChunks().Chunk(5))
            {
                var data = await Task.WhenAll(chunks.Select(async c => await DownloadChunkAsync(c, cancellationToken)));
                i += data.Length;
                if (i >= currentIncrement)
                {
                    _logger.LogDebug("Downloaded {i}/{total} blobs for track {stream}", i, _track.ChunkCount, _trackPrefix);
                    currentIncrement += increment;
                }

                foreach (var d in data.Where(d => d != null))
                {
                    await stream.WriteAsync(d!.ToMemory(), cancellationToken);
                }
            }
            _logger.LogDebug("Finished downloading track {prefix}", _trackPrefix);
        }

        // Multi file streams are always smooth/cmaf.
        public string GetStreamArguments() => "-f mp4";
    }
}
