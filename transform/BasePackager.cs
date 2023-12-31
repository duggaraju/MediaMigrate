﻿using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using MediaMigrate.Pipes;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using MediaMigrate.Log;
using MediaMigrate.Utils;

namespace MediaMigrate.Transform
{
    public enum FileType
    {
        File,
        Pipe,
    }

    public record PackagerOutput(string File, FileType Type, string? ContentType, string? ContentLanguage)
    {
        public string FilePath { get; set; } = File;
    }

    record PackagerInput(string File, FileType Type, bool TransMux, List<Track> Tracks)
    {
        public string FilePath { get; set; } = File;
    }

    public record DiscontinuityInfo
    {
        public IList<(StreamType Type, double Time)> TimeStamps { get; init; }

        public double MinVideoTimestamp { get; set; }

        public double MinAudioTimeStamp { get; set; }

        public double Delta => TimeStamps.Max().Time - Math.Min(MinVideoTimestamp, MinVideoTimestamp);

        public DiscontinuityInfo(ClientManifest clientManifest)
        {
            TimeStamps = clientManifest.Streams
                .Where(s => (s.Type == StreamType.Video || s.Type == StreamType.Audio) && s.Chunks != null)
                .Select(stream => (stream.Type, stream.FirstTimeStamp))
                .ToArray();
            MinVideoTimestamp = TimeStamps.Where(s => s.Type == StreamType.Video).Min(s => s.Item2);
            MinAudioTimeStamp = TimeStamps.Where(s => s.Type == StreamType.Audio).Min(s => s.Item2);
        }
    }

    abstract class BasePackager : IPackager
    {
        public const string MEDIA_FILE = ".mp4";
        public const string DASH_MANIFEST = ".mpd";
        public const string HLS_MANIFEST = ".m3u8";
        public const string VTT_FILE = ".vtt";
        public const string TRANSCRIPT_SOURCE = "transcriptsrc";

        public const string HLS_CONTENT_TYPE = "application/vnd.apple.mpegurl";
        public const string DASH_CONTENT_TYPE = "application/dash+xml";
        public const string MP4_CONTENT_TYPE = "video/mp4";
        public const string VTT_CONTENT_TYPE = "text/vtt";


        protected readonly TransMuxer _transMuxer;
        protected readonly ILogger _logger;
        protected readonly StorageOptions _options;
        protected DiscontinuityInfo? _discontinuityInfo;

        public BasePackager(StorageOptions options, TransMuxer transMuxer, ILogger logger)
        {
            _options = options;
            _transMuxer = transMuxer;
            _logger = logger;
        }

        protected virtual FileType GetInputFileType(Manifest manifest) => _options.UsePipes ? FileType.Pipe : FileType.File;

        protected virtual FileType GetOutputFileType(Manifest manifest) => _options.UsePipes ? FileType.Pipe : FileType.File;

        // Manifests do not use pipe to avoid multiple writes to storage.
        protected virtual FileType GetManifestFileType(Manifest manifest) => FileType.File;

        protected virtual bool NeedsTransMux(Manifest manifest, ClientManifest? clientManifest)
        {
            return clientManifest != null && clientManifest.HasDiscontinuities();
        }

        protected List<PackagerInput> GetInputs(Manifest manifest, ClientManifest? clientManifest, string workingDirectory)
        {
            if (clientManifest != null)
            {
                _discontinuityInfo = new DiscontinuityInfo(clientManifest);
            }

            var fileType = GetInputFileType(manifest);
            var needsTransMux = NeedsTransMux(manifest, clientManifest);

            var inputs = new List<PackagerInput>();
            foreach (var track in manifest.Tracks)
            {
                var extension = track.IsMultiFile ? (track is TextTrack ? VTT_FILE : MEDIA_FILE) : string.Empty;
                var file = $"{track.Source}{extension}";
                var transMux = needsTransMux;
                if (track is TextTrack)
                {
                    if (track.Parameters.Any(p => p.Name == "parentTrackName"))
                    {
                        continue;
                    }

                    if (!track.Source.EndsWith(VTT_FILE))
                    {
                        var trackFile = track.Parameters.SingleOrDefault(p => p.Name == TRANSCRIPT_SOURCE);
                        if (trackFile != null)
                        {
                            file = trackFile.Value;
                        }
                        else
                        {
                            transMux = true;
                        }
                    }
                    else
                    {
                        transMux = false;
                    }
                }

                var item = inputs.Find(i => i.File == file);
                // for now workaround for smooth input till shaka packager fix goes in.
                if (item == default || manifest.IsSmooth)
                {
                    var suffix = manifest.IsSmooth ? $"_{track.TrackId}": string.Empty;
                    var input = new PackagerInput(file, fileType, transMux, new List<Track> { track })
                    {
                        FilePath = Path.Combine(
                            workingDirectory,
                            manifest.Format == "fmp4" ? $"{Path.GetFileNameWithoutExtension(file)}{suffix}{Path.GetExtension(file)}" : file)
                    };
                    inputs.Add(input);
                }
                else
                {
                    item.Tracks.Add(track);
                }
            }
            return inputs;
        }

