﻿using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using FFMpegCore.Pipes;
using MediaMigrate.Log;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Pipes
{
    internal class BlobSource : IPipeSource
    {
        private static readonly IReadOnlyDictionary<string, string> ExtensionToFormatMap = new Dictionary<string, string>
        {
            { ".ts", "mpegts" },
            { ".vtt", "webvtt" }
        };

        private readonly BlockBlobClient _blobClient;
        private readonly ILogger _logger;

        public BlobSource(BlockBlobClient blobClient, ILogger logger)
        {
            _blobClient = blobClient;
            _logger = logger;
        }

        public BlobSource(BlobContainerClient container, string file, ILogger logger)
            : this(container.GetBlockBlobClient(file), logger)
        {
        }

        public string GetStreamArguments()
        {
            var extension = Path.GetExtension(_blobClient.Name);
            if (!ExtensionToFormatMap.TryGetValue(extension, out var format))
            {
                format = "mp4"; // fallback to mp4.
            }
            return $"-f {format}";
        }

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Begin downloading track: {name}", _blobClient.Name);
            try
            {
                BlobProperties properties = await _blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                // report update every 10%.
                long update = 0;
                const int UpdatePercentage = 10;
                var options = new BlobDownloadOptions
                {
                    ProgressHandler = new Progress<long>(
                    progress =>
                    {
                        // report progress for every 10%.
                        var percentage = progress * 100 / properties.ContentLength;
                        if (percentage >= update)
                        {
                            _logger.LogTrace(Events.BlobDownload, "Downloaded {percentage}% ({bytes}/{total}) of blob {blob}", percentage, progress, properties.ContentLength, _blobClient.Name);
                            update += UpdatePercentage;
                        }
                    })
                };
                using BlobDownloadStreamingResult result = await _blobClient.DownloadStreamingAsync(options, cancellationToken);
                await result.Content.CopyToAsync(outputStream, cancellationToken);
                _logger.LogDebug("Finished downloading track: {name}", _blobClient.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download {blob}", _blobClient.Name);
                throw;
            }
        }

        public async Task DownloadAsync(string filePath, CancellationToken cancellationToken)
        {
            await _blobClient.DownloadToAsync(filePath, cancellationToken);
        }
    }
}