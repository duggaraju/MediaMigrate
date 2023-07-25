using FFMpegCore.Pipes;
using MediaMigrate.Utils;
using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace MediaMigrate.Pipes
{
    public interface IPipe : IDisposable
    {
        string PipePath { get; }

        Task RunAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// A class to abstract platform specific pipe.
    /// </summary>
    abstract class Pipe : IPipe
    {
        protected readonly NamedPipeServerStream? _server;
        protected readonly PipeDirection _direction;

        public string PipePath { get; }

        public string FilePath { get; }

        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public Pipe(string filePath, PipeDirection direction)
        {
            FilePath = filePath;
            _direction = direction;

            if (IsWindows)
            {
                var pipeName = $"pipe_{Guid.NewGuid().ToString("N").Substring(0, 8)}_{Path.GetExtension(filePath)}";
                _server = new NamedPipeServerStream(pipeName, direction, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                PipePath = $@"\\.\pipe\{pipeName}";
            }
            else
            {
                PipePath = filePath;
                Mono.Unix.Native.Syscall.mkfifo(filePath, Mono.Unix.Native.FilePermissions.DEFFILEMODE);
            }
        }

        public async Task<Stream> OpenPipeAsync(CancellationToken cancellationToken)
        {
            if (_server == null)
            {
                var access = _direction == PipeDirection.Out ? FileAccess.Write : FileAccess.Read;
                //return new FileStream(PipePath, FileMode.Open, access, FileShare.ReadWrite, 16 * 1024, true);
                return File.Open(PipePath, FileMode.Open, access, FileShare.ReadWrite);
            }
            else
            {
                await _server.WaitForConnectionAsync(cancellationToken);
                if (!_server.IsConnected)
                {
                    throw new OperationCanceledException();
                }
                return _server;
            }
        }

        public void Dispose()
        {
            _server?.Dispose();
        }

        public abstract Task RunAsync(CancellationToken cancellationToken);
    }

    class PipeSource : Pipe
    {
        private readonly IPipeSource _source;

        public PipeSource(string filePath, IPipeSource source) : base(filePath, PipeDirection.Out)
        {
            _source = source;
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            var run = async () =>
            {
                using var stream = await OpenPipeAsync(cancellationToken);
                await _source.WriteAsync(stream, cancellationToken);
            };
            await (_server == null ? Task.Run(run, cancellationToken) : run());
        }
    }

    class PipeSink : Pipe
    {
        private readonly IPipeSink _sink;
        private readonly ILogger _logger;

        public PipeSink(string filePath, IPipeSink sink, ILogger logger) : base(filePath, PipeDirection.In)
        {
            _sink = sink;
            _logger = logger;
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            var filename = Path.GetFileName(FilePath);
            _logger.LogDebug("Starting upload of {Pipe} to storage {blob}", PipePath, filename);
            var run = async () =>
            {
                using var stream = await OpenPipeAsync(cancellationToken);
                await _sink.ReadAsync(stream, cancellationToken);
            };
            await (_server == null ? Task.Run(run, cancellationToken) : run());
            _logger.LogTrace("Finished upload of {Pipe} to {file}", PipePath, filename);
        }
    }

    class ChainPipeSource : IPipeSource
    {
        protected readonly IPipeSource _from;
        protected readonly ILogger _logger;

        public ChainPipeSource(IPipeSource from, ILogger logger)
        {
            _from = from;
            _logger = logger;
        }

        public virtual string GetStreamArguments() => _from.GetStreamArguments();

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var server = new AnonymousPipeServerStream();
            var client = new AnonymousPipeClientStream(server.GetClientHandleAsString());
            var tasks = new List<Task>
            {
                TransformDestination(client, outputStream, source.Token),
                TransformSource(server, source.Token)
            };

            try
            {
                await tasks.WaitAllThrowOnFirstError();
            }
            catch(Exception ex)
            {
                _logger.LogTrace(ex, "Chained pipe failed. cancelling all tasks");
                source.Cancel();
                throw;
            }
        }

        protected virtual async Task TransformSource(Stream server, CancellationToken cancellationToken)
        {
            using (server)
            {
                await _from.WriteAsync(server, cancellationToken);
            }
        }

        protected virtual async Task TransformDestination(Stream client, Stream destination, CancellationToken cancellationToken)
        {
            using (client)
            {
                await client.CopyToAsync(destination, cancellationToken);
            }
        }
    }
}