        protected List<PackagerOutput> GetOutputs(Manifest manifest, IList<PackagerInput> inputs)
        {
            var baseName =  _options.ManifestName ?? Path.GetFileNameWithoutExtension(manifest.FileName);
            var fileType = GetOutputFileType(manifest);
            return inputs.SelectMany(i => i.Tracks)
                        .Select((t, i) =>
                        {
                            var ext = t is TextTrack ? VTT_FILE : MEDIA_FILE;
                            var contentType = t is TextTrack ? VTT_CONTENT_TYPE : MP4_CONTENT_TYPE;
                            // TODO: if you want to keep original file names.
                            // var baseName = Path.GetFileNameWithoutExtension(t.Source);
                            return new PackagerOutput($"{baseName}_{i}{ext}", fileType, contentType, t.SystemLanguage);
                        }).ToList();
        }

        protected abstract List<PackagerOutput> GetManifests(Manifest manifest, IList<PackagerOutput> outputs);

        protected abstract Task PackageAsync(
            AssetDetails assetDetails,
            string workingDirectory,
            IList<PackagerInput> inputFiles,
            IList<PackagerOutput> outputFiles,
            IList<PackagerOutput> manifests,
            CancellationToken cancellationToken);

        private static string Escape(string argument)
        {
            if (argument.Contains(' '))
            {
                return $"\"{argument}\"";
            }
            return argument;
        }

        protected Process StartProcess(
            string command,
            IEnumerable<string> arguments,
            Action<int> onExit,
            Action<string?> stdOut,
            Action<string?> stdError)
        {
            var argumentString = string.Join(" ", arguments.Select(Escape));
            _logger.LogTrace("Starting packager {command} arguments: {args}", command, argumentString);
            var processStartInfo = new ProcessStartInfo(command, argumentString)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };

