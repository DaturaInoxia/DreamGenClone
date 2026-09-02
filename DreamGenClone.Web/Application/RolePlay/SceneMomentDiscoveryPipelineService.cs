using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneMomentDiscoveryPipelineService : ISceneMomentDiscoveryPipelineService
{
    public const string JobType = "SceneMomentDiscovery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneBeatProductionPlanRepository _planRepository;
    private readonly ISceneMomentSetRepository _momentSetRepository;
    private readonly ISceneBeatAnalyzerResolver _analyzerResolver;
    private readonly SceneMomentDiscoverySnapshotBuilder _snapshotBuilder;
    private readonly SceneMomentDiscoveryContract _contract;
    private readonly IDurableBackgroundJobQueue _jobQueue;
    private readonly TimeProvider _timeProvider;

    public SceneMomentDiscoveryPipelineService(
        ISceneBeatProductionPlanRepository planRepository,
        ISceneMomentSetRepository momentSetRepository,
        ISceneBeatAnalyzerResolver analyzerResolver,
        SceneMomentDiscoverySnapshotBuilder snapshotBuilder,
        SceneMomentDiscoveryContract contract,
        IDurableBackgroundJobQueue jobQueue,
        TimeProvider timeProvider)
    {
        _planRepository = planRepository;
        _momentSetRepository = momentSetRepository;
        _analyzerResolver = analyzerResolver;
        _snapshotBuilder = snapshotBuilder;
        _contract = contract;
        _jobQueue = jobQueue;
        _timeProvider = timeProvider;
    }

    public Task<SceneMomentSet> EnqueueAsync(
        GenerateSceneMomentsRequest request,
        CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(request, replaceCurrent: false, cancellationToken);

    public Task<SceneMomentSet> ReplaceAsync(
        GenerateSceneMomentsRequest request,
        CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(request, replaceCurrent: true, cancellationToken);

    public Task<SceneMomentSet?> GetCurrentAsync(
        string beatProductionPlanId,
        CancellationToken cancellationToken = default)
        => _momentSetRepository.GetCurrentAsync(beatProductionPlanId, cancellationToken);

    public async Task CancelAsync(string momentSetId, CancellationToken cancellationToken = default)
    {
        var momentSet = await _momentSetRepository.GetAsync(momentSetId, cancellationToken)
            ?? throw new InvalidOperationException($"Moment Set '{momentSetId}' was not found.");
        if (string.IsNullOrWhiteSpace(momentSet.CurrentAttemptId))
            throw new InvalidOperationException($"Moment Set '{momentSetId}' has no current attempt.");
        var attempt = await _momentSetRepository.GetAttemptAsync(momentSet.CurrentAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Moment discovery attempt '{momentSet.CurrentAttemptId}' was not found.");
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _jobQueue.TryCancelAsync(attempt.JobId, now, cancellationToken);
        if (!await _momentSetRepository.TryCancelCurrentAsync(momentSet.Id, attempt.Id, now, cancellationToken))
            throw new InvalidOperationException($"Moment Set '{momentSetId}' is not cancellable.");
    }

    private async Task<SceneMomentSet> EnqueueCoreAsync(
        GenerateSceneMomentsRequest request,
        bool replaceCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BeatProductionPlanId))
            throw new InvalidOperationException("Beat Production Plan id is required for Moment discovery.");

        var plan = await _planRepository.GetAsync(request.BeatProductionPlanId, cancellationToken)
            ?? throw new InvalidOperationException($"Beat Production Plan '{request.BeatProductionPlanId}' was not found.");
        var currentPlan = await _planRepository.GetCurrentAsync(plan.CatalogueId, plan.BeatId, cancellationToken);
        if (currentPlan is null || !string.Equals(currentPlan.Id, plan.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"Beat Production Plan '{plan.Id}' is no longer current.");
        if (plan.Status != SceneBeatCatalogueStatus.Complete)
            throw new InvalidOperationException($"Beat Production Plan '{plan.Id}' is not complete.");

        var current = await _momentSetRepository.GetCurrentAsync(plan.Id, cancellationToken);
        if (current is { Status: SceneBeatCatalogueStatus.Pending or SceneBeatCatalogueStatus.Processing })
            throw new InvalidOperationException("Moment discovery is already active for the selected Beat plan.");
        if (!replaceCurrent && current is not null)
            throw new InvalidOperationException("A Moment Set already exists. Use the explicit replace operation.");

        var analyzer = await _analyzerResolver.ResolveAsync(cancellationToken);
        var snapshot = _snapshotBuilder.Build(plan);
        var messages = _contract.BuildMessages(snapshot);
        var executionSnapshot = SceneBeatAnalyzerExecutionSnapshot.FromResolved(analyzer);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var momentSetId = Guid.NewGuid().ToString();
        var attemptId = Guid.NewGuid().ToString();
        var jobId = Guid.NewGuid().ToString();
        var momentSet = new SceneMomentSet
        {
            Id = momentSetId,
            CatalogueId = plan.CatalogueId,
            BeatId = plan.BeatId,
            BeatProductionPlanId = plan.Id,
            BeatProductionPlanVersion = plan.Version,
            Version = 0,
            CurrentAttemptId = attemptId,
            SchemaVersion = snapshot.SchemaVersion,
            PromptContractVersion = messages.ContractVersion,
            BeatSnapshotJson = _snapshotBuilder.SerializeBeatSnapshot(snapshot),
            TurnEvidenceSnapshotJson = _snapshotBuilder.SerializeEvidenceSnapshot(snapshot),
            ExecutionSettingsJson = JsonSerializer.Serialize(executionSnapshot, JsonOptions),
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId,
            OwnerRecordId = momentSetId,
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
            PayloadJson = JsonSerializer.Serialize(new SceneMomentDiscoveryJobPayload(momentSetId, attemptId), JsonOptions),
            DedupeKey = $"scene-moment-discovery:{plan.Id}:{plan.Version}:{momentSetId}",
            MaxAttempts = analyzer.RetryDelaysSeconds.Count + 1,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _momentSetRepository.CreateVersionAsync(momentSet, attempt, cancellationToken);
        try
        {
            if (!await _jobQueue.TryEnqueueAsync(job, cancellationToken))
                throw new InvalidOperationException("The durable Moment discovery job was not accepted.");
        }
        catch
        {
            attempt.ValidationCode = "scene_moment_discovery_enqueue_rejected";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(
                new { message = "The durable Moment discovery job was not accepted." }, JsonOptions);
            await _momentSetRepository.TryFailAttemptAsync(
                momentSet.Id,
                attempt,
                attempt.ValidationCode,
                "The durable Moment discovery job was not accepted.",
                now,
                CancellationToken.None);
            throw;
        }
        return momentSet;
    }
}