using MediaMigrate.Ams;
using MediaMigrate.Contracts;
using System.CommandLine;

namespace MediaMigrate.Commands
{
    internal class AnalysisCommand : BaseCommand<AnalysisOptions, AssetAnalyzer>
    {
        const string CommandDescription = @"Analyze assets for migration and generate report.
Example(s):
mediamigrate analyze -s <subscriptionid> -g <resourcegroup> -n <account>
This will analyze the given media account and produce a summary report.";

        private static readonly Option<AnalysisType> _analysisType = new(
            aliases: new[] { "-t", "--analysis-type" },
            () => AnalysisType.Summary,
            description: @"The kind of analysis to do.
Summary - Summary of migration
Detailed - A detailed classification of assets,
Report - A migration report")
        {
            IsRequired = false
        };

        public AnalysisCommand() :
            base("analyze", CommandDescription)
        {
            this.AddOption(_analysisType);
        }
    }
}

