using MediaMigrate.Log;
using MediaMigrate.Pipes;
using CommunityToolkit.HighPerformance;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Media.ISO;
using Media.ISO.Boxes;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Text;

namespace MediaMigrate.Transform
{
    internal class TransMuxer
    {
        private readonly ILogger _logger;

        public TransMuxer(ILogger<TransMuxer> logger)
        {
            _logger = logger;
        }

        public async Task<IMediaAnalysis> AnalyzeAsync(Uri uri, CancellationToken cancellationToken)
        {
            return await FFProbe.AnalyseAsync(uri, null, cancellationToken);
        }

        private static void AddMuxingOptions(FFMpegArgumentOptions options, Channel channel, string? customArguments)
        {
            var frag = channel == Channel.Audio ? "-frag_duration 2 " : string.Empty;
            options
                .ForceFormat("mp4")
                .CopyChannel(channel)
                .WithCustomArgument(customArguments ?? string.Empty);
            if (channel != Channel.Both)
            {
                options.SelectStream(0, channel: channel);
            }
        }

        private async Task RunFfmpeg(FFMpegArgumentProcessor processor, CancellationToken cancellationToken)
        {
            _logger.LogDebug(Events.Ffmpeg, "Running ffmpeg {args}", processor.Arguments);
            await processor
                .NotifyOnOutput(line => _logger.LogTrace(Events.Ffmpeg, "{line}", line))
            .NotifyOnError(line => _logger.LogTrace(Events.Ffmpeg, "{line}", line))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously(throwOnError: true);
        }

        public async Task TransmuxUriAsync(Uri uri, MediaStream stream, string filePath, CancellationToken cancellationToken)
        {
            var processor = FFMpegArguments.FromUrlInput(uri)
            .OutputToFile(filePath, overwrite: true, options =>
            {
                options.CopyChannel()
                .SelectStream(stream.Index, 0)
                .WithCustomArgument("-movflags faststart");
            });
            await RunFfmpeg(processor, cancellationToken);
        }

        public async Task TransmuxUriAsync(Uri uri, string filePath, CancellationToken cancellationToken)
        {
            var result = await FFProbe.AnalyseAsync(uri, null, cancellationToken);
            var processor = FFMpegArguments.FromUrlInput(uri)
            .OutputToFile(filePath, overwrite: true, options =>
            {
                foreach (var stream in result.AudioStreams)
                {
                    options.SelectStream(stream.Index, 0);
                }

                foreach (var stream in result.VideoStreams)
                {
                    options.SelectStream(stream.Index, 0);
                }

                options.CopyChannel()
                    .WithCustomArgument("-movflags faststart");
            });
            await RunFfmpeg(processor, cancellationToken);
        }

        private static string GetCustomArguments(Channel channel, bool dash, bool faststart = true)
        {
            var frag = channel == Channel.Audio ? "-frag_duration 2 " : string.Empty;
            return dash ?
                "-movflags +dash+delay_moov+skip_trailer+skip_sidx+frag_keyframe " + frag :
                faststart ? "-movflags faststart" : string.Empty;
        }

        public async Task TransMuxAsync(IPipeSource source, string destination, Channel channel, CancellationToken cancellationToken, bool dash = false)
        {
            var processor = FFMpegArguments
                .FromPipeInput(source)
                .OutputToFile(destination, overwrite: true, options => AddMuxingOptions(options, channel, GetCustomArguments(channel, false, false)));
            await RunFfmpeg(processor, cancellationToken);
        }

        public async Task PipedTransMuxAsync(IPipeSource source, IPipeSink destination, Channel channel, CancellationToken cancellationToken, bool dash = true)
        {
            var processor = FFMpegArguments
                .FromPipeInput(source)
                .OutputToPipe(destination, options => AddMuxingOptions(options, channel, GetCustomArguments(channel, dash)));
            await RunFfmpeg(processor, cancellationToken);
        }

        public static async Task SkipAsync(Stream stream, int bytes, CancellationToken cancellationToken)
        {
            var memoryPool = MemoryPool<byte>.Shared;
            if (stream.CanSeek)
            {
                stream.Position += bytes;
            }
            else
            {
                using var memory = memoryPool.Rent(bytes);
                while (bytes > 0)
                {
                    bytes -= await stream.ReadAsync(memory.Memory.Slice(0, bytes), cancellationToken);
                }
            }
        }

        static readonly TimeSpan MilliSecond = TimeSpan.FromMilliseconds(1);

