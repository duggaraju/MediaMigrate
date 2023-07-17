using Azure.ResourceManager.Media.Models;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MediaMigrate.Ams
{
    sealed class DecryptingTransform : ICryptoTransform
    {
        const int AesBlockSize = 16;

        private readonly ICryptoTransform _transform;
        private ulong _counter = 0;
        private readonly ulong _iv;

        public bool CanReuseTransform => false;

        public bool CanTransformMultipleBlocks => false;

        public int InputBlockSize => AesBlockSize;

        public int OutputBlockSize => AesBlockSize;


        public DecryptingTransform(ICryptoTransform transform, ulong iv)
        {
            _transform = transform;
            _iv = iv;
        }

        public void Dispose()
        {
            _transform.Dispose();
        }

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            var ivAndCounter = new byte[16];
            BinaryPrimitives.WriteUInt64BigEndian(ivAndCounter.AsSpan(), _iv);
            BinaryPrimitives.WriteUInt64BigEndian(ivAndCounter.AsSpan(sizeof(ulong)), _counter++);
            _transform.TransformBlock(ivAndCounter, 0, ivAndCounter.Length, ivAndCounter, 0);
            for (var i = 0; i < inputCount; i++)
            {
                outputBuffer[outputOffset + i] = (byte)(inputBuffer[inputOffset + i] ^ ivAndCounter[i]);
            }

            return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            var tempBlock = new byte[inputCount];
            if (inputCount != 0)
            {
                TransformBlock(inputBuffer, inputOffset, inputCount, tempBlock, 0);
            }
            return tempBlock;
        }
    }

    sealed class Decryptor : IDisposable
    {
        private StorageEncryptedAssetDecryptionInfo? _info;
        private readonly Aes _algo;

        public Decryptor(StorageEncryptedAssetDecryptionInfo info) : this(info.Key)
        {
            _info = info;
        }

        public Decryptor(byte[] key)
        {
            _algo = Aes.Create();
            _algo.Mode = CipherMode.ECB;
            _algo.Padding = PaddingMode.None;
            _algo.Key = key;
        }

        public async Task DecryptingCopyAsync(
            Stream source,
            byte[] key,
            ulong iv,
            Stream destination,
            CancellationToken cancellationToken)
        {
            using var decryptor = new DecryptingTransform(_algo.CreateEncryptor(), iv);
            var cryptoStream = new CryptoStream(source, decryptor, CryptoStreamMode.Read);
            await cryptoStream.CopyToAsync(destination, cancellationToken);
        }

        public void Dispose()
        {
            _algo.Dispose();
        }

        public ulong? GetInitializationVector(string fileName)
        {
            if (_info != null)
            {
                var data = _info.AssetFileEncryptionMetadata.SingleOrDefault(f => f.AssetFileName == fileName);
                if (data != null)
                {

                    return Convert.ToUInt64(data.InitializationVector);
                }
            }
            return null;
        }

        public Stream GetDecryptingReadStream(Stream source, string fileName)
        {
            var iv = GetInitializationVector(fileName);
            if (iv != null)
            {
                var decryptor = new DecryptingTransform(_algo.CreateEncryptor(), iv.Value);
                return new CryptoStream(source, decryptor, CryptoStreamMode.Read);
            }
            return source;
        }

        public Stream GetDecryptingWriteStream(Stream destination, string fileName)
        {
            var iv = GetInitializationVector(fileName);
            if (iv != null)
            {
                var decryptor = new DecryptingTransform(_algo.CreateEncryptor(), iv.Value);
                return new CryptoStream(destination, decryptor, CryptoStreamMode.Write);
            }

            return destination;
        }
    }
}
