using MediaMigrate.Ams;
using FFMpegCore.Pipes;

namespace MediaMigrate.pipes
{
    internal class DecryptingSource : IPipeSource
{
        private readonly Decryptor _decryptor;
        private readonly IPipeSource _pipeSource;
        private readonly string _fileName;

        public DecryptingSource(
            Decryptor decryptor,
            string fileName,
            IPipeSource source)
{
            _decryptor = decryptor;
            _fileName = fileName;
            _pipeSource = source;
}

        public string GetStreamArguments() => _pipeSource.GetStreamArguments();

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
{
            using var stream = _decryptor.GetDecryptingWriteStream(outputStream, _fileName);
            await _pipeSource.WriteAsync(stream, cancellationToken);
}
    }
}
