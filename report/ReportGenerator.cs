using MediaMigrate.Ams;
using System.Reflection;
using System.Text;

namespace MediaMigrate.Report
{
    sealed class ReportGenerator : IDisposable
    {
        private readonly TextWriter _writer;
        public readonly string FileName;

        public ReportGenerator(string file, Stream stream)
        {
            FileName = file;
            _writer = new StreamWriter(stream, Encoding.UTF8);
        }

        public void Dispose()
        {
            _writer.Dispose();
        }

        public async Task WriteStyleSheetAsync(string fileName)
        {
            using var file = File.OpenWrite(fileName);
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
"MediaMigrate.report.style.css") ?? throw new InvalidOperationException("Stream is null");
            await stream.CopyToAsync(file);
        }

        public void WriteHeader(string styleSheet)
        {
            _writer.WriteLine(@$"
<html>
  <head>
    <link rel=""stylesheet"" href=""{styleSheet}"">
  </head>
  <body>
    <h1>Asset Migration Report</h1>
    <table>
      <thead>
      <tr>
        <th style=""width:30%"">Asset Name</t>
        <th style=""width:10%"">Migration Status</th>
        <th style=""width:60%"">Migration URL</th>
      </tr>
      </thead>
      <tbody>");
        }

        public void WriteTrailer()
        {
            _writer.WriteLine(@"
      </tbody>
    </table>
  </body>
</html>");
        }

        public void WriteRows(IEnumerable<AnalysisResult> results)
        {
            foreach (var result in results)
            {
                _writer.Write($"<tr><td>{result.AssetName}</td><td>{result.Status}</td><td>");
                if (result.Uri != null)
                {
                    _writer.Write($"<a href=\"{result.Uri}\">{result.Uri}</a>");
                    _writer.WriteLine($"</td></tr>");
                }
            }
        }
    }
}

