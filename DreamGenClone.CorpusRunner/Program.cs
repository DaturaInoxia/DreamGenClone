namespace DreamGenClone.CorpusRunner;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        RunnerOptions? options = null;
        try
        {
            options = RunnerOptions.Parse(args);
            var (report, exitCode) = await new CorpusBenchmarkRunner().RunAsync(options, cancellation.Token);
            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
            await File.WriteAllTextAsync(options.OutputPath, BenchmarkReportBuilder.Serialize(report), cancellation.Token);
            var markdownPath = Path.ChangeExtension(options.OutputPath, ".md");
            await File.WriteAllTextAsync(markdownPath, BenchmarkReportBuilder.ToMarkdown(report), cancellation.Token);
            Console.WriteLine($"Report: {options.OutputPath}");
            Console.WriteLine($"Summary: {markdownPath}");
            Console.WriteLine($"All gates passed: {report.AllGatesPassed}");
            return exitCode;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Benchmark cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            var code = GetCode(ex);
            if (options is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
                await File.WriteAllTextAsync(
                    options.OutputPath,
                    BenchmarkReportBuilder.Serialize(BenchmarkReportBuilder.CreateFailedRun(code)),
                    CancellationToken.None);
                await File.WriteAllTextAsync(
                    Path.ChangeExtension(options.OutputPath, ".md"),
                    BenchmarkReportBuilder.ToMarkdown(BenchmarkReportBuilder.CreateFailedRun(code)),
                    CancellationToken.None);
                Console.Error.WriteLine($"Failure report: {options.OutputPath}");
            }
            Console.Error.WriteLine($"{code}: {ex.GetType().Name}: {BenchmarkReportBuilder.SanitizeDetails(ex.Message)}");
            return 2;
        }
    }

    private static string GetCode(Exception exception) => exception switch
    {
        RunnerOptionsException options => options.Code,
        CorpusValidationException corpus => corpus.Code,
        DreamGenClone.Domain.ModelManager.ModelResolutionException => "configuration_resolution_failed",
        Microsoft.Data.Sqlite.SqliteException => "configuration_database_failed",
        _ => "runner_failed"
    };
}