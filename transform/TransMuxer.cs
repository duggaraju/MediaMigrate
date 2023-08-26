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
using FFMpegCore.Arguments;

namespace MediaMigrate.Transform
{
    public record struct TransMuxOptions (
        int TrackId,
        bool Cmaf,
        bool FastStart,
        Channel Channel,
        double Offset,
        bool HasDiscontinuity
    );

    internal class AudioResample : IAudioFilterArgument
    {
        public string Key => "aresample";

        public string Value => "async=1";
    }

    internal class AudioDelay : IAudioFilterArgument
    {
        private double _delay;
        public AudioDelay(double delay)
        {
            _delay = delay;
        }

        public string Key => "adelay";

        public string Value => $"{_delay}s:all=1";
    }

    internal class TransMuxer
    {
        private readonly ILogger _logger;

        public TransMuxer(ILogger<TransMuxer> logger)
        {
            _logger = logger;
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
            await processor.RunAsync(_logger, cancellationToken);
        }

        public async Task TransmuxStreamingUriAsync(Uri uri, string filePath, CancellationToken cancellationToken)
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
            await processor.RunAsync(_logger, cancellationToken);
        }

        public async Task TransMuxAsync(IPipeSource source, string destination, TransMuxOptions options, CancellationToken cancellationToken)
        {
            var processor = FFMpegArguments
                .FromPipeInput(source, args =>
                {
                    if (options.Offset != 0.0)
                        args.WithCustomArgument($"-itsoffset {options.Offset}");
                })
                .OutputToFile(
                    destination,
                    overwrite: true,
                    args => args.CopyChannel(options.Channel).AddMp4MuxingOptions(options));
            await processor.RunAsync(_logger, cancellationToken);
        }

