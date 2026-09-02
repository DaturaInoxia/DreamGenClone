using DreamGenClone.CorpusRunner;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CorpusRunnerTests
{
    private static readonly string Root = RepositoryRootLocator.Find();
    private static readonly string CorpusPath = Path.Combine(
        Root, "specs", "Planning", "B-100-progressive-scene-beat-pipeline", "fixtures", "corpus.json");

    [Fact]
    public async Task CorpusLoader_LoadsEightStrictSanitizedCasesWithStableChecksum()
    {
        var loader = new CorpusLoader();

        var first = await loader.LoadAsync(CorpusPath);
        var second = await loader.LoadAsync(CorpusPath);

        Assert.Equal("b100-corpus-v1", first.Version);
        Assert.Equal(8, first.Cases.Count);
        Assert.Equal(first.ChecksumSha256, second.ChecksumSha256);
        Assert.Equal(64, first.ChecksumSha256.Length);
        Assert.Equal(8, first.Cases.Select(item => item.Category).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(first.Cases, item => item.Category == "long-complex-turn");
        Assert.Contains(first.Cases, item => item.Category == "malformed-missing-narrative");
        Assert.All(first.Cases.Where(item => item.Expectations is not null), item =>
        {
            Assert.InRange(item.Expectations!.SelectedBeatOrdinal, 1, item.Expectations.BeatBoundaries.Count);
            Assert.Equal(2, item.Expectations.Moments.Minimum);
            Assert.Equal(4, item.Expectations.Moments.Maximum);
            Assert.True(item.Expectations.Moments.RecommendedRequired);
        });
    }

    [Fact]
    public async Task MissingNarrative_IsRejectedByCanonicalPreflightAndExcludedFromExecutionDenominators()
    {
        var corpus = await new CorpusLoader().LoadAsync(CorpusPath);
        var malformed = Assert.Single(corpus.Cases, item => item.ExpectedPreflightRejection is not null);

        var exception = Assert.Throws<InvalidOperationException>(() => new SceneBeatCatalogueSnapshotBuilder().Build(
            CorpusCaseMapper.CreateFullTurn(malformed),
            CorpusCaseMapper.CreateSession(malformed),
            CorpusCaseMapper.CreateCharacters(malformed)));
        Assert.Contains("exactly one authoritative Narrative", exception.Message, StringComparison.Ordinal);

        var executions = new[] { ExecutionWithDurations(15_000, 20_000, 10_000, 20_000) };
        var preflight = new[] { new PreflightRejectionReport(malformed.Id, "missing_narrative", "missing_narrative", true) };
        var (aggregates, _, _) = BenchmarkReportBuilder.Aggregate(executions, preflight);
        Assert.All(aggregates, aggregate => Assert.Equal(1, aggregate.Attempted));
    }

    [Fact]
    public void NearestRankPercentile_IsDeterministicForOddAndEvenSamples()
    {
        Assert.Equal(3, BenchmarkStatistics.NearestRankPercentile([5, 1, 3, 2, 4], 50));
        Assert.Equal(4, BenchmarkStatistics.NearestRankPercentile([4, 1, 3, 2], 95));
        Assert.Equal(2, BenchmarkStatistics.NearestRankPercentile([4, 1, 3, 2], 50));
    }

    [Fact]
    public void Gates_ExactLatencyAndValidityBoundariesPass()
    {
        var executions = Enumerable.Range(1, 100)
            .Select(index => ExecutionWithDurations(15_000, 20_000, 10_000, 20_000, index == 100 ? "Invalid" : "Valid"))
            .ToList();

        var (aggregates, gates, allPassed) = BenchmarkReportBuilder.Aggregate(executions, []);

        Assert.True(allPassed);
        Assert.All(aggregates, item => Assert.Equal(99m, item.ValidityPercent));
        Assert.All(gates, item => Assert.True(item.Passed));
    }

    [Fact]
    public void Gates_FailWhenLatencyOrValidityMissesFixedThresholds()
    {
        var executions = Enumerable.Range(1, 100)
            .Select(index => ExecutionWithDurations(index > 5 ? 45_001 : 1, 20_000, 10_000, 20_000, index >= 99 ? "Invalid" : "Valid"))
            .ToList();

        var (_, gates, allPassed) = BenchmarkReportBuilder.Aggregate(executions, []);

        Assert.False(allPassed);
        Assert.False(Assert.Single(gates, item => item.TaskId == "T065").Passed);
        Assert.False(Assert.Single(gates, item => item.TaskId == "T172").Passed);
    }

    [Fact]
    public void ReportSerialization_OmitsRawProsePromptsProviderUrlAndCredentials()
    {
        var execution = new CaseExecutionReport("case-1", 1,
        [
            new StageExecutionReport(BenchmarkStages.Catalogue, "Invalid", 12, null,
                "scene_beat_output_invalid", BenchmarkReportBuilder.SanitizeDetails("scene_beat_output_invalid"), new())
        ]);
        var report = new BenchmarkReport(1, DateTime.UnixEpoch, "v1", "abc", 1, 1,
            new ConfiguredAnalyzerReport("default", "model", "provider", "model-id", "Model Name"),
            [execution], [], [], [], false, "nearest-rank");

        var json = BenchmarkReportBuilder.Serialize(report);

        Assert.DoesNotContain("RAW_ROLEPLAY_PROSE_SENTINEL", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_API_KEY_SENTINEL", json, StringComparison.Ordinal);
        Assert.DoesNotContain("https://private-provider.example", json, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawModelResponse", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Structured output failed strict stage validation.", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedRunReport_IsSanitizedAndFailsEveryGate()
    {
        var report = BenchmarkReportBuilder.CreateFailedRun("configuration_database_failed");
        var json = BenchmarkReportBuilder.Serialize(report);

        Assert.False(report.AllGatesPassed);
        Assert.Equal(5, report.Gates.Count);
        Assert.All(report.Gates, gate => Assert.False(gate.Passed));
        Assert.Equal("configuration_database_failed", report.RunFailureCode);
        Assert.Contains("configuration database was unavailable", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Source=", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunnerOptions_ResolveDefaultsAndRejectInvalidIterations()
    {
        var now = new DateTime(2026, 8, 31, 12, 34, 56, DateTimeKind.Utc);

        var options = RunnerOptions.Parse([], Root, Root, now);

        Assert.Equal(CorpusPath, options.CorpusPath);
        Assert.Equal(Path.Combine(Root, "DreamGenClone.Web", "data", "dreamgenclone.dev.db"), options.ConfigurationDatabasePath);
        Assert.Equal(Path.Combine(Root, "artifacts", "tmp", "b100-corpus-report-20260831T123456Z.json"), options.OutputPath);
        Assert.Equal(1, options.Iterations);
        Assert.Throws<RunnerOptionsException>(() => RunnerOptions.Parse(["--iterations", "0"], Root, Root, now));
        var filtered = RunnerOptions.Parse(["--case", "solo", "--stage", "catalogue"], Root, Root, now);
        Assert.Equal("solo", filtered.SelectedCaseId);
        Assert.Equal(BenchmarkStages.Catalogue, filtered.TargetStage);
        Assert.Throws<RunnerOptionsException>(() => RunnerOptions.Parse(["--stage", "unknown"], Root, Root, now));
    }

    [Fact]
    public void PartialStageGate_EvaluatesOnlyRequestedStageAndMarkdownContainsNoRawContent()
    {
        var executions = new[] { ExecutionWithDurations(15_000, 99_999, 99_999, 99_999) };
        var (aggregates, gates, allPassed) = BenchmarkReportBuilder.Aggregate(executions, [], [BenchmarkStages.Catalogue]);
        var report = new BenchmarkReport(1, DateTime.UnixEpoch, "v1", "abc", 1, 1, null,
            executions, [], aggregates, gates, allPassed, "nearest-rank");

        Assert.True(allPassed);
        Assert.Single(gates);
        Assert.Equal("T065", gates[0].TaskId);
        var markdown = BenchmarkReportBuilder.ToMarkdown(report);
        Assert.Contains("| T065 | Catalogue |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawModelResponse", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryComposition_AssignsLiveReadOnlyConnectionOnlyToConfigAndTempConnectionToAllStages()
    {
        var livePath = Path.Combine(Root, "DreamGenClone.Web", "data", "dreamgenclone.dev.db");
        var plan = RunnerDatabasePlan.Create(livePath, "architecture-test", 1);
        var connections = RunnerRepositoryConnections.FromPlan(plan);
        var composition = new RunnerComposition(plan);

        Assert.Contains(Path.GetFullPath(livePath), connections.ConfigurationConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mode=ReadOnly", connections.ConfigurationConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(plan.WorkingDatabasePath), connections.StageConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFullPath(livePath), connections.StageConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<FunctionDefaultRepository>(composition.FunctionDefaults);
        Assert.IsType<RegisteredModelRepository>(composition.Models);
        Assert.IsType<ProviderRepository>(composition.Providers);
        Assert.IsType<SceneBeatCatalogueRepository>(composition.Catalogues);
        Assert.IsType<SceneBeatProductionPlanRepository>(composition.ProductionPlans);
        Assert.IsType<SceneMomentSetRepository>(composition.MomentSets);
        Assert.IsType<SceneMomentEnrichmentRepository>(composition.Enrichments);
    }

    private static CaseExecutionReport ExecutionWithDurations(
        long catalogue,
        long production,
        long discovery,
        long enrichment,
        string status = "Valid") => new("case", 1,
        [
            Stage(BenchmarkStages.Catalogue, catalogue, status),
            Stage(BenchmarkStages.BeatProduction, production, status),
            Stage(BenchmarkStages.MomentDiscovery, discovery, status),
            Stage(BenchmarkStages.MomentEnrichment, enrichment, status)
        ]);

    private static StageExecutionReport Stage(string stage, long duration, string status)
        => new(stage, status, duration, "stop", status == "Valid" ? null : "invalid", null, new());
}