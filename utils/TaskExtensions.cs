
namespace MediaMigrate.Utils
{
    static class TaskExtensions
    {
        /// <summary>
        /// Wait for all tasks to finish but fail quickly on the first failure.
        /// </summary>
        /// <param name="tasks"> A list of tasks to wait</param>
        /// <returns>A task that completes when all tasks succeeds or any one task fails.</returns>
        public static async Task FailFastWaitAll(this IList<Task> tasks, CancellationToken cancellationToken)
        {
            while (tasks.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var task = await Task.WhenAny(tasks);
                await task;
                tasks.Remove(task);
            }
        }
    }
}
