using Azure.ResourceManager.Media.Models;
using MediaMigrate.Contracts;

namespace MediaMigrate.Ams
{
    public interface IStats
    {
        int Total { get; }
    }

    record class AssetStats() : IStats
    {
        private int _total = default;
        private int _encrypted = default;
        private int _streamable = default;
        private int _successful = default;
        private int _migrated = default;
        private int _skipped = default;
        private int _failed = default;
        private int _deleted = default;
        private int _notMigrated = default;

        public int Total => _total;

        public int Encrypted => _encrypted;

        public int Streamable => _streamable;

        public int Successful => _successful;

        public int Failed => _failed;

        public int Skipped => _skipped;

        public int Migrated => _migrated;

        public int Deleted => _deleted;

        public int NotMigrated => _notMigrated;

        public void Update(
            MigrationStatus status,
            bool streamable,
            bool deleteMigrated,
            MediaAssetStorageEncryptionFormat? format = null)
        {
            Interlocked.Increment(ref _total);
            if (format != MediaAssetStorageEncryptionFormat.None)
            {
                Interlocked.Increment(ref _encrypted);
            }
            if (streamable)
            {
                Interlocked.Increment(ref _streamable);
            }
            switch (status)
            {
                case MigrationStatus.Success:
                    Interlocked.Increment(ref _successful);
                    if (deleteMigrated)
                    {
                        Interlocked.Increment(ref _deleted);
                    }
                    break;

                case MigrationStatus.Skipped:
                    Interlocked.Increment(ref _skipped);
                    break;
                case MigrationStatus.AlreadyMigrated:
                    Interlocked.Increment(ref _migrated);
                    break;
                case MigrationStatus.Failure:
                    Interlocked.Increment(ref _failed);
                    break;
                case MigrationStatus.NotMigrated:
                    Interlocked.Increment(ref _notMigrated);
                    break;
            }
        }

        public void Update(AssetMigrationResult result, MediaAssetStorageEncryptionFormat? format = null, bool deleteMigrated = false)
        {
            Update(result.Status, result.Format != null, deleteMigrated, format);
        }
    }
}