            var process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (s, args) => stdOut(args.Data);
            process.ErrorDataReceived += (s, args) => stdError(args.Data);
            process.Exited += (s, args) =>
            {
                if (process.ExitCode != 0)
                {
                    _logger.LogError("Packager {command} finished with exit code {code}", command, process.ExitCode);
                }
                onExit(process.ExitCode);
                process.Dispose();
            };
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return process;
            }
            catch (Exception ex)
            {
                _logger.LogError(Events.ShakaPackager, "Failed to start process {command} with error: {ex}", command, ex);
                throw;
            }
        }

        private IPipeSource GetInputSource(AssetDetails assetDetails, PackagerInput input)
        {
            var tracks = input.Tracks;
            IPipeSource source;
            if (tracks.Count == 1 && tracks[0].IsMultiFile)
            {
                var track = tracks[0];
                source = new MultiFileSource(assetDetails, input.TransMux ? _transMuxer : null, track, _logger);
            }
            else
            {
                source = new BlobSource(assetDetails.Container, input.File, _logger);
            }

            if (input.TransMux)
            {
                if (tracks.Count == 1)
                {
                    var track = tracks[0];
                    var offset = 0.0;
                    var hasDiscontinuity = false;
                    if (assetDetails.Manifest!.IsLiveArchive)
                    {
                        var (stream, _) = assetDetails.ClientManifest!.GetStream(track);
                        offset = stream.FirstTimeStamp - _discontinuityInfo!.MinVideoTimestamp;
                        hasDiscontinuity = stream.HasDiscontinuities();
                    }

                    var options = new TransMuxOptions
                    {
                        TrackId = track.TrackId,
                        Channel = track is VideoTrack ? Channel.Video : track is AudioTrack ? Channel.Audio : Channel.Subtitle,
                        Offset = offset,
                        HasDiscontinuity = hasDiscontinuity,
                        Cmaf = true
                    };

                    if (track is TextTrack)
                    {
                        source = new TtmlToVttTransmuxer(_transMuxer, source, _logger, options);
                    }
                    else
                    {
                        if (assetDetails.Manifest.IsSmooth)
                        {
                            source = new IsmvToCmafMuxer(_transMuxer, source, options, _logger);
                        }
                        else if (track is AudioTrack && hasDiscontinuity)
                        {
                            source = new FfmpegIsmvToMp4Muxer(_transMuxer, source, options);
                        }
                    }
                }
                else
                {
                    var options = new TransMuxOptions
                    {
                        Channel = Channel.Both,
                        FastStart = input.Type == FileType.Pipe
                    };
                    source = new FfmpegIsmvToMp4Muxer(_transMuxer, source, options);
                }
            }

            return source;
        }

        private IPipeSink GetOutputSink(
            PackagerOutput output,
            IFileUploader uploader)
        {
            var file = output.File;
            // Report update for every 1MB.
            long update = 0;
            var progress = new Progress<long>(p =>
            {
                if (p >= update)
                {
                    lock (this)
                    {
                        if (p >= update)
                        {
                            _logger.LogTrace(Events.BlobUpload, "Uploaded {byte} bytes to {file}", p, file);
                            update += 1024 * 1024;
                        }
                    }
                }
            });
            var headers = new ContentHeaders(output.ContentType, output.ContentLanguage);
            return new UploadSink(uploader, file, headers, progress, _logger);
        }

        private async Task DownloadAsync(AssetDetails assetDetails, PackagerInput input, CancellationToken cancellationToken)
        {
            var source = GetInputSource(assetDetails, input);

            if (source is IPipeToFileSource fileSource)
            {
                await fileSource.WriteToAsync(input.FilePath, cancellationToken);
            }
            else
            {
                var sink = new FileSink(input.FilePath, source);
                await sink.RunAsync(cancellationToken);
            }
        }

        public async Task RunAsync(
            AssetDetails assetDetails,
            string workingDirectory,
            IFileUploader uploader,
            CancellationToken cancellationToken)
        {
            // Create a linked CancellationTokenSource which when disposed cancells all tasks.
            using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // TODO: have absolute timeout configurable.
            source.CancelAfter(TimeSpan.FromHours(1)); 

            var (assetName, _, decryptionInfo, manifest, clientManifest) = assetDetails;
            using var decryptor = decryptionInfo == null ? null : new Decryptor(decryptionInfo);
            var allTasks = new List<Task>();

            var inputs = GetInputs(manifest!, clientManifest, workingDirectory);
            var outputs = GetOutputs(manifest!, inputs);
            var manifests = GetManifests(manifest!, outputs);

            var pipes = new List<Pipe>();
            foreach (var input in inputs)
            {
                var inputSource = GetInputSource(assetDetails, input);
                if (input.Type == FileType.Pipe)
                {
                    var pipe = new PipeSource(input.FilePath, inputSource);
                    pipes.Add(pipe);
                    input.FilePath = pipe.PipePath;
                }
            }

            await Task.WhenAll(inputs
                .Where(i => i.Type == FileType.File)
                .Select(i => DownloadAsync(assetDetails, i, cancellationToken)));

            var outputDirectory = Path.Combine(workingDirectory, "output");
            Directory.CreateDirectory(outputDirectory);

            var uploads = new List<FileSource>();
            foreach (var output in outputs.Concat(manifests))
            {
                var sink = GetOutputSink(output, uploader);
                var filePath = Path.Combine(outputDirectory, output.File);
                if (output.Type == FileType.Pipe)
                {
                    var pipe = new PipeSink(filePath, sink, _logger);
                    output.FilePath = pipe.PipePath;
                    pipes.Add(pipe);
                }
                else
                {
                    uploads.Add(new FileSource(filePath, sink));
                    output.FilePath = filePath;
                }
            }

            _logger.LogTrace("Starting packaging of asset {name}...", assetDetails.AssetName);
            var task = PackageAsync(
                assetDetails,
                outputDirectory,
                inputs,
                outputs,
                manifests,
                source.Token).ContinueWith(t =>
                {
                    source.CancelAfter(TimeSpan.FromSeconds(5));
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
            allTasks.Add(task);
            allTasks.AddRange(pipes
                .Select(async p =>
                {
                    using (p)
                    {
                        await p!.RunAsync(source.Token);
                    }
                }));

            try
            {
                await allTasks.FailFastWaitAll(source.Token);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "One of the tasks failed. Cancelling all.");
                source.Cancel();
                throw;
            }

            _logger.LogTrace("Packaging asset: {asset} finished successfully!", assetDetails.AssetName);

            // Upload any files pending to be uploaded.
            await Task.WhenAll(uploads.Select(upload => upload.RunAsync(cancellationToken)));
        }
    }
}

