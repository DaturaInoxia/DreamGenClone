using System.Diagnostics;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.Models;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.CorpusRunner;

public sealed class CorpusBenchmarkRunner
{
    private readonly CorpusLoader _loader = new();

    public async Task<(BenchmarkReport Report, int ExitCode)> RunAsync(RunnerOptions options, CancellationToken cancellationToken)
    {
        if (!File.Exists(options.ConfigurationDatabasePath))
            throw new RunnerOptionsException("configuration_database_missing", $"Configuration database was not found: {options.ConfigurationDatabasePath}");

        var corpus = await _loader.LoadAsync(options.CorpusPath, cancellationToken);
        var selectedCases = string.IsNullOrWhiteSpace(options.SelectedCaseId)
            ? corpus.Cases
            : corpus.Cases.Where(item => string.Equals(item.Id, options.SelectedCaseId, StringComparison.Ordinal)).ToList();
        if (selectedCases.Count == 0)
            throw new RunnerOptionsException("runner_case_not_found", $"Corpus case '{options.SelectedCaseId}' was not found.");
        var executions = new List<CaseExecutionReport>();
        var preflight = new List<PreflightRejectionReport>();
        ConfiguredAnalyzerReport? configuredAnalyzer = null;

        foreach (var corpusCase in selectedCases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (corpusCase.ExpectedPreflightRejection is not null)
            {
                preflight.Add(RunPreflight(corpusCase));
                continue;
            }

            for (var iteration = 1; iteration <= options.Iterations; iteration++)
            {
                var databasePlan = RunnerDatabasePlan.Create(options.ConfigurationDatabasePath, corpusCase.Id, iteration);
                try
                {
                    var result = await RunExecutionAsync(corpusCase, iteration, databasePlan, options.TargetStage, cancellationToken);
                    configuredAnalyzer ??= result.Analyzer;
                    executions.Add(result.Execution);
                }
                finally
                {
                    if (!options.KeepWorkingDatabase)
                        DeleteSqliteFiles(databasePlan.WorkingDatabasePath);
                    else
                        Console.WriteLine($"Working database retained: {databasePlan.WorkingDatabasePath}");
                }
            }
        }

        var requiredStages = options.TargetStage is null ? BenchmarkStages.All : [options.TargetStage];
        var (aggregates, gates, allPassed) = BenchmarkReportBuilder.Aggregate(executions, preflight, requiredStages);
        var report = new BenchmarkReport(
            1,
            DateTime.UtcNow,
            corpus.Version,
            corpus.ChecksumSha256,
            selectedCases.Count,
            executions.Count,
            configuredAnalyzer,
            executions,
            preflight,
            aggregates,
            gates,
            allPassed,
            "Nearest-rank: sort durations ascending and select rank ceil(P/100 * N), using a one-based rank.");
        return (report, allPassed ? 0 : 1);
    }

