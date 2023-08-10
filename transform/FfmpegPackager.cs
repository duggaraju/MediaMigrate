using MediaMigrate.Contracts;
using MediaMigrate.Log;
using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Transform
{
    internal class FfmpegPackager : BasePackager, IPackager
    {
        public FfmpegPackager(
            StorageOptions options,
            TransMuxer transMuxer,
            ILogger<FfmpegPackager> logger)
            : base(options, transMuxer, logger)
        {
        }

        protected override FileType GetInputFileType(Manifest manifest)
        {
            // MP4 cannot be read over a pipe.
            return manifest.Format.StartsWith("mp4") || manifest.Format.Equals("fmp4") ? FileType.File : base.GetInputFileType(manifest);
        }

        public override async Task<bool> PackageAsync(
            AssetDetails assetDetails,
            string workingDirectory,
            IList<PackagerInput> inputFiles,
            IList<PackagerOutput> outputFiles,
            IList<PackagerOutput> manifests,
            CancellationToken cancellationToken = default)
        {
            var manifest = assetDetails.Manifest!;
            try
            {
                GlobalFFOptions.Configure(options => options.WorkingDirectory = workingDirectory);
                var args = FFMpegArguments.FromFileInput(inputFiles[0].FilePath, verifyExists: false);
                foreach (var file in inputFiles.Skip(1))
                {
                    args.AddFileInput(file.FilePath, verifyExists: false);
                }

                args.WithGlobalOptions(options => options.WithVerbosityLevel(FFMpegCore.Arguments.VerbosityLevel.Debug));
                var manifestName = Path.GetFileNameWithoutExtension(manifest.FileName);
                var dash = manifests[manifests.Count - 1].FilePath;
                var hls = manifests[manifests.Count - 2].FilePath;
                var processor = args.OutputToFile(dash, overwrite: true, options =>
                {
                    var i = 0;
                    foreach (var input in inputFiles)
                    {
                        foreach (var track in input.Tracks)
                        {
                            var ext = track.IsMultiFile ? MEDIA_FILE : string.Empty;
                            options.SelectStream(0, i, track is VideoTrack ? Channel.Video : Channel.Audio);

                            ++i;


                        }
                    }
                    options
                    .CopyChannel()
                    .ForceFormat("dash")
                    .WithCustomArgument($"-single_file 1 -single_file_name {manifestName}_$RepresentationID$.mp4 -hls_playlist 1 -hls_master_name {hls} -use_timeline 1");
                    if (manifest.Format == "m3u8-aapl")
                    {
                        options.WithCustomArgument("-vtag avc1 -atag mp4a");
                    }
                });
                _logger.LogDebug("Starting Ffmpeg with args {args}", args.Text);
                var result = await processor
                    .CancellableThrough(cancellationToken)
                    .NotifyOnOutput(line => _logger.LogDebug(Events.Ffmpeg, "{line}", line))
                    .NotifyOnError(line => _logger.LogDebug(Events.Ffmpeg, "{line}", line))
                    .ProcessAsynchronously(throwOnError: true);
                _logger.LogDebug("Ffmpeg process completed successfully");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ffmpeg command failed.");
                throw;
            }
        }

        protected override List<PackagerOutput> GetManifests(Manifest manifest, IList<PackagerOutput> outputs)
        {
            var fileType = GetManifestFileType(manifest);
            var manifestName = Path.GetFileNameWithoutExtension(manifest.FileName);
            var manifestFiles = outputs
                .Select((t, i) => $"media_{i}{HLS_MANIFEST}")
                .ToList();
                manifestFiles.Add($"{manifestName}{HLS_MANIFEST}");
                manifestFiles.Add($"{manifestName}{DASH_MANIFEST}");

            return manifestFiles
                .Select((f, i) => new PackagerOutput(f, fileType, i == manifestFiles.Count -1 ? DASH_CONTENT_TYPE : HLS_CONTENT_TYPE, null))
                .ToList();
        }
    }
}
