
namespace MediaMigrate.Contracts
{
    interface ITransform<in T, TResult> where TResult : MigrationResult
    {
        /// <summary>
        /// Run the transform.
        /// </summary>
        /// <param name="resource">The resource to be transformed</param>
        Task<TResult> RunAsync(T resource, CancellationToken cancellationToken);
    }

    interface ITransform<in T> : ITransform<T, MigrationResult>
    {
    }
}
