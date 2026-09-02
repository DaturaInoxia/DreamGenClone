using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneBeatPipelineService : ISceneBeatPipelineService
{
    public const string CatalogueJobType = "SceneBeatCatalogue";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneBeatSessionReader _sessionReader;
    private readonly IRolePlayTurnReader _turnReader;
    private readonly ISceneBeatScenarioReader _scenarioReader;
    private readonly ISceneBeatAnalyzerResolver _analyzerResolver;
    private readonly SceneBeatCatalogueSnapshotBuilder _snapshotBuilder;
    private readonly SceneBeatCatalogueContract _contract;
    private readonly ISceneBeatCatalogueRepository _catalogueRepository;
    private readonly IDurableBackgroundJobQueue _jobQueue;
    private readonly TimeProvider _timeProvider;

    public SceneBeatPipelineService(
        ISceneBeatSessionReader sessionReader,
        IRolePlayTurnReader turnReader,
        ISceneBeatScenarioReader scenarioReader,
        ISceneBeatAnalyzerResolver analyzerResolver,
        SceneBeatCatalogueSnapshotBuilder snapshotBuilder,
        SceneBeatCatalogueContract contract,
        ISceneBeatCatalogueRepository catalogueRepository,
        IDurableBackgroundJobQueue jobQueue,
        TimeProvider timeProvider)
    {
        _sessionReader = sessionReader;
        _turnReader = turnReader;
        _scenarioReader = scenarioReader;
        _analyzerResolver = analyzerResolver;
        _snapshotBuilder = snapshotBuilder;
        _contract = contract;
        _catalogueRepository = catalogueRepository;
        _jobQueue = jobQueue;
        _timeProvider = timeProvider;
    }

    public Task<SceneBeatCatalogue> EnqueueCatalogueAsync(
        GenerateSceneBeatCatalogueRequest request,
        CancellationToken cancellationToken = default)
        => EnqueueAsync(request, replaceCurrent: false, cancellationToken);

    public Task<SceneBeatCatalogue> ReplaceCatalogueAsync(
        GenerateSceneBeatCatalogueRequest request,
        CancellationToken cancellationToken = default)
        => EnqueueAsync(request, replaceCurrent: true, cancellationToken);

    public Task<SceneBeatCatalogue?> GetCurrentCatalogueAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default)
        => _catalogueRepository.GetCurrentByTurnAsync(sessionId, turnId, cancellationToken);

    public async Task CancelCatalogueAsync(string catalogueId, CancellationToken cancellationToken = default)
    {
        var catalogue = await _catalogueRepository.GetAsync(catalogueId, cancellationToken)
            ?? throw new InvalidOperationException($"Beat Catalogue '{catalogueId}' was not found.");
        if (string.IsNullOrWhiteSpace(catalogue.CurrentAttemptId))
            throw new InvalidOperationException($"Beat Catalogue '{catalogueId}' has no current attempt.");
        var attempt = await _catalogueRepository.GetAttemptAsync(catalogue.CurrentAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Beat Catalogue attempt '{catalogue.CurrentAttemptId}' was not found.");
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _jobQueue.TryCancelAsync(attempt.JobId, now, cancellationToken);
        if (!await _catalogueRepository.TryCancelCurrentAsync(catalogue.Id, attempt.Id, now, cancellationToken))
            throw new InvalidOperationException($"Beat Catalogue '{catalogueId}' is not cancellable.");
    }

    private async Task<SceneBeatCatalogue> EnqueueAsync(
        GenerateSceneBeatCatalogueRequest request,
        bool replaceCurrent,
        CancellationToken cancellationToken)
    {
        RequireIds(request);
        var current = await _catalogueRepository.GetCurrentByTurnAsync(
            request.SessionId, request.TurnId, cancellationToken);
        if (current is { Status: SceneBeatCatalogueStatus.Pending or SceneBeatCatalogueStatus.Processing })
            throw new InvalidOperationException("Generate Again is unavailable while the current Beat Catalogue is active.");
        if (!replaceCurrent && current is not null)
            throw new InvalidOperationException("A Beat Catalogue already exists. Use the explicit replace operation to Generate Again.");

        var session = await _sessionReader.GetSessionAsync(request.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{request.SessionId}' was not found.");
        var turn = await _turnReader.GetTurnAsync(request.SessionId, request.TurnId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play turn '{request.TurnId}' was not found in session '{request.SessionId}'.");
        if (turn.Status != RolePlayTurnStatus.Completed || turn.CompletedUtc is null)
            throw new InvalidOperationException($"Role-play turn '{request.TurnId}' is not complete.");

        var membership = turn.OutputInteractionIds
            .Prepend(turn.InputInteractionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var interactions = session.Interactions.Where(item => membership.Contains(item.Id)).ToList();
        var narrative = interactions.SingleOrDefault(item =>
            string.Equals(item.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase));
        var fullTurn = new FullTurnContext
        {
            Turn = turn,
            Interactions = interactions,
            SelectedInteraction = narrative ?? interactions.FirstOrDefault()
                ?? throw new InvalidOperationException($"Role-play turn '{request.TurnId}' has no persisted interactions."),
            NarrativeInteraction = narrative
        };

        IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character>? characters = null;
        IReadOnlyList<string>? locations = null;
        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            characters = await _scenarioReader.GetCharactersAsync(session.ScenarioId)
                ?? throw new InvalidOperationException($"Scenario '{session.ScenarioId}' was not found.");
            locations = await _scenarioReader.GetLocationsAsync(session.ScenarioId);
        }

        var analyzer = await _analyzerResolver.ResolveAsync(cancellationToken);
        var snapshot = _snapshotBuilder.Build(fullTurn, session, characters, locations);
        var messages = _contract.BuildMessages(snapshot, analyzer.MaximumCatalogueEntries);
        var executionSnapshot = SceneBeatAnalyzerExecutionSnapshot.FromResolved(analyzer);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var catalogueId = Guid.NewGuid().ToString();
        var attemptId = Guid.NewGuid().ToString();
        var jobId = Guid.NewGuid().ToString();
        var executionSettingsJson = JsonSerializer.Serialize(executionSnapshot, JsonOptions);
        var catalogue = new SceneBeatCatalogue
        {
            Id = catalogueId,
            SessionId = request.SessionId.Trim(),
            TurnId = request.TurnId.Trim(),
            Version = 0,
            CurrentAttemptId = attemptId,
            SchemaVersion = snapshot.SchemaVersion,
            PromptContractVersion = messages.ContractVersion,
            InputSnapshotJson = _snapshotBuilder.Serialize(snapshot),
            ExecutionSettingsJson = executionSettingsJson,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId,
            OwnerRecordId = catalogueId,
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
            JobType = CatalogueJobType,
            Lane = DurableJobLane.TextAnalysis,
            PayloadJson = JsonSerializer.Serialize(new SceneBeatCatalogueJobPayload(catalogueId, attemptId), JsonOptions),
            DedupeKey = $"scene-beat-catalogue:{catalogueId}:{attemptId}",
            MaxAttempts = analyzer.RetryDelaysSeconds.Count + 1,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _catalogueRepository.CreateVersionAsync(catalogue, attempt, cancellationToken);
        try
        {
            if (!await _jobQueue.TryEnqueueAsync(job, cancellationToken))
                throw new InvalidOperationException("The durable Beat Catalogue job was not accepted.");
        }
        catch
        {
            attempt.ValidationCode = "scene_beat_enqueue_rejected";
            attempt.ValidationDetailsJson = JsonSerializer.Serialize(
                new { message = "The durable Beat Catalogue job was not accepted." }, JsonOptions);
            await _catalogueRepository.TryFailAttemptAsync(
                catalogueId,
                attempt,
                attempt.ValidationCode,
                "The durable Beat Catalogue job was not accepted.",
                now,
                CancellationToken.None);
            throw;
        }
        return catalogue;
    }

    private static void RequireIds(GenerateSceneBeatCatalogueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.TurnId))
            throw new InvalidOperationException("Session id and turn id are required to generate a Beat Catalogue.");
    }
}