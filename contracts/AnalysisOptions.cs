namespace MediaMigrate.Contracts
{
    public enum AnalysisType
    {
        Summary,
        Detailed,
        Report
    }

    public record AnalysisOptions : QueryOptions
    {
        public AnalysisType AnalysisType { get; set; } = AnalysisType.Summary;

        public int BatchSize { get; set; } = 1;

        public string ReportFileName => $"Report_{GlobalOptions.RunId}.html";
    }
}
