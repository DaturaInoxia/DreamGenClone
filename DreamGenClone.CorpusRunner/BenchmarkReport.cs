using System.Text.Json;

namespace DreamGenClone.CorpusRunner;

public static class BenchmarkStages
{
    public const string Catalogue = "Catalogue";
    public const string BeatProduction = "BeatProduction";
    public const string MomentDiscovery = "MomentDiscovery";
    public const string MomentEnrichment = "MomentEnrichment";
    public static readonly IReadOnlyList<string> All = [Catalogue, BeatProduction, MomentDiscovery, MomentEnrichment];
}

public sealed record BenchmarkReport(
    int ReportVersion,
    DateTime RunUtc,
    string CorpusVersion,
    string CorpusChecksumSha256,
    int CaseCount,
    int ExecutionCount,
    ConfiguredAnalyzerReport? ConfiguredAnalyzer,
    IReadOnlyList<CaseExecutionReport> Executions,
    IReadOnlyList<PreflightRejectionReport> PreflightRejections,
    IReadOnlyList<StageAggregateReport> StageAggregates,
    IReadOnlyList<GateReport> Gates,
    bool AllGatesPassed,
    string PercentileMethod,
    string? RunFailureCode = null,
    string? RunFailureDetails = null);

public sealed record ConfiguredAnalyzerReport(
    string FunctionDefaultId,
    string ModelId,
    string ProviderId,
    string ModelIdentifier,
    string ModelName);

public sealed record CaseExecutionReport(
    string CaseId,
    int Iteration,
    IReadOnlyList<StageExecutionReport> Stages);

public sealed record StageExecutionReport(
    string Stage,
    string Status,
    long DurationMs,
    string? FinishReason,
    string? ValidationCode,
    string? Details,
    StageLineageReport Lineage,
    int? OutputCharacters = null);

public sealed record StageLineageReport(
    string? CatalogueId = null,
    int? CatalogueVersion = null,
    string? BeatId = null,
    string? BeatProductionPlanId = null,
    int? BeatProductionPlanVersion = null,
    string? MomentSetId = null,
    int? MomentSetVersion = null,
    string? MomentId = null,
    string? MomentEnrichmentId = null,
    int? MomentEnrichmentRevision = null);

public sealed record PreflightRejectionReport(string CaseId, string ExpectedCode, string ActualCode, bool Passed);

public sealed record StageAggregateReport(
    string Stage,
    int Attempted,
    int Valid,
    int Invalid,
    decimal ValidityPercent,
    long? P50DurationMs,
    long? P95DurationMs);

public sealed record GateReport(
    string TaskId,
    string Stage,
    bool Passed,
    decimal RequiredValidityPercent,
    long? P50DurationMs,
    long? P50LimitMs,
    long? P95DurationMs,
    long? P95LimitMs);

public static class BenchmarkReportBuilder
{
    private static readonly IReadOnlyDictionary<string, (string TaskId, long P50, long P95)> Limits =
        new Dictionary<string, (string, long, long)>(StringComparer.Ordinal)
        {
            [BenchmarkStages.Catalogue] = ("T065", 15_000, 45_000),
            [BenchmarkStages.BeatProduction] = ("T082", 20_000, 60_000),
            [BenchmarkStages.MomentDiscovery] = ("T099", 10_000, 30_000),
            [BenchmarkStages.MomentEnrichment] = ("T119", 20_000, 60_000)
        };

    public static (IReadOnlyList<StageAggregateReport> Aggregates, IReadOnlyList<GateReport> Gates, bool AllPassed) Aggregate(
        IEnumerable<CaseExecutionReport> executions,
        IEnumerable<PreflightRejectionReport> preflightRejections,
        IReadOnlyList<string>? requiredStages = null)
    {
        var executionList = executions.ToList();
        requiredStages ??= BenchmarkStages.All;
        var aggregates = requiredStages.Select(stage => AggregateStage(stage, executionList)).ToList();
        var gates = aggregates.Select(aggregate => BuildStageGate(aggregate, Limits[aggregate.Stage])).ToList();
        var stageGatesPassed = gates.All(gate => gate.Passed);
        var preflightPassed = preflightRejections.All(item => item.Passed);
        if (requiredStages.Count == BenchmarkStages.All.Count)
            gates.Add(new GateReport("T172", "AllStages", stageGatesPassed && preflightPassed, 99m, null, null, null, null));
        return (aggregates, gates, gates.All(gate => gate.Passed));
    }

