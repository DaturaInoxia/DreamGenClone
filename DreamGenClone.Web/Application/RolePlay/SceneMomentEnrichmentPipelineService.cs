using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneMomentEnrichmentPipelineService : ISceneMomentEnrichmentPipelineService
{
    public const string JobType = "SceneMomentEnrichment";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneMomentSetRepository _momentSetRepository;
    private readonly ISceneBeatProductionPlanRepository _planRepository;
    private readonly ISceneMomentEnrichmentRepository _enrichmentRepository;
    private readonly ISceneBeatAnalyzerResolver _analyzerResolver;
    private readonly SceneMomentEnrichmentSnapshotBuilder _snapshotBuilder;
    private readonly SceneMomentEnrichmentContract _contract;
    private readonly IDurableBackgroundJobQueue _jobQueue;
    private readonly TimeProvider _timeProvider;

    public SceneMomentEnrichmentPipelineService(
        ISceneMomentSetRepository momentSetRepository,
        ISceneBeatProductionPlanRepository planRepository,
        ISceneMomentEnrichmentRepository enrichmentRepository,
        ISceneBeatAnalyzerResolver analyzerResolver,
        SceneMomentEnrichmentSnapshotBuilder snapshotBuilder,
        SceneMomentEnrichmentContract contract,
        IDurableBackgroundJobQueue jobQueue,
        TimeProvider timeProvider)
    {
        _momentSetRepository = momentSetRepository;
        _planRepository = planRepository;
        _enrichmentRepository = enrichmentRepository;
        _analyzerResolver = analyzerResolver;
        _snapshotBuilder = snapshotBuilder;
        _contract = contract;
        _jobQueue = jobQueue;
        _timeProvider = timeProvider;
    }

    public Task<SceneMomentEnrichment> EnqueueAsync(
        GenerateSceneMomentEnrichmentRequest request,
        CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(request, replaceCurrent: false, cancellationToken);

    public Task<SceneMomentEnrichment> ReplaceAsync(
        GenerateSceneMomentEnrichmentRequest request,
        CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(request, replaceCurrent: true, cancellationToken);

    public async Task<SceneMomentEnrichment> EnqueueRecommendedAsync(
        string momentSetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(momentSetId))
            throw new InvalidOperationException("Moment Set id is required for recommended Moment enrichment.");
        var momentSet = await _momentSetRepository.GetAsync(momentSetId, cancellationToken)
            ?? throw new InvalidOperationException($"Moment Set '{momentSetId}' was not found.");
        if (string.IsNullOrWhiteSpace(momentSet.RecommendedMomentId))
            throw new InvalidOperationException($"Moment Set '{momentSet.Id}' has no persisted recommended Moment.");
        return await EnqueueAsync(
            new GenerateSceneMomentEnrichmentRequest(momentSet.Id, momentSet.RecommendedMomentId),
            cancellationToken);
    }

    public Task<SceneMomentEnrichment?> GetCurrentAsync(
        string momentSetId,
        string momentId,
        CancellationToken cancellationToken = default)
        => _enrichmentRepository.GetCurrentAsync(momentSetId, momentId, cancellationToken);

    public async Task CancelAsync(string enrichmentId, CancellationToken cancellationToken = default)
    {
        var enrichment = await _enrichmentRepository.GetAsync(enrichmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Moment Enrichment '{enrichmentId}' was not found.");
        if (string.IsNullOrWhiteSpace(enrichment.CurrentAttemptId))
            throw new InvalidOperationException($"Moment Enrichment '{enrichmentId}' has no current attempt.");
        var attempt = await _enrichmentRepository.GetAttemptAsync(enrichment.CurrentAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Moment enrichment attempt '{enrichment.CurrentAttemptId}' was not found.");
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _jobQueue.TryCancelAsync(attempt.JobId, now, cancellationToken);
        if (!await _enrichmentRepository.TryCancelCurrentAsync(enrichment.Id, attempt.Id, now, cancellationToken))
            throw new InvalidOperationException($"Moment Enrichment '{enrichmentId}' is not cancellable.");
    }

    private async Task<SceneMomentEnrichment> EnqueueCoreAsync(
        GenerateSceneMomentEnrichmentRequest request,
        bool replaceCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.MomentSetId) || string.IsNullOrWhiteSpace(request.MomentId))
            throw new InvalidOperationException("Moment Set id and Moment id are required for Moment enrichment.");

        var momentSet = await _momentSetRepository.GetAsync(request.MomentSetId, cancellationToken)
            ?? throw new InvalidOperationException($"Moment Set '{request.MomentSetId}' was not found.");
        var currentMomentSet = await _momentSetRepository.GetCurrentAsync(momentSet.BeatProductionPlanId, cancellationToken);
        if (currentMomentSet is null || !string.Equals(currentMomentSet.Id, momentSet.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"Moment Set '{momentSet.Id}' is no longer current.");
        if (momentSet.Status != SceneBeatCatalogueStatus.Complete)
            throw new InvalidOperationException($"Moment Set '{momentSet.Id}' is not complete.");

        var selectedMoments = momentSet.Moments
            .Where(item => string.Equals(item.MomentId, request.MomentId, StringComparison.Ordinal))
            .ToList();
        if (selectedMoments.Count != 1)
            throw new InvalidOperationException($"Selected Moment '{request.MomentId}' must be exactly one member of Moment Set '{momentSet.Id}'.");
        var selectedMoment = selectedMoments[0];

        var plan = await _planRepository.GetAsync(momentSet.BeatProductionPlanId, cancellationToken)
            ?? throw new InvalidOperationException($"Beat Production Plan '{momentSet.BeatProductionPlanId}' was not found.");
        var currentPlan = await _planRepository.GetCurrentAsync(plan.CatalogueId, plan.BeatId, cancellationToken);
        if (currentPlan is null || !string.Equals(currentPlan.Id, plan.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"Beat Production Plan '{plan.Id}' is no longer current.");
        if (plan.Status != SceneBeatCatalogueStatus.Complete)
            throw new InvalidOperationException($"Beat Production Plan '{plan.Id}' is not complete.");
        if (!string.Equals(momentSet.CatalogueId, plan.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(momentSet.BeatId, plan.BeatId, StringComparison.Ordinal)
            || !string.Equals(momentSet.BeatProductionPlanId, plan.Id, StringComparison.Ordinal)
            || momentSet.BeatProductionPlanVersion != plan.Version)
            throw new InvalidOperationException("Moment enrichment parent lineage does not match.");

        var current = await _enrichmentRepository.GetCurrentAsync(momentSet.Id, selectedMoment.MomentId, cancellationToken);
        if (!replaceCurrent && current is { Status: SceneBeatCatalogueStatus.Pending or SceneBeatCatalogueStatus.Processing or SceneBeatCatalogueStatus.Complete })
            return current;
        if (!replaceCurrent && current is { Status: SceneBeatCatalogueStatus.Failed or SceneBeatCatalogueStatus.Cancelled })
            throw new InvalidOperationException("The current Moment Enrichment failed or was cancelled. Use the explicit replace operation.");
        if (replaceCurrent && current is { Status: SceneBeatCatalogueStatus.Pending or SceneBeatCatalogueStatus.Processing })
            throw new InvalidOperationException("Moment enrichment is already active for the selected Moment.");

        var analyzer = await _analyzerResolver.ResolveAsync(cancellationToken);
        var snapshot = _snapshotBuilder.Build(selectedMoment, momentSet, plan);
        var messages = _contract.BuildMessages(snapshot);
        var executionSnapshot = SceneBeatAnalyzerExecutionSnapshot.FromResolved(analyzer);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var enrichmentId = Guid.NewGuid().ToString();
        var attemptId = Guid.NewGuid().ToString();
        var jobId = Guid.NewGuid().ToString();
        var enrichment = new SceneMomentEnrichment
        {
            Id = enrichmentId,
            CatalogueId = plan.CatalogueId,
            BeatId = plan.BeatId,
            BeatProductionPlanId = plan.Id,
            BeatProductionPlanVersion = plan.Version,
            MomentSetId = momentSet.Id,
            MomentSetVersion = momentSet.Version,
            MomentId = selectedMoment.MomentId,
            Revision = 0,
            CurrentAttemptId = attemptId,
            SchemaVersion = snapshot.SchemaVersion,
            PromptContractVersion = messages.ContractVersion,
            MomentSnapshotJson = _snapshotBuilder.SerializeMomentSnapshot(snapshot),
            TurnEvidenceSnapshotJson = _snapshotBuilder.SerializeEvidenceSnapshot(snapshot),
            ExecutionSettingsJson = JsonSerializer.Serialize(executionSnapshot, JsonOptions),
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId,
            OwnerRecordId = enrichmentId,
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
            PayloadJson = JsonSerializer.Serialize(new SceneMomentEnrichmentJobPayload(enrichmentId, attemptId), JsonOptions),
            DedupeKey = $"scene-moment-enrichment:{momentSet.Id}:{momentSet.Version}:{selectedMoment.MomentId}:{enrichmentId}",
            MaxAttempts = analyzer.RetryDelaysSeconds.Count + 1,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _enrichmentRepository.CreateRevisionAsync(enrichment, attempt, cancellationToken);
        try
        {
            if (!await _jobQueue.TryEnqueueAsync(job, cancellationToken))
                throw new InvalidOperationException("The durable Moment enrichment job was not accepted.");
        }
        catch
        {
            attempt.ValidationCode = "scene_moment_enrichment_enqueue_rejected";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(
                new { message = "The durable Moment enrichment job was not accepted." }, JsonOptions);
            await _enrichmentRepository.TryFailAttemptAsync(
                enrichment.Id,
                attempt,
                attempt.ValidationCode,
                "The durable Moment enrichment job was not accepted.",
                now,
                CancellationToken.None);
            throw;
        }
        return enrichment;
    }
}