    private static PreflightRejectionReport RunPreflight(FrozenCorpusCase corpusCase)
    {
        try
        {
            new SceneBeatCatalogueSnapshotBuilder().Build(
                CorpusCaseMapper.CreateFullTurn(corpusCase),
                CorpusCaseMapper.CreateSession(corpusCase),
                CorpusCaseMapper.CreateCharacters(corpusCase));
            return new(corpusCase.Id, corpusCase.ExpectedPreflightRejection!.Code, "none", false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("exactly one authoritative Narrative", StringComparison.Ordinal))
        {
            return new(corpusCase.Id, corpusCase.ExpectedPreflightRejection!.Code, "missing_narrative", true);
        }
    }

    private static async Task<(ConfiguredAnalyzerReport Analyzer, CaseExecutionReport Execution)> RunExecutionAsync(
        FrozenCorpusCase corpusCase,
        int iteration,
        RunnerDatabasePlan databasePlan,
        string? targetStage,
        CancellationToken cancellationToken)
    {
        var composition = new RunnerComposition(databasePlan);
        var resolved = await composition.AnalyzerResolver.ResolveAsync(cancellationToken);
        var model = await composition.Models.GetByIdAsync(resolved.ModelId, cancellationToken)
            ?? throw new InvalidOperationException("The resolved analyzer model disappeared during benchmark setup.");
        var analyzerReport = new ConfiguredAnalyzerReport(
            resolved.FunctionDefaultId, resolved.ModelId, resolved.ProviderId, resolved.Model.ModelIdentifier, model.DisplayName);

        var services = new ServiceCollection();
        services.AddHttpClient("StructuredTextCompletionClient");
        await using var serviceProvider = services.BuildServiceProvider();
        var completion = new OpenAiStructuredTextCompletionClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            new ApiKeyEncryptionService(),
            NullLogger<OpenAiStructuredTextCompletionClient>.Instance);
        var queue = new RecordingDurableQueue();
        var stages = new List<StageExecutionReport>();
        var timeProvider = TimeProvider.System;
        var session = CorpusCaseMapper.CreateSession(corpusCase);
        var turn = CorpusCaseMapper.CreateTurn(corpusCase);
        var characters = CorpusCaseMapper.CreateCharacters(corpusCase);

        var catalogueBuilder = new SceneBeatCatalogueSnapshotBuilder();
        var catalogueService = new SceneBeatPipelineService(
            new FrozenSessionReader(session), new FrozenTurnReader(turn), new FrozenScenarioReader(session.ScenarioId!, characters),
            composition.AnalyzerResolver, catalogueBuilder, new SceneBeatCatalogueContract(catalogueBuilder),
            composition.Catalogues, queue, timeProvider);
        var catalogue = await catalogueService.EnqueueCatalogueAsync(new(session.Id, turn.TurnId), cancellationToken);
        stages.Add(await ExecuteStageAsync(
            BenchmarkStages.Catalogue,
            queue.TakeLast(SceneBeatPipelineService.CatalogueJobType),
            new SceneBeatCatalogueJobHandler(composition.Catalogues, composition.Providers, completion, new SceneBeatCatalogueContract(catalogueBuilder), timeProvider),
            () => composition.Catalogues.GetAttemptAsync(catalogue.CurrentAttemptId!, cancellationToken),
            () => composition.Catalogues.GetAsync(catalogue.Id, cancellationToken),
            record => record is null ? new() : new(record.Id, record.Version),
            cancellationToken));
        catalogue = (await composition.Catalogues.GetAsync(catalogue.Id, cancellationToken))!;
        if (catalogue.Status != SceneBeatCatalogueStatus.Complete)
            return (analyzerReport, CompleteWithBlocked(corpusCase.Id, iteration, stages));
        var catalogueExpectationCode = CorpusExpectationEvaluator.ValidateCatalogue(corpusCase, catalogue);
        if (catalogueExpectationCode is not null)
            stages[^1] = stages[^1] with { Status = "Invalid", ValidationCode = catalogueExpectationCode, Details = BenchmarkReportBuilder.SanitizeDetails(catalogueExpectationCode) };
        if (targetStage == BenchmarkStages.Catalogue)
            return (analyzerReport, new CaseExecutionReport(corpusCase.Id, iteration, stages));

        var selectedEntry = catalogue.Entries.OrderBy(item => item.Order).ElementAt(corpusCase.Expectations!.SelectedBeatOrdinal - 1);
        var productionBuilder = new SceneBeatProductionSnapshotBuilder();
        var productionService = new SceneBeatProductionPipelineService(
            composition.Catalogues, composition.ProductionPlans, composition.AnalyzerResolver,
            productionBuilder, new SceneBeatProductionContract(), queue, timeProvider);
        var plan = await productionService.EnqueueAsync(new(catalogue.Id, selectedEntry.BeatId), cancellationToken);
        stages.Add(await ExecuteStageAsync(
            BenchmarkStages.BeatProduction,
            queue.TakeLast(SceneBeatProductionPipelineService.JobType),
            new SceneBeatProductionPlanJobHandler(composition.ProductionPlans, composition.Providers, completion, new SceneBeatProductionParser(), timeProvider),
            () => composition.ProductionPlans.GetAttemptAsync(plan.CurrentAttemptId!, cancellationToken),
            () => composition.ProductionPlans.GetAsync(plan.Id, cancellationToken),
            record => record is null ? new() : new(record.CatalogueId, record.CatalogueVersion, record.BeatId, record.Id, record.Version),
            cancellationToken));
        plan = (await composition.ProductionPlans.GetAsync(plan.Id, cancellationToken))!;
        if (plan.Status != SceneBeatCatalogueStatus.Complete)
            return (analyzerReport, CompleteWithBlocked(corpusCase.Id, iteration, stages));
        if (targetStage == BenchmarkStages.BeatProduction)
            return (analyzerReport, new CaseExecutionReport(corpusCase.Id, iteration, stages));

        var discoveryBuilder = new SceneMomentDiscoverySnapshotBuilder();
        var discoveryService = new SceneMomentDiscoveryPipelineService(
            composition.ProductionPlans, composition.MomentSets, composition.AnalyzerResolver,
            discoveryBuilder, new SceneMomentDiscoveryContract(), queue, timeProvider);
        var momentSet = await discoveryService.EnqueueAsync(new(plan.Id), cancellationToken);
        stages.Add(await ExecuteStageAsync(
            BenchmarkStages.MomentDiscovery,
            queue.TakeLast(SceneMomentDiscoveryPipelineService.JobType),
            new SceneBeatMomentDiscoveryJobHandler(composition.MomentSets, composition.Providers, completion, discoveryBuilder, new SceneMomentDiscoveryParser(), timeProvider),
            () => composition.MomentSets.GetAttemptAsync(momentSet.CurrentAttemptId!, cancellationToken),
            () => composition.MomentSets.GetAsync(momentSet.Id, cancellationToken),
            record => record is null ? new() : new(record.CatalogueId, null, record.BeatId, record.BeatProductionPlanId, record.BeatProductionPlanVersion, record.Id, record.Version),
            cancellationToken));
        momentSet = (await composition.MomentSets.GetAsync(momentSet.Id, cancellationToken))!;
        if (momentSet.Status != SceneBeatCatalogueStatus.Complete)
            return (analyzerReport, CompleteWithBlocked(corpusCase.Id, iteration, stages));
        var momentExpectationCode = CorpusExpectationEvaluator.ValidateMoments(corpusCase, momentSet);
        if (momentExpectationCode is not null)
            stages[^1] = stages[^1] with { Status = "Invalid", ValidationCode = momentExpectationCode, Details = BenchmarkReportBuilder.SanitizeDetails(momentExpectationCode) };
        if (targetStage == BenchmarkStages.MomentDiscovery)
            return (analyzerReport, new CaseExecutionReport(corpusCase.Id, iteration, stages));

        var enrichmentBuilder = new SceneMomentEnrichmentSnapshotBuilder();
        var enrichmentService = new SceneMomentEnrichmentPipelineService(
            composition.MomentSets, composition.ProductionPlans, composition.Enrichments, composition.AnalyzerResolver,
            enrichmentBuilder, new SceneMomentEnrichmentContract(), queue, timeProvider);
        var enrichment = await enrichmentService.EnqueueRecommendedAsync(momentSet.Id, cancellationToken);
        stages.Add(await ExecuteStageAsync(
            BenchmarkStages.MomentEnrichment,
            queue.TakeLast(SceneMomentEnrichmentPipelineService.JobType),
            new SceneMomentEnrichmentJobHandler(composition.Enrichments, composition.Providers, completion, enrichmentBuilder, new SceneMomentEnrichmentParser(), timeProvider),
            () => composition.Enrichments.GetAttemptAsync(enrichment.CurrentAttemptId!, cancellationToken),
            () => composition.Enrichments.GetAsync(enrichment.Id, cancellationToken),
            record => record is null ? new() : new(record.CatalogueId, null, record.BeatId, record.BeatProductionPlanId, record.BeatProductionPlanVersion, record.MomentSetId, record.MomentSetVersion, record.MomentId, record.Id, record.Revision),
            cancellationToken));

        return (analyzerReport, new CaseExecutionReport(corpusCase.Id, iteration, stages));
    }

