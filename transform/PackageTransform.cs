using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Transform
{
    internal class PackageTransform : StorageTransform
    {
        private readonly PackagerFactory _packagerFactory;

        public PackageTransform(
            StorageOptions options,
            ILogger<PackageTransform> logger,
            TemplateMapper templateMapper,
            IUploader uploader,
            PackagerFactory factory)
            : base(options, templateMapper, uploader, logger)
        {
            _packagerFactory = factory;
        }

        // If manifest is present then we can package it.
        protected override bool IsSupported(AssetDetails details)
        {
            if (details.Manifest == null)
            {
                _logger.LogDebug("Packaging asset {asset} without manifest is not supported!", details.AssetName);
                return false;
            }

            if (details.DecryptionInfo != null)
            {
                _logger.LogWarning("Packaging encrypted asset {asset} is not supported!", details.AssetName);
                return false;
            }

            if (details.ClientManifest != null && details.ClientManifest.HasDiscontinuities())
            {
                _logger.LogWarning("Packaging asset {asset}, which is a live archive with discontinuities is not supported!", details.AssetName);
                return false;
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

            var fileUploader = await _uploader.GetUploaderAsync(outputPath.Container, cancellationToken);
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
                    await UploadBlobAsync(blob, decryptor, fileUploader, outputPath.Prefix, cancellationToken);
                }));

                var package = packager.RunAsync(details, workingDirectory, fileUploader, outputPath, cancellationToken);
                await Task.WhenAll(upload, package);
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
