using System.Buffers;

namespace MediaMigrate.Utils
{
    static class StreamExtensions
    {
        /// <summary>
        /// Read the exact number of bytes from stream. Handle EOF
        /// </summary>
        public static async Task<int> ReadExactAsync(this Stream stream, Memory<byte> memory, CancellationToken cancellationToken)
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

        /// <summary>
        /// Skip a given number of bytes from a stream. handle non-seekable stream.
        /// </summary>
        public static async Task SkipAsync(this Stream stream, int bytes, CancellationToken cancellationToken)
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
    }
}
