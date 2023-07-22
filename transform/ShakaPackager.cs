using MediaMigrate.Contracts;
using MediaMigrate.Log;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MediaMigrate.Transform
{
    internal class ShakaPackager : BasePackager
    {
        static readonly string PackagerPath =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        static readonly string Packager = Path.Combine(PackagerPath, GetExecutableName());

        private readonly TaskCompletionSource<bool> _taskCompletionSource;

        public ShakaPackager(StorageOptions options, TransMuxer transMuxer, ILogger<ShakaPackager> logger)
            : base(options, transMuxer, logger)
        {
            _taskCompletionSource = new TaskCompletionSource<bool>();
        }

        static string GetExecutableName()
        {
            var prefix = "packager";
            var suffix = OperatingSystem.IsLinux() ? "-linux-x64" : OperatingSystem.IsMacOS() ? "-osx-x64" : "-win-x64.exe";
            return prefix + suffix;
        }

        // Shaka packager cannot handle smooth input.
        protected override FileType GetInputFileType(Manifest manifest)
        {
            return FileType.Pipe;
        }

        protected override bool NeedsTransMux(Manifest manifest, ClientManifest? clientManifest)
        {
            if (manifest.Format == "fmp4")
            {
                _logger.LogWarning("Shaka packager doesn't support smooth streaming assets with multiple tracks in single file. Transmuxing into CMAF file.");
                return true;
            }
            return base.NeedsTransMux(manifest, clientManifest);
        }

        protected override FileType GetOutputFileType(Manifest _)
        {
            // known hang on linux and windows not supported.
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? FileType.File : FileType.Pipe;
        }

        private string GetArguments(Manifest manifest, IList<PackagerInput> inputs, IList<PackagerOutput> outputs, IList<PackagerOutput> manifests)
        {
            var usePipe = GetInputFileType(manifest) == FileType.Pipe || GetOutputFileType(manifest) == FileType.Pipe;
            var i = 0;
            var tracks = inputs
                .SelectMany(item =>
                {
                    return item.Tracks.Select(track =>
                    {
                        var ext = track.IsMultiFile ? MEDIA_FILE : string.Empty;
                        var stream = track.Type.ToString().ToLowerInvariant();
                        var language = string.IsNullOrEmpty(track.SystemLanguage) || track.SystemLanguage == "und" ? string.Empty : $"language={track.SystemLanguage}";
                        return $"stream={stream},in={item.FilePath},out={outputs[i].FilePath},playlist_name={manifests[i++].FilePath},{language}";
                    });
                });
            var dash = manifests[manifests.Count - 1].FilePath;
            var hls = manifests[manifests.Count - 2].FilePath;
            var logging = false;
            var extraArgs = $"{(usePipe ? "--io_block_size 65536" : string.Empty)} {(logging ? "--vmodule=*=2" : string.Empty)}";
            return $"{extraArgs} {string.Join(" ", tracks)} --segment_duration {_options.SegmentDuration} --mpd_output {dash} --hls_master_playlist_output {hls}";
        }

        public override Task<bool> PackageAsync(
            AssetDetails assetDetails,
            string workingDirectory,
            IList<PackagerInput> inputs,
            IList<PackagerOutput> outputs,
            IList<PackagerOutput> manifests,
            CancellationToken cancellationToken)
        {
            var arguments = GetArguments(assetDetails.Manifest!, inputs, outputs, manifests);
            var process = StartProcess(Packager, arguments,
                exit =>
                {
                    if (exit == 0)
                    {
                        _taskCompletionSource.SetResult(true);
                    }
                    else
                    {
                        _taskCompletionSource.SetException(new Win32Exception(exit, $"{Packager} failed"));
                    }
                },
                s => { },
                LogProcessOutput);
            cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch (Exception)
                {
                }
            });
            return _taskCompletionSource.Task;
        }

        const string ShakaLogPattern = @"\d+/\d+:(?<level>\w+):";
        static readonly Regex ShakaLogRegEx = new Regex(ShakaLogPattern, RegexOptions.Compiled);
        static readonly IDictionary<string, LogLevel> LogLevels = new Dictionary<string, LogLevel>
        {
            { "FATAL", LogLevel.Critical },
            { "ERROR", LogLevel.Error },
            { "WARN", LogLevel.Warning },
            { "INFO", LogLevel.Information },
            { "VERBOSE1", LogLevel.Trace },
            { "VERBOSE2", LogLevel.Trace },
        };

        private void LogProcessOutput(string? line)
        {
            if (line != null)
            {
                var logLevel = LogLevel.Information;
                var match = ShakaLogRegEx.Match(line);
                var group = match.Groups["level"];
                _ = match.Success && group.Success && LogLevels.TryGetValue(group.Value, out logLevel);
                _logger.Log(logLevel, Events.ShakaPackager, line);
            }
        }

        protected override List<PackagerOutput> GetManifests(Manifest manifest, IList<PackagerOutput> outputs)
        {
            var fileType = GetManifestFileType(manifest);

            var baseName = Path.GetFileNameWithoutExtension(manifest.FileName);
            var manifests = outputs
                .Select((t, i) => new PackagerOutput($"{baseName}_{i}{HLS_MANIFEST}", fileType, HLS_CONTENT_TYPE, null))
                .ToList();
            manifests.Add(new PackagerOutput($"{baseName}{HLS_MANIFEST}", fileType, HLS_CONTENT_TYPE, null));
            manifests.Add(new PackagerOutput($"{baseName}{DASH_MANIFEST}", fileType, DASH_CONTENT_TYPE, null));
            return manifests;
        }
    }
}