    public static string SanitizeDetails(string? validationCode)
        => validationCode switch
        {
            null => null!,
            "expected_contract_mismatch" => "Output did not satisfy the frozen human-reviewed expectations.",
            var code when code.Contains("output_invalid", StringComparison.Ordinal) => "Structured output failed strict stage validation.",
            var code when code.Contains("transport", StringComparison.Ordinal) || code.Contains("http", StringComparison.Ordinal) => "The configured provider request failed.",
            var code when code.Contains("credential", StringComparison.Ordinal) => "The configured provider credential was unavailable or invalid.",
            var code when code.Contains("configuration", StringComparison.Ordinal) => "The configured analyzer or configuration database was unavailable or invalid.",
            var code when code.Contains("corpus", StringComparison.Ordinal) => "The frozen corpus failed strict loading or expectation validation.",
            _ => "The stage failed with the reported stable code."
        };

    public static string Serialize(BenchmarkReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    public static string ToMarkdown(BenchmarkReport report)
    {
        var lines = new List<string>
        {
            "# B-100 Corpus Benchmark",
            string.Empty,
            $"**Run UTC:** {report.RunUtc:yyyy-MM-dd HH:mm:ss}Z  ",
            $"**Corpus:** {report.CorpusVersion} ({report.CorpusChecksumSha256})  ",
            $"**Executions:** {report.ExecutionCount}  ",
            $"**All reported gates passed:** {report.AllGatesPassed}",
            string.Empty,
            "| Task | Stage | Validity | p50 | Limit | p95 | Limit | Passed |",
            "|---|---|---:|---:|---:|---:|---:|---|"
        };
        foreach (var gate in report.Gates)
        {
            var aggregate = report.StageAggregates.FirstOrDefault(item => item.Stage == gate.Stage);
            lines.Add($"| {gate.TaskId} | {gate.Stage} | {aggregate?.ValidityPercent ?? 0}% | {FormatMs(gate.P50DurationMs)} | {FormatMs(gate.P50LimitMs)} | {FormatMs(gate.P95DurationMs)} | {FormatMs(gate.P95LimitMs)} | {gate.Passed} |");
        }
        if (report.RunFailureCode is not null)
            lines.AddRange([string.Empty, $"**Run failure:** `{report.RunFailureCode}` - {report.RunFailureDetails}"]);
        lines.AddRange([string.Empty, $"Percentiles: {report.PercentileMethod}"]);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string FormatMs(long? value) => value is null ? "n/a" : $"{value} ms";

    public static BenchmarkReport CreateFailedRun(string failureCode)
    {
        var (aggregates, gates, _) = Aggregate([], []);
        return new BenchmarkReport(
            1,
            DateTime.UtcNow,
            "unavailable",
            "unavailable",
            0,
            0,
            null,
            [],
            [],
            aggregates,
            gates,
            false,
            "Nearest-rank: sort durations ascending and select rank ceil(P/100 * N), using a one-based rank.",
            failureCode,
            SanitizeDetails(failureCode));
    }

    private static StageAggregateReport AggregateStage(string stage, IReadOnlyList<CaseExecutionReport> executions)
    {
        var attempts = executions.SelectMany(item => item.Stages)
            .Where(item => item.Stage == stage && item.Status != "NotAttempted")
            .ToList();
        var valid = attempts.Count(item => item.Status == "Valid");
        var durations = attempts.Select(item => item.DurationMs).ToArray();
        return new StageAggregateReport(
            stage,
            attempts.Count,
            valid,
            attempts.Count - valid,
            attempts.Count == 0 ? 0 : Math.Round(valid * 100m / attempts.Count, 4),
            durations.Length == 0 ? null : BenchmarkStatistics.NearestRankPercentile(durations, 50),
            durations.Length == 0 ? null : BenchmarkStatistics.NearestRankPercentile(durations, 95));
    }

    private static GateReport BuildStageGate(StageAggregateReport aggregate, (string TaskId, long P50, long P95) limit)
    {
        var passed = aggregate.Attempted > 0
            && aggregate.ValidityPercent >= 99m
            && aggregate.P50DurationMs <= limit.P50
            && aggregate.P95DurationMs <= limit.P95;
        return new GateReport(limit.TaskId, aggregate.Stage, passed, 99m, aggregate.P50DurationMs, limit.P50, aggregate.P95DurationMs, limit.P95);
    }
}