        public async Task TransMuxTTMLAsync(Stream input, Stream output, CancellationToken cancellationToken)
        {
            var pool = MemoryPool<byte>.Shared;
            var header = new byte[8];
            using var vttWriter = new StreamWriter(output, Encoding.UTF8)
            {
                NewLine = "\r\n"
            };
            await vttWriter.WriteLineAsync("WEBVTT");
            await vttWriter.WriteLineAsync();
            await vttWriter.WriteLineAsync();

            while (true)
            {
                var bytes = await input.ReadAsync(header, cancellationToken);
                if (bytes == 0) break;
                var box = BoxFactory.Parse(header.AsSpan());
                var size = (int)box.Size - 8;
                if (box.Type == BoxType.MediaDataBox)
                {

                    using var memory = pool.Rent(size);
                    var buffer = memory.Memory.Slice(0, size);
                    await input.ReadAsync(buffer, cancellationToken);
                    var stream = buffer.AsStream();
                    var captions = TtmlCaptions.Parse(stream, _logger);
                    foreach (var caption in captions.Body.Captions)
                    {

                        if (caption.Start > caption.End)
                        {

                            caption.End = caption.Start.Add(MilliSecond);
                        }
                        await vttWriter.WriteLineAsync($@"{caption.Start:hh\:mm\:ss\.fff} --> {caption.End:hh\:mm\:ss\.fff}");
                        await vttWriter.WriteLineAsync(caption.Text);
                        await vttWriter.WriteLineAsync();
                    }
                }
                else
                {
                    await SkipAsync(input, size, cancellationToken);
                }
            }
        }

        public async Task TransMuxSmoothAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            var pool = MemoryPool<byte>.Shared;
            var header = new byte[8];

            while (true)
            {
                var bytes = await source.ReadAsync(header, cancellationToken);
                if (bytes == 0) break;
                var box = BoxFactory.Parse(header.AsSpan());
                _logger.LogTrace("Found Box {type} size {size}", box.Type.GetBoxName(), box.Size);
                var size = (int)box.Size;
                using var memory = pool.Rent((int)box.Size);
                var buffer = memory.Memory.Slice(0, size);
                header.CopyTo(buffer);
                await source.ReadAsync(buffer.Slice(8), cancellationToken);
                if (box.Type == BoxType.MovieFragmentBox)
                {
                    var stream = buffer.AsStream();
                    var moof = (MovieFragmentBox)BoxFactory.Parse(new BoxReader(stream));
                    foreach (var track in moof.Tracks)
                    {
                        var tfxd = track.GetChildren<TrackFragmentExtendedHeaderBox>().SingleOrDefault();
                        if (tfxd != null)
                        {
                            var tfdt = new TrackFragmentDecodeTimeBox();
                            tfdt.BaseMediaDecodeTime = tfxd.Time;
                            track.Children.Remove(tfxd);
                            track.Children.Insert(0, tfdt);
                            moof.ComputeSize();
                        }
                    }
                    moof.Write(destination);
                }
                else
                {
                    await destination.WriteAsync(buffer, cancellationToken);
                }
            }
        }
    }

    class TtmlToVttTransmuxer : ChainPipeSource
    {
        private readonly TransMuxer _transMuxer;

        public TtmlToVttTransmuxer(TransMuxer transMuxer, IPipeSource source) : base(source)
        {
            _transMuxer = transMuxer;
        }

        public override string GetStreamArguments() => "-f webvtt";

        protected override async Task TransformDestination(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            using (source)
            {
                await _transMuxer.TransMuxTTMLAsync(source, destination, cancellationToken);
            }
        }
    }

    class IsmvToCmafMuxer : ChainPipeSource
    {
        private readonly TransMuxer _transMuxer;
        private readonly Channel _channel;

        public IsmvToCmafMuxer(TransMuxer transMuxer, IPipeSource source, Channel channel) : base(source)
        {
            _transMuxer = transMuxer;
            _channel = channel;
        }

        public override string GetStreamArguments() => "-f mp4";

        protected override async Task TransformDestination(Stream client, Stream destination, CancellationToken cancellationToken)
        {
            using (client)
            {
                await _transMuxer.TransMuxSmoothAsync(client, destination, cancellationToken);
            }
        }
    }

    interface IPipeToFileSource : IPipeSource
    {
        public Task RunAsync(CancellationToken cancellationToken);
    }

    class FfmpegIsmvToMp4Muxer : IPipeToFileSource
    {
        private readonly TransMuxer _transMuxer;
        private readonly IPipeSource _from;
        private readonly Channel _channel;
        private readonly string _destination;

        public FfmpegIsmvToMp4Muxer(TransMuxer transMuxer, IPipeSource source, Channel channel, string destionation)
        {
            _transMuxer = transMuxer;
            _from = source;
            _channel = channel;
            _destination = destionation;
        }

        public string GetStreamArguments() => "-f mp4";

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            const int BlockSize = 64 * 1024;
            var sink = new StreamPipeSink(outputStream)
            {
                Format = "mp4",
                BlockSize = BlockSize
            };
            await _transMuxer.PipedTransMuxAsync(_from, sink, _channel, cancellationToken);
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            await _transMuxer.TransMuxAsync(_from, _destination, _channel, cancellationToken);
        }

        protected async Task TransformSource(Stream server, CancellationToken cancellationToken)
        {
            const int BlockSize = 64 * 1024;
            var sink = new StreamPipeSink(server)
            {
                Format = "mp4",
                BlockSize = BlockSize
            };
            using (server)
            {
                await _transMuxer.PipedTransMuxAsync(_from, sink, _channel, cancellationToken, dash: false);
            }
        }
    }
}
