﻿using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace MediaMigrate.Transform
{
    internal class PackageTransform : StorageTransform
    {
        private readonly PackagerFactory _packagerFactory;

        public PackageTransform(
            StorageOptions options,
            ILogger<PackageTransform> logger,
            TemplateMapper templateMapper,
            ICloudProvider cloudProvider,
            PackagerFactory factory)
            : base(options, templateMapper, cloudProvider, logger)
        {
            _packagerFactory = factory;
        }

        // If manifest is present then we can package it.
        protected override async Task<bool> IsSupportedAsync(AssetDetails details, CancellationToken cancellationToken)
        {
            if (details.Manifest == null)
            {
                if  (_options.PackageSingleFile)
                {
                    var blobs = await details.Container.GetListOfBlobsAsync(cancellationToken);
                    return blobs.Count() == 1;
                }
                _logger.LogDebug("Packaging of asset {asset} without manifest (.ism) is not supported!", details.AssetName);
                return false;
            }

            if (details.ClientManifest != null)
            {
                if (details.ClientManifest.Streams.Any(s => s.Type == StreamType.Audio && s.HasDiscontinuities()))
                {
                    _logger.LogWarning("Audio stream in asset {asset} has discontinuities and will be transcoded to fill silence.", details.AssetName);
                }
            }

            return true;
        }

        protected override async Task<string> TransformAsync(
            AssetDetails details,
            (string Container, string Prefix) outputPath,
            CancellationToken cancellationToken = default)
        {
            var (assetName, container, _, manifest, _) = details;
            if (manifest == null) throw new ArgumentNullException(nameof(details));

            var fileUploader = await _uploader.GetUploaderAsync(outputPath.Container, outputPath.Prefix, cancellationToken);
            // temporary space for either pipes or files.
            var workingDirectory = Path.Combine(_options.WorkingDirectory, assetName);
            Directory.CreateDirectory(workingDirectory);

            var packager = _packagerFactory.GetPackager(_options, manifest);
            try
            {
                var decryptor = details.DecryptionInfo == null ? null : new Decryptor(details.DecryptionInfo);
                // Anything not package and can be uploaded is uploaded directly.
                var blobs = await container.GetListOfBlobsRemainingAsync(manifest, cancellationToken);
                var upload = Task.WhenAll(blobs.Select(async blob =>
                {
                    await UploadBlobAsync(blob, decryptor, fileUploader, cancellationToken);
                }));

                if (_options.EncryptContent)
                {
                    details.KeyId = Guid.NewGuid().ToString("n");
                    details.LicenseUrl = _templateMapper.ExpandKeyUriTemplate(_options.KeyUri!, details.KeyId);
                    var key= new byte[16];
                    new Random().NextBytes(key);
                    details.EncryptionKey = Convert.ToHexString(key);
                }

                var package = packager.RunAsync(details, workingDirectory, fileUploader, cancellationToken);
                await Task.WhenAll(upload, package);

                if (details.KeyId != null && details.EncryptionKey != null)
                {
                    var secrets = _cloudProvider.GetSecretProvider(_options.KeyOptions);
                    await secrets.UploadAsync(
                        details.KeyId, details.EncryptionKey,
                        ImmutableDictionary<string, string>.Empty, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate asset {name}", assetName);
                throw;
            }
            finally
            {
                Directory.Delete(workingDirectory, true);
            }

            return $"{outputPath.Prefix}{Path.GetFileNameWithoutExtension(manifest.FileName)}";
        }
    }
}
