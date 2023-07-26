namespace MediaMigrate.Contracts
{
    interface IMigrationTracker<T, TResult> where TResult : MigrationResult
    {
        Task<TResult> GetMigrationStatusAsync(T resource, CancellationToken cancellationToken);

        Task UpdateMigrationStatus(T resource, TResult result, CancellationToken cancellationToken);

        Task<IAsyncDisposable?> BeginMigrationAsync(T resource, CancellationToken cancellationToken);

        Task ResetMigrationStatus(T resource, CancellationToken cancellationToken);
    }
}
