using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneBeatProductionPipelineService : ISceneBeatProductionPipelineService
{
    public const string JobType = "SceneBeatProductionPlan";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneBeatCatalogueRepository _catalogueRepository;
    private readonly ISceneBeatProductionPlanRepository _planRepository;
    private readonly ISceneBeatAnalyzerResolver _analyzerResolver;
    private readonly SceneBeatProductionSnapshotBuilder _snapshotBuilder;
    private readonly SceneBeatProductionContract _contract;
    private readonly IDurableBackgroundJobQueue _jobQueue;
    private readonly TimeProvider _timeProvider;

    public SceneBeatProductionPipelineService(
        ISceneBeatCatalogueRepository catalogueRepository,
        ISceneBeatProductionPlanRepository planRepository,
        ISceneBeatAnalyzerResolver analyzerResolver,
        SceneBeatProductionSnapshotBuilder snapshotBuilder,
        SceneBeatProductionContract contract,
        IDurableBackgroundJobQueue jobQueue,
        TimeProvider timeProvider)
    {
        _catalogueRepository = catalogueRepository;
        _planRepository = planRepository;
        _analyzerResolver = analyzerResolver;
        _snapshotBuilder = snapshotBuilder;
        _contract = contract;
        _jobQueue = jobQueue;
        _timeProvider = timeProvider;
    }

    public Task<SceneBeatProductionPlan> EnqueueAsync(
        GenerateSceneBeatProductionPlanRequest request,
        CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(request, replaceCurrent: false, cancellationToken);

    public Task<SceneBeatProductionPlan> ReplaceAsync(
        GenerateSceneBeatProductionPlanRequest request,
        CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(request, replaceCurrent: true, cancellationToken);

    public Task<SceneBeatProductionPlan?> GetCurrentAsync(
        string catalogueId,
        string beatId,
        CancellationToken cancellationToken = default)
        => _planRepository.GetCurrentAsync(catalogueId, beatId, cancellationToken);

    public async Task CancelAsync(string planId, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.GetAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Beat Production Plan '{planId}' was not found.");
        if (string.IsNullOrWhiteSpace(plan.CurrentAttemptId))
            throw new InvalidOperationException($"Beat Production Plan '{planId}' has no current attempt.");
        var attempt = await _planRepository.GetAttemptAsync(plan.CurrentAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Beat Production attempt '{plan.CurrentAttemptId}' was not found.");
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _jobQueue.TryCancelAsync(attempt.JobId, now, cancellationToken);
        if (!await _planRepository.TryCancelCurrentAsync(plan.Id, attempt.Id, now, cancellationToken))
            throw new InvalidOperationException($"Beat Production Plan '{planId}' is not cancellable.");
    }

    private async Task<SceneBeatProductionPlan> EnqueueCoreAsync(
        GenerateSceneBeatProductionPlanRequest request,
        bool replaceCurrent,
        CancellationToken cancellationToken)
    {
        RequireRequest(request);
        var catalogue = await _catalogueRepository.GetAsync(request.CatalogueId, cancellationToken)
            ?? throw new InvalidOperationException($"Beat Catalogue '{request.CatalogueId}' was not found.");
        var currentCatalogue = await _catalogueRepository.GetCurrentByTurnAsync(
            catalogue.SessionId, catalogue.TurnId, cancellationToken);
        if (currentCatalogue is null || !string.Equals(currentCatalogue.Id, catalogue.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"Beat Catalogue '{catalogue.Id}' is no longer current.");
        if (catalogue.Status != SceneBeatCatalogueStatus.Complete)
            throw new InvalidOperationException($"Beat Catalogue '{catalogue.Id}' is not complete.");
        var entry = catalogue.Entries.SingleOrDefault(item => string.Equals(item.BeatId, request.BeatId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Beat '{request.BeatId}' was not found in catalogue '{catalogue.Id}'.");

        var current = await _planRepository.GetCurrentAsync(catalogue.Id, entry.BeatId, cancellationToken);
        if (current is { Status: SceneBeatCatalogueStatus.Pending or SceneBeatCatalogueStatus.Processing })
            throw new InvalidOperationException("Beat production is already active for the selected Beat.");
        if (!replaceCurrent && current is not null)
            throw new InvalidOperationException("A Beat Production Plan already exists. Use the explicit replace operation.");

        var analyzer = await _analyzerResolver.ResolveAsync(cancellationToken);
        var snapshot = _snapshotBuilder.Build(catalogue, entry);
        var messages = _contract.BuildMessages(snapshot);
        var executionSnapshot = SceneBeatAnalyzerExecutionSnapshot.FromResolved(analyzer);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var planId = Guid.NewGuid().ToString();
        var attemptId = Guid.NewGuid().ToString();
        var jobId = Guid.NewGuid().ToString();
        var plan = new SceneBeatProductionPlan
        {
            Id = planId,
            CatalogueId = catalogue.Id,
            BeatId = entry.BeatId,
            CatalogueVersion = catalogue.Version,
            Version = 0,
            CurrentAttemptId = attemptId,
            SchemaVersion = snapshot.SchemaVersion,
            PromptContractVersion = messages.ContractVersion,
            SourceSnapshotJson = _snapshotBuilder.Serialize(snapshot),
            ExecutionSettingsJson = JsonSerializer.Serialize(executionSnapshot, JsonOptions),
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId,
            OwnerRecordId = planId,
            AttemptNumber = 1,
            JobId = jobId,
            SystemPrompt = messages.SystemPrompt,
            UserPrompt = messages.UserPrompt,
            ValidationDetailsJson = "{}",
            InputCharacters = messages.SystemPrompt.Length + messages.UserPrompt.Length,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var job = new DurableBackgroundJob
        {
            Id = jobId,
            JobType = JobType,
            Lane = DurableJobLane.TextAnalysis,
            PayloadJson = JsonSerializer.Serialize(new SceneBeatProductionPlanJobPayload(planId, attemptId), JsonOptions),
            DedupeKey = $"scene-beat-production:{catalogue.Id}:{entry.BeatId}:{catalogue.Version}:{planId}",
            MaxAttempts = analyzer.RetryDelaysSeconds.Count + 1,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _planRepository.CreateVersionAsync(plan, attempt, cancellationToken);
        try
        {
            if (!await _jobQueue.TryEnqueueAsync(job, cancellationToken))
                throw new InvalidOperationException("The durable Beat Production job was not accepted.");
        }
        catch
        {
            attempt.ValidationCode = "scene_beat_production_enqueue_rejected";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(
                new { message = "The durable Beat Production job was not accepted." }, JsonOptions);
            await _planRepository.TryFailAttemptAsync(
                plan.Id, attempt, attempt.ValidationCode,
                "The durable Beat Production job was not accepted.", now, CancellationToken.None);
            throw;
        }
        return plan;
    }

    private static void RequireRequest(GenerateSceneBeatProductionPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CatalogueId) || string.IsNullOrWhiteSpace(request.BeatId))
            throw new InvalidOperationException("Catalogue id and Beat id are required for Beat production.");
    }
}