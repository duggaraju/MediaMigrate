using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using Azure.ResourceManager.Media;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Transform
{
    internal class TransformFactory
    {
        private readonly ICloudProvider _cloudProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly PackagerFactory _packagerFactory;
        private readonly TemplateMapper _templateMapper;

        public TransformFactory(
            ILoggerFactory loggerFactory,
            TemplateMapper templateMapper,
            PackagerFactory packagerFactory,
            ICloudProvider cloudProvider)
        {
            _loggerFactory = loggerFactory;
            _cloudProvider = cloudProvider;
            _packagerFactory = packagerFactory;
            _templateMapper = templateMapper;
        }

        public IEnumerable<StorageTransform> GetStorageTransforms(StorageOptions options)
        {
            var uploader = _cloudProvider.GetStorageProvider(options);
            if (options.Packager != Packager.None)
            {
                yield return new PackageTransform(
                    options,
                    _loggerFactory.CreateLogger<PackageTransform>(),
                    _templateMapper,
                    _cloudProvider,
                    _packagerFactory);
            }
            if (options.CopyNonStreamable || options.Packager == Packager.None)
            {
                yield return new UploadTransform(
                    options,
                    _cloudProvider,
                    _loggerFactory.CreateLogger<UploadTransform>(),
                    _templateMapper);
            }
        }

        public IEnumerable<AssetTransform> GetAssetTransforms(AssetOptions options)
        {
            return GetStorageTransforms(options)
                .Select(t => new AssetTransform(options, _templateMapper, t, _loggerFactory.CreateLogger<AssetTransform>()));
        }

        public ITransform<MediaTransformResource> TransformTransform => throw new NotImplementedException();
    }
}
