using MediaMigrate.Ams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;

namespace MediaMigrate.Commands
{
    abstract class BaseCommand<TOptions, THandler> : Command
        where TOptions : notnull
        where THandler : BaseMigrator
    {
        public BaseCommand(string name, string description) : base(name, description)
        {
            Handler = CommandHandler.Create<IHost, TOptions, CancellationToken>(ExecuteAsync);
        }

        async Task ExecuteAsync(IHost host, TOptions options, CancellationToken cancellationToken)
        {
            var handler = (THandler)ActivatorUtilities.CreateInstance(host.Services, typeof(THandler), options);
            await handler.MigrateAsync(cancellationToken);
        }

        public object GetOptions(InvocationContext context)
        {
            var binder = new ModelBinder<TOptions>();
            return binder.CreateInstance(context.BindingContext)!;
        }
    }
}
