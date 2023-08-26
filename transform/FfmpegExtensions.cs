using FFMpegCore.Arguments;
using FFMpegCore;
using MediaMigrate.Log;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;

namespace MediaMigrate.Transform
{
    static class FfmpegExtensions
    {
        private static string GetCustomArguments(TransMuxOptions options)
        {
            var arguments = string.Empty; //"-avoid_negative_ts make_zero";
            if (options.Cmaf)
                arguments += "-movflags cmaf+delay_moov+skip_trailer+skip_sidx+frag_keyframe  -frag_duration 2";
            else if (options.FastStart)
                arguments += "-movflags faststart";
            return arguments;
        }

        public static FFMpegArguments SetVerbosity(this FFMpegArguments args, VerbosityLevel level = VerbosityLevel.Info)
        {
            return args.WithGlobalOptions(options => options.WithVerbosityLevel(level));
        }

        public static void AddMp4MuxingOptions(this FFMpegArgumentOptions args, TransMuxOptions options)
        {
            args
                .ForceFormat("mp4")
                .WithCustomArgument(GetCustomArguments(options));
            if (options.Channel != Channel.Both)
            {
                args.SelectStream(0, channel: options.Channel);
            }
        }

        public static async Task RunAsync(this FFMpegArgumentProcessor processor, ILogger logger, CancellationToken cancellationToken)
        {
            logger.LogTrace(Events.Ffmpeg, "Running ffmpeg {args}", processor.Arguments);
            try
            {
                await processor
                    .NotifyOnOutput(line => logger.LogTrace(Events.Ffmpeg, "{line}", line))
                .NotifyOnError(line => logger.LogTrace(Events.Ffmpeg, "{line}", line))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously(throwOnError: true);
            }
            catch (Exception ex)
            {
                logger.LogError(Events.Ffmpeg, ex, "Ffmpeg command failed!");
                throw;
            }
        }

        public static async Task<IMediaAnalysis> AnalyzeAsync(this Uri uri, CancellationToken cancellationToken)
        {
            return await FFProbe.AnalyseAsync(uri, null, cancellationToken);
        }
    }

}
