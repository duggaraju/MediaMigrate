using Azure.ResourceManager.Media.Models;
using Azure.ResourceManager.Media;
using Azure.Storage.Blobs;
using Azure;
using System.Diagnostics;

namespace MediaMigrate.Ams
{
    static class AmsExtensions
    {
        public static async Task<BlobContainerClient> GetContainerAsync(this MediaAssetResource asset, CancellationToken cancellationToken)
        {
            var content = new MediaAssetStorageContainerSasContent
            {
                Permissions = MediaAssetContainerPermission.ReadWriteDelete,
                ExpireOn = DateTimeOffset.Now.AddHours(1)
            };

            var uris = asset.GetStorageContainerUrisAsync(content, cancellationToken).AsPages();
            var urls = new List<Uri>();
            await foreach (var page in uris)
            {
                urls.AddRange(page.Values);
            }

            return new BlobContainerClient(urls[0]);
        }

        public static BlobContainerClient GetContainer(this BlobServiceClient storage, MediaAssetResource asset)
        {
            return storage.GetBlobContainerClient(asset.Data.Container);
        }

        public static async Task<StreamingLocatorResource?> GetStreamingLocatorAsync(
            this MediaServicesAccountResource account,
            MediaAssetResource asset,
            CancellationToken cancellationToken)
        {
            var locators = asset.GetStreamingLocatorsAsync(cancellationToken);
            await foreach (var locatorData in locators)
            {
                return await account.GetStreamingLocatorAsync(locatorData.Name, cancellationToken);
            }
            return null;
        }

        public static async Task<string> GetStreamingEndpointAsync(
            this MediaServicesAccountResource account,
            string endpointName = "default",
            CancellationToken cancellationToken = default)
        {
            StreamingEndpointResource endpoint = await account.GetStreamingEndpointAsync(endpointName, cancellationToken);
            return endpoint.Data.HostName;
        }

        public static async Task CreateStreamingLocator(
            this MediaServicesAccountResource account,
            MediaAssetResource asset,
            CancellationToken cancellationToken)
        {
            await account.GetStreamingPolicies().CreateOrUpdateAsync(WaitUntil.Completed, "migration", new StreamingPolicyData
            {
                NoEncryptionEnabledProtocols = new MediaEnabledProtocols(
                    isDashEnabled: true,
                    isHlsEnabled: true,
                    isDownloadEnabled: false,
                    isSmoothStreamingEnabled: false)
            });
            await account.GetStreamingLocators().CreateOrUpdateAsync(WaitUntil.Completed, "migration", new StreamingLocatorData
            {
                AssetName = asset.Data.Name,
                StreamingPolicyName = "migration"
            });
        }

        static readonly StreamingPolicyStreamingProtocol Protocol = StreamingPolicyStreamingProtocol.Hls;

        public static async Task<Uri?> GetStreamingUrlAsync(
            this MediaServicesAccountResource account,
            MediaAssetResource asset,
            CancellationToken cancellationToken)
        {
            var hostName = await account.GetHostNameAsync(cancellationToken);
            var locator = await account.GetStreamingLocatorAsync(asset, cancellationToken);
            if (locator == null)
            {
                return null;
            }

            StreamingPathsResult pathResult = await locator.GetStreamingPathsAsync(cancellationToken);
            var path = pathResult.StreamingPaths.SingleOrDefault(p => p.StreamingProtocol == Protocol);
            if (path == null)
            {
                Trace.TraceWarning("The locator {locator} has no HLS streaming support.", locator.Id);
                return null;
            }
            var uri = new UriBuilder("https", _hostName)
            {
                Path = path.Paths[0] + ".m3u8"
            }.Uri;
            return uri;
        }

        private static string? _hostName = null;
        public static async Task<string> GetHostNameAsync(this MediaServicesAccountResource account, CancellationToken cancellationToken)
        {
            if (_hostName == null)
            {
                _hostName = await account.GetStreamingEndpointAsync(cancellationToken: cancellationToken);
            }
            return _hostName;
        }
    }
}