        public async Task PipedTransMuxAsync(IPipeSource source, IPipeSink destination, TransMuxOptions options, CancellationToken cancellationToken)
        {
            var processor = FFMpegArguments
                .FromPipeInput(source, args =>
                {
                    if (options.Offset != 0.0)
                        args.WithCustomArgument($"-itsoffset {options.Offset}");
                })
                .OutputToPipe(
                    destination,
                    args => args.CopyChannel(options.Channel).AddMp4MuxingOptions(options));
            await processor.RunAsync(_logger, cancellationToken);
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
                var minSize = Math.Min(bytes, 64 * 1024);
                using var memory = memoryPool.Rent(minSize);
                while (bytes > 0)
                {
                    var buffer = memory.Memory.Slice(0, Math.Min(minSize, bytes));
                    bytes -= await stream.ReadAsync(buffer, cancellationToken);
                }
            }
        }

        public static async Task<int> ReadExactAsync(Stream stream, Memory<byte> memory, CancellationToken cancellationToken)
        {
            var bytes = memory.Length;
            var bytesRead = 0;
            while (bytes > 0)
            {
                var read = await stream.ReadAsync(memory, cancellationToken);
                if (read == 0)
                {
                    break;
                }
                memory = memory.Slice(read);
                bytesRead += read;
                bytes -= read;
            }
            return bytesRead;
        }

        static readonly TimeSpan MilliSecond = TimeSpan.FromMilliseconds(1);

        public async Task TransMuxTTMLAsync(Stream input, Stream output, TransMuxOptions options, CancellationToken cancellationToken)
        {
            var firstTime  = TimeSpan.MinValue;
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
                if (!Enum.IsDefined(box.Type))
                {
                    _logger.LogError("Unknown box type {type}. Malfored TTML", box.Type);
                    throw new InvalidDataException($"Malformed TTML. Unknown box type {box.Type}");
                }
                var size = (int)box.Size - 8;
                if (box.Type == BoxType.MediaDataBox)
                {
                    using var memory = pool.Rent(size);
                    var buffer = memory.Memory.Slice(0, size);
                    await ReadExactAsync(input, buffer, cancellationToken);
                    var stream = buffer.AsStream();
                    var captions = TtmlCaptions.Parse(stream, _logger);
                    foreach (var caption in captions.Body.Captions)
                    {
                        if (firstTime == TimeSpan.MinValue)
                        {
                            firstTime = caption.Start.Add(TimeSpan.FromSeconds(-options.Offset));
                        }

                        caption.Start -= firstTime;
                        caption.End -= firstTime;
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

        public async Task TransMuxSmoothAsync(Stream source, Stream destination, TransMuxOptions options, CancellationToken cancellationToken)
        {
            var pool = MemoryPool<byte>.Shared;
            var header = new byte[8];
            var skip = false;
            while (true)
            {
                var bytes = await ReadExactAsync(source, header.AsMemory(), cancellationToken);
                if (bytes == 0) break;
                var box = BoxFactory.Parse(header.AsSpan());
                if (!Enum.IsDefined(box.Type))
                {
                    _logger.LogError("Unknown box type {type}. Malfored Smooth media", box.Type);
                    throw new InvalidDataException($"Malformed Smooth asset. Unknown box type {box.Type}");
                }

                _logger.LogTrace(Events.TransMuxer, "Found Box {type} size {size}", box.Type.GetBoxName(), box.Size);
                var size = (int)box.Size;
                if (!Enum.IsDefined(box.Type))
                {
                    _logger.LogError(Events.TransMuxer, "Unknown box type {type}", box.Type);
                }
                if (box.Type == BoxType.MovieFragmentRandomAccessBox)
                {
                    _logger.LogTrace(Events.TransMuxer, "Skipping box {type} size {size}", box.Type, box.Size);
                    await SkipAsync(source, size - 8, cancellationToken);
                    continue;
                }
                using var memory = pool.Rent((int)box.Size);
                var buffer = memory.Memory.Slice(0, size);
                header.CopyTo(buffer);
                await ReadExactAsync(source, buffer.Slice(8), cancellationToken);
                if (box.Type == BoxType.MovieFragmentBox)
                {
                    var stream = buffer.AsStream();
                    var moof = (MovieFragmentBox)BoxFactory.Parse(new BoxReader(stream));
                    var track = moof.Tracks.Single();
                    var theader = track.GetSingleChild<TrackFragmentHeaderBox>();
                    if (theader.TrackId != options.TrackId)
                    {
                        skip = true;
                        continue;
                    }
                    else
                    {
                        skip = false;
                    }

                    // Always set version to 1 due to bug in old smooth content.
                    var trun = track.GetSingleChild<TrackFragmentRunBox>();
                    trun.Version = 1;
                    var tfxd = track.GetChildren<TrackFragmentExtendedHeaderBox>().SingleOrDefault();
                    if (tfxd != null)
                    {
                        var tfdt = new TrackFragmentDecodeTimeBox();
                        tfdt.BaseMediaDecodeTime = tfxd.Time;
                        track.Children.Remove(tfxd);
                        track.Children.Insert(0, tfdt);
                    }
                    moof.ComputeSize();
                    var newSize = (int) moof.Size;
                    using var newMoof = pool.Rent(newSize);
                    var target = newMoof.Memory.AsStream();
                    moof.Write(target);
                    await destination.WriteAsync(newMoof.Memory.Slice(0, newSize));
                }
                else if (!skip)
                {
                    await destination.WriteAsync(buffer, cancellationToken);
                }
            }
        }

        public async Task TranscodeAudioAsync(Stream source, Stream destination, TransMuxOptions transMuxOptions, CancellationToken cancellationToken)
        {
            var input = new StreamPipeSource(source);
            var output = new StreamPipeSink(destination);
            await TranscodeAudioAsync(input, output, transMuxOptions, cancellationToken);
        }

        public async Task TranscodeAudioAsync(IPipeSource input, IPipeSink output, TransMuxOptions transMuxOptions, CancellationToken cancellationToken)
        {
            FFMpegArgumentProcessor processor;
            if (transMuxOptions.Offset > 0.0)
            {
                processor = FFMpegArguments
                .FromPipeInput(input)
                .OutputToPipe(output, options =>
                options
                .WithAudioFilters(
                    filterOptions =>
                    {
                        if (transMuxOptions.HasDiscontinuity)
                        {
                            filterOptions.Arguments.Add(new AudioResample());
                        }
                        filterOptions.Arguments.Add(new AudioDelay(transMuxOptions.Offset));
                    }
                )
                .WithAudioCodec(AudioCodec.Aac)
                .AddMp4MuxingOptions(transMuxOptions));
            }
            else
            {
                if (transMuxOptions.HasDiscontinuity)
                {
                    processor = FFMpegArguments
                    .FromPipeInput(input, opt => opt.Seek(TimeSpan.FromSeconds(Math.Abs(transMuxOptions.Offset))))
                    .OutputToPipe(output, options =>
                    options
                    .ForceFormat("mp4")
                    .WithAudioFilters(
                        audioFilterOptions =>
                        {
                            audioFilterOptions.Arguments.Add(new AudioResample());
                        }
                    )
                    .WithAudioCodec(AudioCodec.Aac)
                    .AddMp4MuxingOptions(transMuxOptions));
                }
                else
                {
                    processor = FFMpegArguments
                    .FromPipeInput(input, opt => opt.Seek(TimeSpan.FromSeconds(transMuxOptions.Offset)))
                    .OutputToPipe(
                        output, 
                        options => options.CopyChannel(transMuxOptions.Channel).AddMp4MuxingOptions(transMuxOptions));
                }
            }
            await processor.RunAsync(_logger, cancellationToken);
        }
    }

    class TtmlToVttTransmuxer : ChainPipeSource
    {
        private readonly TransMuxer _transMuxer;
        private readonly TransMuxOptions _options;

        public TtmlToVttTransmuxer(TransMuxer transMuxer, IPipeSource source, ILogger logger, TransMuxOptions options) 
            : base(source, logger)
        {
            _transMuxer = transMuxer;
            _options = options;
        }

        public override string GetStreamArguments() => "-f webvtt";

        protected override async Task TransformDestination(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            using (source)
            {
                await _transMuxer.TransMuxTTMLAsync(source, destination, _options, cancellationToken);
            }
        }
    }

    class IsmvToCmafMuxer : ChainPipeSource
    {
        private readonly TransMuxer _transMuxer;
        private readonly TransMuxOptions _options;

        public IsmvToCmafMuxer(TransMuxer transMuxer, IPipeSource source, TransMuxOptions options, ILogger logger) : base(source, logger)
        {
            _transMuxer = transMuxer;
            _options = options;
        }

        public override string GetStreamArguments() => "-f mp4";

        protected override async Task TransformDestination(Stream client, Stream destination, CancellationToken cancellationToken)
        {
            using (client)
            {
                await _transMuxer.TransMuxSmoothAsync(client, destination, _options, cancellationToken);
            }
        }
    }

    interface IPipeToFileSource : IPipeSource
    {
        public Task WriteToAsync(string destination, CancellationToken cancellationToken);
    }

    class FfmpegIsmvToMp4Muxer : IPipeToFileSource
    {
        private readonly TransMuxer _transMuxer;
        private readonly IPipeSource _from;
        private TransMuxOptions _options;

        public FfmpegIsmvToMp4Muxer(TransMuxer transMuxer, IPipeSource source, TransMuxOptions options)
        {
            _transMuxer = transMuxer;
            _from = source;
            _options = options;
        }

        public string GetStreamArguments() => "-f mp4";

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            await TransformSource(outputStream, cancellationToken);
        }

        public async Task WriteToAsync(string destination, CancellationToken cancellationToken)
        {
            using var output = File.OpenWrite(destination);
            await TransformSource(output, cancellationToken);
        }

        protected async Task TransformSource(Stream destination, CancellationToken cancellationToken)
        {
            const int BlockSize = 64 * 1024;
            var sink = new StreamPipeSink(destination)
            {
                Format = "mp4",
                BlockSize = BlockSize
            };
            if (_options.Channel == Channel.Audio)
            {
                await _transMuxer.TranscodeAudioAsync(_from, sink, _options, cancellationToken);
            }
            else
            {
                await _transMuxer.PipedTransMuxAsync(_from, sink, _options, cancellationToken);
            }
        }
    }
}
