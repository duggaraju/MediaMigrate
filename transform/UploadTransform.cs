﻿using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Transform
{
    internal class UploadTransform : StorageTransform
    {
        public UploadTransform(
            StorageOptions options,
            IUploader uploader,
            ILogger<UploadTransform> logger,
            TemplateMapper templateMapper) :
            base(options, templateMapper, uploader, logger)
        { 
        }

        // simple upload is supported except for live/live archive assets.
        protected override Task<bool> IsSupportedAsync(AssetDetails details, CancellationToken cancellationToken)
            => Task.FromResult(details.Manifest == null || !(details.Manifest.IsLive || details.Manifest.IsLiveArchive));

        protected override async Task<string> TransformAsync(
            AssetDetails details,
            (string Container, string Prefix) outputPath,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Uploading files from asset {asset} ", details.AssetName);
            var fileUploader = await _uploader.GetUploaderAsync(outputPath.Container, cancellationToken);
            var (assetName, inputContainer, _, manifest, _) = details;
            var inputBlobs = await inputContainer.GetListOfBlobsAsync(cancellationToken, manifest);
            using var decryptor = details.DecryptionInfo != null ? new Decryptor(details.DecryptionInfo) : null;
            var uploads = inputBlobs.Select(blob => UploadBlobAsync(blob, decryptor, fileUploader, outputPath.Prefix, cancellationToken));
            await Task.WhenAll(uploads);
            return outputPath.Prefix;
        }
    }
}
