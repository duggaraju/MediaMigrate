using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using MediaMigrate.Ams;
using MediaMigrate.Aws;
using MediaMigrate.Azure;
using MediaMigrate.Contracts;
using MediaMigrate.Gcp;
using MediaMigrate.Local;
using MediaMigrate.Log;
using MediaMigrate.Transform;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using Vertical.SpectreLogger;
using Vertical.SpectreLogger.Core;

namespace MediaMigrate
{
    static class DependencyInjection
    {
        /// <summary>
        /// Cloud providers.
        /// </summary>
        static readonly Dictionary<CloudType, Type> CloudProviders = new()
        {
            [CloudType.Azure] = typeof(AzureProvider),
            [CloudType.AWS] = typeof(AWSProvider),
            [CloudType.GCP] = typeof(GCSProvider),
            [CloudType.Local] = typeof(LocalFileProvider),
            // [CloudType.Custom] = typeof(LocalFileProvider), register your custom cloud provider here.
        };

        public static CommandLineBuilder UseDependencyInjection(this CommandLineBuilder builder)
        {
            builder.AddMiddleware(async (context, next) =>
            {
                var host = context.BindingContext.GetRequiredService<IHost>();
                var options = host.Services.GetRequiredService<GlobalOptions>();
                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                logger.LogDebug("Writing logs to {file}", options.LogFile);
                await next(context);
                logger.LogInformation("See file {file} for detailed logs.", options.LogFile);
            });
            return builder;
        }

        private static bool LogFileFilter(LogLevel level) => true;

        private static bool ConsoleFilter(in LogEventContext context) => context.EventId != Events.ShakaPackager;

        public static IServiceCollection SetupLogging(this IServiceCollection services, GlobalOptions options)
        {
            services.AddLogging(builder =>
            {
                var logSwitch = new SourceSwitch("migration")
                {
                    Level = SourceLevels.All
                };

                builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddFilter(LogFileFilter)
                    .AddSpectreConsole(builder =>
                        builder
                            .SetLogEventFilter(ConsoleFilter)
                            .SetMinimumLevel(options.LogLevel)
                            .WriteInForeground())
                    .AddTraceSource(logSwitch, new TextWriterTraceListener(options.LogFile));
            });
            return services;
        }

        public static void RegisterServices(this IServiceCollection services)
        {
            services
                .AddSingleton(CreateConsole)
                .AddSingleton(GetCredential())
                .AddSingleton<AssetAnalyzer>()
                .AddSingleton<StorageMigrator>()
                .AddSingleton<AssetMigrator>()
                .AddSingleton<KeysMigrator>()
                .AddSingleton<IMigrationTracker<BlobContainerClient, AssetMigrationResult>, AssetMigrationTracker>()
                .AddSingleton<TemplateMapper>()
                .AddSingleton<TransMuxer>()
                .AddSingleton<TransformFactory>()
                .AddSingleton<PackagerFactory>()
                .AddSingleton<AzureResourceProvider>()
                .AddSingleton(GetCloudProvider)
                .AddSingleton(GetAWSCredentials);
        }

        private static TokenCredential GetCredential()
        {
            return new DefaultAzureCredential(
                new DefaultAzureCredentialOptions
                {
                    // Disable shared token cache since it needs X11 on Linux.
                    ExcludeSharedTokenCacheCredential = OperatingSystem.IsLinux(),
                    ExcludeInteractiveBrowserCredential = false
                });
        }

        private static AWSCredentials GetAWSCredentials(IServiceProvider provider)
        {
            var options = provider.GetRequiredService<GlobalOptions>();
            if (options.CloudType != CloudType.AWS)
            {
                throw new NotImplementedException("AWSCredentials requested when cloud type is not AWS");
            }
            var chain = new CredentialProfileStoreChain();
            var profile = Environment.GetEnvironmentVariable("AWS_PROFILE");
            if (profile != null &&
                chain.TryGetAWSCredentials(profile, out var credentials))
            {
                return credentials;
            }
            throw new InvalidOperationException("AWS credentials not found!! Set environment variable AWS_PROFILE to the profile to use.");
        }

        static ICloudProvider GetCloudProvider(IServiceProvider provider)
        {
            var options = provider.GetRequiredService<GlobalOptions>();
            return (ICloudProvider)ActivatorUtilities.CreateInstance(provider, CloudProviders[options.CloudType]);
        }

        static IAnsiConsole CreateConsole(IServiceProvider provider)
        {
            var daemonMode = provider.GetRequiredService<GlobalOptions>().DaemonMode;
            var settings = new AnsiConsoleSettings
            {
                Ansi = daemonMode ? AnsiSupport.No : AnsiSupport.Detect,
                ColorSystem = daemonMode ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
                Interactive = daemonMode ? InteractionSupport.No : InteractionSupport.Detect
            };
            return AnsiConsole.Create(settings);
        }

        public static void SetupHost(this IHostBuilder builder)
        {
            var context = (InvocationContext)builder.Properties[typeof(InvocationContext)];
            var globalOptions = (GlobalOptions)new ModelBinder<GlobalOptions>().CreateInstance(context.BindingContext)!;
            builder.ConfigureServices(collection =>
            {
                collection
                    .AddSingleton(globalOptions)
                    .SetupLogging(globalOptions)
                    .RegisterServices();
            });
        }
    }
}
