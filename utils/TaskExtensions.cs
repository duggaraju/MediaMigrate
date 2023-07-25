
namespace MediaMigrate.Utils
{
    static class TaskExtensions
    {
        public static async Task WaitAllThrowOnFirstError(this IList<Task> tasks)
        {
            while (tasks.Count > 0)
            {
                var task = await Task.WhenAny(tasks);
                await task;
                tasks.Remove(task);
            }
        }
    }
}
