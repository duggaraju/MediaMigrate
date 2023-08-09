using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using Azure.ResourceManager.Media;
using Azure.ResourceManager.Media.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Transform
{
    static class StorageExtensions
    {
        public const string MigratedBlobName = "__migrate";
        public const int PAGE_SIZE = 1024;

        // AMS specific files that can be excluded since they are of no use outside AMS.
        public static readonly string[] ExcludedFiles =
        {
            ".ism",
            ".ismc",
            ".ismx",
            ".mpi"
        };

        public static async Task<BlobItem[]?> LookupManifestBlobsAsync(
            this BlobContainerClient container,
            CancellationToken cancellationToken)
        {
            var pages = container.GetBlobsByHierarchyAsync(prefix: string.Empty, delimiter: "/", cancellationToken: cancellationToken)
                .AsPages(pageSizeHint: PAGE_SIZE);
            await foreach (var page in pages)
            {
                var manifests = page.Values
                    .Where(p => p.IsBlob && p.Blob.Name.EndsWith(".ism", StringComparison.InvariantCultureIgnoreCase))
                    .Select(p => p.Blob)
                    .ToArray();
                if (manifests.Length != 0)
                {
                    return manifests;
                }
            }
            return null;
        }

        public static async Task<string?> LookupManifestFileAsync(
            this BlobContainerClient container,
            CancellationToken cancellationToken)
        {
            var blobs = await container.LookupManifestBlobsAsync(cancellationToken);
            return blobs?[0].Name;
        }

        public static async Task<Manifest?> LookupManifestAsync(
            this BlobContainerClient container,
            string? name,
            ILogger logger,
            CancellationToken cancellationToken,
            StorageEncryptedAssetDecryptionInfo? encryptionInfo = null)
        {
            var type = name == null ? "container" : "asset";
            var manifests = await container.LookupManifestBlobsAsync(cancellationToken);

            Manifest? manifest = null;
            if (manifests == null || manifests.Length == 0)
            {
                logger.LogWarning("No manifest (.ism file) found in {type} {name}",
                type, name ?? container.Name);
            }
            else
            {
                if (manifests.Length > 1)
                {
                    logger.LogWarning(
                    "Multiple manifsets (.ism) present in {type} {name}. Only processing the first one {manifest}",
                    type, name ?? container.Name, manifests[0].Name);
                }

                manifest = await GetManifestAsync(container, manifests[0].Name, logger, cancellationToken, encryptionInfo);
                logger.LogTrace("Found manifest {name} of format {format} in {type} {name}",
                manifest.FileName, manifest.Format, type, name ?? container.Name);
            }

            return manifest;
        }

        public static async Task<ClientManifest> GetClientManifestAsync(this BlobContainerClient container, Manifest manifest, ILogger logger, CancellationToken cancellationToken)
        {
            if (manifest.ClientManifest == null) 
                throw new ArgumentException("No client manifest found", nameof(manifest));
            var blob = container.GetBlockBlobClient(manifest.ClientManifest);
            logger.LogDebug("Getting client manifest {manifest} for asset", manifest.ClientManifest);
            using BlobDownloadStreamingResult result = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return ClientManifest.Parse(result.Content, blob.Name, logger);
        }

        public static async Task<Manifest> GetManifestAsync(
            this BlobContainerClient container,
            string manifestName,
            ILogger logger,
            CancellationToken cancellationToken,
            StorageEncryptedAssetDecryptionInfo? encryptionInfo = null)
        {
            var blobClient = container.GetBlockBlobClient(manifestName);

            using BlobDownloadStreamingResult result =
                await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var content = result.Content;
            Manifest manifest;
            if (encryptionInfo != null)
            {
                using var decryptor = new Decryptor(encryptionInfo);
                content = decryptor.GetDecryptingReadStream(content, manifestName);
                manifest = Manifest.Parse(content, manifestName, logger);
            }
            else
            {
                manifest = Manifest.Parse(content, manifestName, logger);
            }

            return manifest;
        }

        public static async Task<IEnumerable<BlockBlobClient>> GetListOfBlobsAsync(
            this BlobContainerClient container,
            CancellationToken cancellationToken,
            Manifest? manifest = null)
        {
            // If manifest is present use tracks from manifest.
            if (manifest != null)
            {
                return manifest.Body.Tracks
                    .Select(track => track.Source)
                    .Distinct() // select distinct names since single file can have multiple tracks and hence repeated in .ism
                    .Select(track => container.GetBlockBlobClient(track));
            }

            return await GetListOfBlobsAsync(container, cancellationToken);
        }

        public static async Task<IEnumerable<BlockBlobClient>> GetListOfBlobsAsync(
            this BlobContainerClient container,
            CancellationToken cancellationToken)
        {
            // list the blobs from the storage.
            var pages = container.GetBlobsByHierarchyAsync(delimiter: "/", cancellationToken: cancellationToken).AsPages();
            await foreach (var page in pages)
            {
                return page.Values
                    .Where(b => b.IsBlob && !ExcludedFiles.Contains(Path.GetExtension(b.Blob.Name)))
                    .Where(b => b.Blob.Name != MigratedBlobName)
                    .Select(b => container.GetBlockBlobClient(b.Blob.Name));
            }

            return Array.Empty<BlockBlobClient>();
        }

        /// <summary>
        /// List of remaining blobs not specified in the manifest.
        /// </summary>
        public static async Task<IEnumerable<BlockBlobClient>> GetListOfBlobsRemainingAsync(
            this BlobContainerClient container,
            Manifest manifest,
            CancellationToken cancellationToken)
        {
            var blobs = await GetListOfBlobsAsync(container, cancellationToken);
            return blobs.Where(blob => !manifest.Tracks.Any(t => t.Source == blob.Name));
        }

        public static async Task<AssetDetails> GetDetailsAsync(
            this MediaAssetResource asset,
            ILogger logger,
            CancellationToken cancellationToken,
            StorageEncryptedAssetDecryptionInfo? encryptionInfo = null,
            bool includeClientManifest = true)
        {
            var container = await asset.GetContainerAsync(cancellationToken);
            return await container.GetDetailsAsync(
                logger, cancellationToken, asset.Data.Name, encryptionInfo, includeClientManifest);
        }

        public static async Task<AssetDetails> GetDetailsAsync(
            this BlobContainerClient container,
            ILogger logger,
            CancellationToken cancellationToken,
            string? name = null,
            StorageEncryptedAssetDecryptionInfo? encryptionInfo = null,
            bool includeClientManifest = true)
        {
            var manifest = await container.LookupManifestAsync(name, logger, cancellationToken, encryptionInfo);
            ClientManifest? clientManifest = null;
            if (manifest != null && includeClientManifest && manifest.IsLiveArchive)
            {
                clientManifest = await container.GetClientManifestAsync(manifest, logger, cancellationToken);
            }
            return new AssetDetails(name ?? container.Name, container, null, manifest, clientManifest);
        }
    }
}
