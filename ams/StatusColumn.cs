using Spectre.Console;
using Spectre.Console.Rendering;

namespace MediaMigrate.Ams
{
    internal class StatusColumn : ProgressColumn
    {
        private readonly string _unit;
        private int _successful;
        private int _failed;

        public StatusColumn(string unit)
        {
            _unit = unit;
        }

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            return Markup.FromInterpolated($"{task.Value}([green]{_successful}[/]/[red]{_failed}[/])[grey]/[/]{task.MaxValue} [grey]{_unit}[/]");
        }

        public void Update(IStats stats)
        {
            _successful = stats.Successful;
            _failed = stats.Failed;
        }
    }
}
