using MediaMigrate.Contracts;
using MediaMigrate.Log;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MediaMigrate.Transform
{
    internal class ShakaPackager : BasePackager
    {
        static readonly string PackagerPath = AppContext.BaseDirectory;
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
            => manifest.IsLiveArchive ? FileType.File : base.GetInputFileType(manifest);

        protected override bool NeedsTransMux(Manifest manifest, ClientManifest? clientManifest)
        {
            if (manifest.Format == "fmp4")
            {
                _logger.LogWarning("Shaka packager doesn't support smooth streaming assets with multiple tracks in single file. Transmuxing into single track CMAF file.");
                return true;
            }
            else if (manifest.IsLiveArchive)
            {
                return _discontinuityInfo!.Delta > 0.1 || clientManifest!.HasDiscontinuities();
            }
            return base.NeedsTransMux(manifest, clientManifest);
        }

        protected override FileType GetOutputFileType(Manifest manifest)
        {
            // known hang on linux and windows not supported.
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? FileType.File : base.GetOutputFileType(manifest);
        }

        private IEnumerable<string> GetArguments(AssetDetails details, IList<PackagerInput> inputs, IList<PackagerOutput> outputs, IList<PackagerOutput> manifests)
        {
            const string EncryptionLabel = "cenc";
            var manifest = details.Manifest!;
            var drm_label = _options.EncryptContent ? $",drm_label={EncryptionLabel}" : string.Empty;
            var i = 0;
            var arguments = inputs
                .SelectMany(item =>
                {
                    return item.Tracks.Select(track =>
                    {
                        var ext = track.IsMultiFile ? MEDIA_FILE : string.Empty;
                        var stream = track.Type.ToString().ToLowerInvariant();
                        var language = string.IsNullOrEmpty(track.SystemLanguage) || track.SystemLanguage == "und" ? string.Empty : $"language={track.SystemLanguage}";
                        var label = track is TextTrack ? string.Empty: drm_label;
                        return $"stream={stream},in={item.FilePath},out={outputs[i].FilePath},playlist_name={manifests[i++].FilePath},{language}{label}";
                    });
                }).ToList();

            if (_options.EncryptContent)
            {
                arguments.Add("--enable_raw_key_encryption");
                arguments.Add("--protection_scheme");
                arguments.Add("cbcs");
                arguments.Add("--keys");
                arguments.Add($"label={EncryptionLabel}:key_id={details.KeyId}:key={details.EncryptionKey}");
                arguments.Add("--hls_key_uri");
                arguments.Add(details.LicenseUrl!);
                arguments.Add("--clear_lead");
                arguments.Add("0");
            }

            var usePipe = GetInputFileType(manifest) == FileType.Pipe || GetOutputFileType(manifest) == FileType.Pipe;
            if (usePipe)
            {
                arguments.Add("--io_block_size");
                arguments.Add("65536");
            }

            var vlog = 0;
            arguments.Add($"--vmodule=*={vlog}");
            arguments.Add("--temp_dir");
            arguments.Add(_options.WorkingDirectory);

            arguments.Add("--mpd_output");
            arguments.Add(manifests[manifests.Count - 1].FilePath);

            arguments.Add("--hls_master_playlist_output");
            arguments.Add(manifests[manifests.Count - 2].FilePath);
            return arguments;
        }

        protected override Task<bool> PackageAsync(
            AssetDetails assetDetails,
            string workingDirectory,
            IList<PackagerInput> inputs,
            IList<PackagerOutput> outputs,
            IList<PackagerOutput> manifests,
            CancellationToken cancellationToken)
        {
            var arguments = GetArguments(assetDetails, inputs, outputs, manifests);
            var process = StartProcess(Packager, arguments,
                exit =>
                {
                    if (exit == 0)
                    {
                        _taskCompletionSource.SetResult(true);
                    }
                    else
                    {
                        _logger.LogError("Shaka packager failed with error code {code}", exit);
                        if (assetDetails.Manifest!.Format.StartsWith("mp4"))
                        {
                            _logger.LogWarning("Ensure that the MP4 files starts with moov box in the front");
                        }
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
        static readonly Regex ShakaLogRegEx = new(ShakaLogPattern, RegexOptions.Compiled);

        public static LogLevel GetLogLevel(string level)
        {
            return level switch
            {
                "FATAL" => LogLevel.Critical,
                "ERROR" => LogLevel.Error,
                "INFO" => LogLevel.Trace,
                "WARN" => LogLevel.Warning,
                "VERBOSE1" => LogLevel.Trace,
                "VERBOSE2" => LogLevel.Trace,
                _ => LogLevel.Information
            };
        }

        public static LogLevel GetLineLogLevel(string line)
        {
            var match = ShakaLogRegEx.Match(line);
            var group = match.Groups["level"];
            return match.Success && group.Success ? GetLogLevel(group.Value) : LogLevel.Information;
        }

        private void LogProcessOutput(string? line)
        {
            if (line != null)
            {
                _logger.Log(GetLineLogLevel(line), Events.ShakaPackager, line);
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