    private static async Task<StageExecutionReport> ExecuteStageAsync<TRecord>(
        string stage,
        DreamGenClone.Domain.Processing.DurableBackgroundJob job,
        IDurableBackgroundJobHandler handler,
        Func<Task<SceneBeatAnalysisAttempt?>> getAttempt,
        Func<Task<TRecord?>> getRecord,
        Func<TRecord?, StageLineageReport> getLineage,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? thrownCode = null;
        try
        {
            await handler.HandleAsync(job, cancellationToken);
        }
        catch (DurableJobFailureException ex)
        {
            thrownCode = ex.ErrorCode;
        }
        stopwatch.Stop();
        var attempt = await getAttempt();
        var record = await getRecord();
        var valid = attempt?.Status == SceneBeatAnalysisAttemptStatus.Complete;
        var code = attempt?.ValidationCode ?? thrownCode;
        return new StageExecutionReport(
            stage,
            valid ? "Valid" : "Invalid",
            attempt?.DurationMs ?? stopwatch.ElapsedMilliseconds,
            attempt?.FinishReason,
            code,
            BenchmarkReportBuilder.SanitizeDetails(code),
            getLineage(record),
            attempt?.OutputCharacters);
    }

    private static CaseExecutionReport CompleteWithBlocked(string caseId, int iteration, List<StageExecutionReport> stages)
    {
        foreach (var stage in BenchmarkStages.All.Skip(stages.Count))
            stages.Add(new StageExecutionReport(stage, "NotAttempted", 0, null, "upstream_stage_failed", "An earlier stage failed; this stage was not sent to the model.", new()));
        return new CaseExecutionReport(caseId, iteration, stages);
    }

    private static void DeleteSqliteFiles(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
                File.Delete(path + suffix);
        }
    }
}