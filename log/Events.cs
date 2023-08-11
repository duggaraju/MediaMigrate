
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Log
{
    public static class Events
    {
        public static readonly EventId MediaServices = new(10, "MediaServices");
        public static readonly EventId ShakaPackager = new (11, "ShakaPackager");
        public static readonly EventId Ffmpeg = new (12, "Ffmpeg");
        public static readonly EventId BlobDownload = new (13, "BlobDownload");
        public static readonly EventId BlobUpload = new (14, "BlobUpload");
        public static readonly EventId TransMuxer = new (15, "TransMuxer");
        public static readonly EventId AzureKeyVault = new (16, "AzureKeyVault");
        public static readonly EventId Pipes = new (17, "Pipes");
        public static readonly EventId Failure = new (18, "Failure");
    }
}
