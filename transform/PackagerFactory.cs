using MediaMigrate.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Transform
{
    internal class PackagerFactory : IPackagerFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly TransMuxer _transMuxer;

        public PackagerFactory(ILoggerFactory factory, TransMuxer transMuxer)
        {
            _transMuxer = transMuxer;
            _loggerFactory = factory;
        }

        public IPackager GetPackager(StorageOptions options, Manifest? manifest)
        {
            BasePackager packager;
            if (options.Packager == Packager.Ffmpeg)
            {
                packager = new FfmpegPackager(options, _transMuxer, _loggerFactory.CreateLogger<FfmpegPackager>());
            }
            else
            {
                packager = new ShakaPackager(options, _transMuxer, _loggerFactory.CreateLogger<ShakaPackager>());
            }
            return packager;
        }
    }
}
