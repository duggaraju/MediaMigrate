using MediaMigrate.Commands;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.Text;

namespace MediaMigrate
{
    internal class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var transformsCommand = new Command("transforms", "Migrate Transforms")
            {
                Handler = CommandHandler.Create(NotImplemented)
            };

            var eventsCommand = new Command("liveevents", "Migrate Live Events")
            {
                Handler = CommandHandler.Create(NotImplemented)
            };

            var rootCommand = new RootCommand("Azure Media Services Migration Tool")
            {
                new AnalysisCommand(),
                new AssetsCommand(),
                new StorageCommand(),
                new KeysCommand(),
                eventsCommand,
                transformsCommand
            }.AddGlobalOptions();

            var parser = new CommandLineBuilder(rootCommand)
                .UseDefaults()
                .UseHost(builder => builder.SetupHost())
                .UseDependencyInjection()
                .UseHelp(ctx =>
                {
                    ctx.HelpBuilder.CustomizeLayout(_ =>
                    {
                        return HelpBuilder.Default
                            .GetLayout()
                            .Skip(1)
                            .Prepend(_ =>
                            {
                                AnsiConsole.Write(
                                    new FigletText(rootCommand.Description!)
                                    .Color(Color.CadetBlue)
                                    .Centered());
                            });
                    });
                })
                .Build();
            return await parser.InvokeAsync(args);
        }

        private static void NotImplemented(InvocationContext context)
        {
            Console.Error.WriteLine("Command is not implemented yet!!");
            context.ExitCode = -1;
        }
    }
}
