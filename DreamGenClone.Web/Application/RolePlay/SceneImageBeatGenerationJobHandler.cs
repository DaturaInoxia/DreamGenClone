using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneImageBeatGenerationJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISessionService _sessionService;
    private readonly ISceneImageRepository _repository;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ICompletionClient _completionClient;
    private readonly IScenarioService _scenarioService;
    private readonly SceneImageTurnResolver _turnResolver;
    private readonly SceneImageBeatAnalysisService _analysisService;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly ILogger<SceneImageBeatGenerationJobHandler> _logger;

    public SceneImageBeatGenerationJobHandler(
        ISessionService sessionService,
        ISceneImageRepository repository,
        IModelResolutionService modelResolutionService,
        ICompletionClient completionClient,
        IScenarioService scenarioService,
        SceneImageTurnResolver turnResolver,
        SceneImageBeatAnalysisService analysisService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<SceneImageBeatGenerationJobHandler> logger)
    {
        _sessionService = sessionService;
        _repository = repository;
        _modelResolutionService = modelResolutionService;
        _completionClient = completionClient;
        _scenarioService = scenarioService;
        _turnResolver = turnResolver;
        _analysisService = analysisService;
        _debugEventSink = debugEventSink;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneImageBeatGeneration;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneImageBeatGenerationJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image beat generation job payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.SessionId) || string.IsNullOrWhiteSpace(payload.InteractionId) || string.IsNullOrWhiteSpace(payload.AnalysisRecordId))
            throw new InvalidOperationException("Scene image beat generation payload is missing required values.");

        var session = await _sessionService.LoadRolePlaySessionAsync(payload.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{payload.SessionId}' was not found for scene image beat generation.");
        var fullTurn = await _turnResolver.ResolveAsync(session, payload.InteractionId, cancellationToken);
        if (fullTurn.Turn is null)
            throw new InvalidOperationException("Beat generation requires a persisted RolePlayV2Turn; this legacy interaction has no authoritative turn.");

        var analysis = await _repository.GetBeatAnalysisByTurnAsync(session.Id, fullTurn.Turn.TurnId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image beat analysis '{payload.AnalysisRecordId}' was not found.");
        if (!string.Equals(analysis.Id, payload.AnalysisRecordId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The queued beat analysis is no longer current for this turn.");
        if (analysis.Status == SceneImageBeatAnalysisStatus.Complete)
            return;

        try
        {
            var scenario = string.IsNullOrWhiteSpace(session.ScenarioId)
                ? null
                : await _scenarioService.GetScenarioAsync(session.ScenarioId);
            var resolved = await _modelResolutionService.ResolveImagePromptModelAsync(session.SessionModelId, cancellationToken);
            var (systemPrompt, userPrompt) = _analysisService.BuildMessages(fullTurn, session, scenario?.Characters);
            await WriteDebugEventAsync("SceneImageBeatAnalysisSent", session.Id, payload.InteractionId, new
            {
                analysisRecordId = analysis.Id,
                turnId = fullTurn.Turn.TurnId,
                modelIdentifier = resolved.ModelIdentifier,
                systemPrompt,
                userPrompt
            }, cancellationToken);

            var (rawResponse, reasoning) = await _completionClient.GenerateWithReasoningAsync(
                systemPrompt,
                userPrompt,
                resolved,
                cancellationToken);
            analysis.RawModelResponse = rawResponse;
            analysis.ReasoningContent = reasoning;
            analysis.ModelIdentifier = resolved.ModelIdentifier;
            var beats = _analysisService.ParseOutput(rawResponse, fullTurn.Interactions);
            analysis.Status = SceneImageBeatAnalysisStatus.Complete;
            analysis.BeatsJson = JsonSerializer.Serialize(beats, JsonOptions);
            analysis.ErrorMessage = null;
            analysis.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertBeatAnalysisAsync(analysis, cancellationToken);

            await WriteDebugEventAsync("SceneImageBeatAnalysisCompleted", session.Id, payload.InteractionId, new
            {
                analysisRecordId = analysis.Id,
                turnId = analysis.TurnId,
                rawResponseLength = rawResponse.Length,
                reasoningLength = reasoning?.Length ?? 0,
                beats
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            analysis.Status = SceneImageBeatAnalysisStatus.Failed;
            analysis.ErrorMessage = ex.Message;
            analysis.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertBeatAnalysisAsync(analysis, cancellationToken);
            _logger.LogError(
                ex,
                "Scene image beat analysis failed: SessionId={SessionId}, TurnId={TurnId} --- RAW OUTPUT ---{NewLine}{RawOutput}",
                session.Id,
                analysis.TurnId,
                Environment.NewLine,
                analysis.RawModelResponse ?? string.Empty);
            throw;
        }
    }

    private Task WriteDebugEventAsync<T>(string kind, string sessionId, string interactionId, T metadata, CancellationToken cancellationToken)
        => _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = sessionId,
            InteractionId = interactionId,
            EventKind = kind,
            Severity = "Info",
            Summary = kind,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
        }, cancellationToken);
}