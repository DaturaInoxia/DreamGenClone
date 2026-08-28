using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Public orchestration surface for the scene-image feature. Enqueues the two-stage pipeline
/// (pre-processor prompt generation + image rendering) onto the background job queue and provides
/// query/delete operations for the studio, gallery, and workspace.
/// </summary>
public sealed class SceneImageService : ISceneImageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionService _sessionService;
    private readonly ISceneImageRepository _repository;
    private readonly ISceneImageEditRepository _editRepository;
    private readonly ISceneImageStorageService _storage;
    private readonly IBackgroundJobQueue _backgroundJobQueue;
    private readonly SceneImageTurnResolver? _turnResolver;
    private readonly ILogger<SceneImageService> _logger;

    public SceneImageService(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IBackgroundJobQueue backgroundJobQueue,
        ILogger<SceneImageService> logger)
        : this(sessionService, repository, editRepository, storage, backgroundJobQueue, null, logger)
    {
    }

    public SceneImageService(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IBackgroundJobQueue backgroundJobQueue,
        SceneImageTurnResolver? turnResolver,
        ILogger<SceneImageService> logger)
    {
        _sessionService = sessionService;
        _repository = repository;
        _editRepository = editRepository;
        _storage = storage;
        _backgroundJobQueue = backgroundJobQueue;
        _turnResolver = turnResolver;
        _logger = logger;
    }

    public async Task<SceneImageBeatAnalysisRecord> EnqueueBeatAnalysisAsync(
        SceneImageBeatGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        var interaction = FindInteraction(session, request.InteractionId);
        var turnResolver = _turnResolver
            ?? throw new InvalidOperationException("Scene image beat analysis requires the turn resolver service.");
        var fullTurn = await turnResolver.ResolveAsync(session, interaction.Id, cancellationToken);
        if (fullTurn.Turn is null)
            throw new InvalidOperationException("Beat generation requires a persisted RolePlayV2Turn; this interaction has no authoritative turn.");

        var analysis = new SceneImageBeatAnalysisRecord
        {
            SessionId = session.Id,
            TurnId = fullTurn.Turn.TurnId,
            AnchorInteractionId = interaction.Id,
            Status = SceneImageBeatAnalysisStatus.Pending,
            InputSnapshotJson = JsonSerializer.Serialize(new
            {
                turnId = fullTurn.Turn.TurnId,
                interactionIds = fullTurn.Interactions.Select(x => x.Id).ToList()
            })
        };
        await _repository.UpsertBeatAnalysisAsync(analysis, cancellationToken);
        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneImageBeatGeneration,
            JsonSerializer.Serialize(new SceneImageBeatGenerationJobPayload
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                AnalysisRecordId = analysis.Id
            }),
            dedupeKey: $"{BackgroundJobTypes.SceneImageBeatGeneration}:{analysis.Id}");
        return analysis;
    }

    public Task<SceneImageBeatAnalysisRecord?> GetBeatAnalysisByTurnAsync(
        string sessionId, string turnId, CancellationToken cancellationToken = default)
        => _repository.GetBeatAnalysisByTurnAsync(sessionId, turnId, cancellationToken);

    public async Task<SceneImagePromptRecord> EnqueuePromptAsync(ScenePromptRequest request, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        var interaction = FindInteraction(session, request.InteractionId);
        if (string.IsNullOrWhiteSpace(request.BeatAnalysisId))
            throw new InvalidOperationException("A completed beat analysis is required to generate an image prompt.");
        if (string.IsNullOrWhiteSpace(request.BeatSnapshotJson))
            throw new InvalidOperationException("A selected beat is required to generate an image prompt.");
        if (string.IsNullOrWhiteSpace(request.Pov))
            throw new InvalidOperationException("A POV is required to generate an image prompt.");

        var turnResolver = _turnResolver
            ?? throw new InvalidOperationException("Scene image prompt generation requires the turn resolver service.");
        var fullTurn = await turnResolver.ResolveAsync(session, interaction.Id, cancellationToken);
        if (fullTurn.Turn is null)
            throw new InvalidOperationException("Scene image prompt generation requires a persisted RolePlayV2Turn.");
        var analysis = await _repository.GetBeatAnalysisByTurnAsync(session.Id, fullTurn.Turn.TurnId, cancellationToken);
        if (analysis is null || analysis.Status != SceneImageBeatAnalysisStatus.Complete)
            throw new InvalidOperationException("Generate a completed beat analysis before generating an image prompt.");
        if (!string.Equals(analysis.Id, request.BeatAnalysisId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected beat belongs to a replaced analysis. Select a beat from the current analysis.");

        var requestedBeat = JsonSerializer.Deserialize<SceneImageBeat>(request.BeatSnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("The selected beat snapshot is invalid.");
        var currentBeats = JsonSerializer.Deserialize<IReadOnlyList<SceneImageBeat>>(analysis.BeatsJson, JsonOptions)
            ?? throw new InvalidOperationException("The completed beat analysis has an invalid beat list.");
        var selectedBeat = currentBeats.FirstOrDefault(x => string.Equals(x.BeatId, requestedBeat.BeatId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected beat is not part of the current completed analysis.");
        if (selectedBeat.SchemaVersion != SceneImageBeatAnalysisService.CurrentSchemaVersion)
            throw new InvalidOperationException("The selected beat analysis uses an older schema. Generate beats again.");
        if (!string.Equals(request.Pov, SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase)
            && !selectedBeat.Characters.Any(x => string.Equals(x.Name, request.Pov, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected character POV is not associated with the selected beat.");

        var record = new SceneImagePromptRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            BeatAnalysisId = request.BeatAnalysisId.Trim(),
            BeatSnapshotJson = JsonSerializer.Serialize(selectedBeat, JsonOptions),
            Pov = request.Pov.Trim(),
            SettingsJson = JsonSerializer.Serialize(request.Settings),
            InputExcerpt = request.ExcerptOverride ?? string.Empty,
            RefineInstruction = string.IsNullOrWhiteSpace(request.RefineInstruction) ? null : request.RefineInstruction.Trim(),
            Status = SceneImagePromptStatus.Pending
        };

        await _repository.UpsertPromptAsync(record, cancellationToken);

        var payloadJson = JsonSerializer.Serialize(new SceneImagePromptGenerationJobPayload
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
                PromptRecordId = record.Id
        });

        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneImagePromptGeneration,
            payloadJson,
            dedupeKey: $"{BackgroundJobTypes.SceneImagePromptGeneration}:{record.Id}");

        _logger.LogInformation(
            "Enqueued scene image prompt generation: SessionId={SessionId}, InteractionId={InteractionId}, PromptRecordId={PromptRecordId}",
            session.Id,
            interaction.Id,
            record.Id);

        return record;
    }

    public async Task<SceneImageRecord> EnqueueRenderAsync(SceneRenderRequest request, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        FindInteraction(session, request.InteractionId);

        if (string.IsNullOrWhiteSpace(request.PromptRecordId))
        {
            throw new InvalidOperationException("A prompt record id is required to render a scene image.");
        }

        var promptRecord = await _repository.GetPromptAsync(request.PromptRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image prompt record '{request.PromptRecordId}' was not found.");

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new InvalidOperationException("A non-empty prompt is required to render a scene image.");
        }

        var settingsJson = string.IsNullOrWhiteSpace(request.SettingsJson) ? "{}" : request.SettingsJson;

        if (request.RenderMode == SceneImageRenderMode.IdentityControlled
            && string.IsNullOrWhiteSpace(request.IdentityPackId)
            && (request.IdentityPacks is null || request.IdentityPacks.Count == 0))
        {
            throw new InvalidOperationException("At least one approved identity pack is required for identity-controlled rendering.");
        }

        var firstPackId = request.IdentityPacks?.FirstOrDefault()?.PackId;
        var record = new SceneImageRecord
        {
            SessionId = session.Id,
            InteractionId = request.InteractionId,
            PromptRecordId = promptRecord.Id,
            PromptSnapshot = request.Prompt,
            Status = SceneImageStatus.Pending,
            ImageSize = request.ImageSize,
            SettingsJson = settingsJson,
            RegenerateOfId = request.RegenerateOfId,
            BeatId = request.BeatId,
            Pov = request.Pov,
            RenderMode = request.RenderMode,
            IdentityPackId = request.RenderMode == SceneImageRenderMode.IdentityControlled
                ? (firstPackId ?? request.IdentityPackId)
                : null,
            IdentityPacksJson = request.RenderMode == SceneImageRenderMode.IdentityControlled && request.IdentityPacks is { Count: > 0 }
                ? JsonSerializer.Serialize(request.IdentityPacks, JsonOptions)
                : null
        };

        // Extract the style/size labels from the settings snapshot so the image card can display
        // them without a separate join. Best-effort metadata only — never a fallback gate.
        try
        {
            var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(settingsJson);
            if (settings is not null)
            {
                if (!string.IsNullOrWhiteSpace(settings.Style))
                {
                    record.Style = settings.Style;
                }
                if (string.IsNullOrWhiteSpace(record.ImageSize) && !string.IsNullOrWhiteSpace(settings.ImageSize))
                {
                    record.ImageSize = settings.ImageSize;
                }
            }
        }
        catch (JsonException)
        {
            // The settings snapshot is informational; a malformed snapshot does not block rendering.
        }

        await _repository.InsertImageAsync(record, cancellationToken);

        var payloadJson = JsonSerializer.Serialize(new SceneImageRenderingJobPayload
        {
            SessionId = session.Id,
            InteractionId = request.InteractionId,
            ImageRecordId = record.Id
        });

        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneImageRendering,
            payloadJson,
            dedupeKey: $"{BackgroundJobTypes.SceneImageRendering}:{record.Id}");

        _logger.LogInformation(
            "Enqueued scene image rendering: SessionId={SessionId}, InteractionId={InteractionId}, ImageRecordId={ImageRecordId}",
            session.Id,
            request.InteractionId,
            record.Id);

        return record;
    }

    public async Task<SceneImageRecord> EnqueueEditAsync(SceneImageEditRequest request, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        var interaction = FindInteraction(session, request.InteractionId);
        if (string.IsNullOrWhiteSpace(request.SourceImageId))
            throw new InvalidOperationException("A source image id is required to edit a scene image.");
        if (string.IsNullOrWhiteSpace(request.EditSessionId)
            || string.IsNullOrWhiteSpace(request.CompilationAttemptId)
            || string.IsNullOrWhiteSpace(request.PromptRevisionId)
            || string.IsNullOrWhiteSpace(request.SourceImageSha256)
            || string.IsNullOrWhiteSpace(request.PromptSha256))
            throw new InvalidOperationException("An exact compiled edit session, attempt, prompt revision, source checksum, and prompt checksum are required.");

        var source = await _repository.GetImageAsync(request.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{request.SourceImageId}' was not found.");
        if (source.Status != SceneImageStatus.Complete)
            throw new InvalidOperationException("Only completed scene images can be edited.");
        if (!string.Equals(source.SessionId, session.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.InteractionId, interaction.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The source scene image must belong to the selected session and interaction.");
        }
        if (string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("The completed source scene image has no stored image path.");

        var editSession = await _editRepository.GetSessionAsync(request.EditSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Edit session '{request.EditSessionId}' was not found.");
        if (!string.Equals(editSession.SourceImageId, source.Id, StringComparison.Ordinal)
            || !string.Equals(editSession.SessionId, session.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(editSession.InteractionId, interaction.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The edit session does not belong to the selected source image, session, and interaction.");
        var revision = await _editRepository.GetExecutableRevisionAsync(
            editSession.Id,
            request.CompilationAttemptId,
            request.PromptRevisionId,
            request.SourceImageSha256,
            request.PromptSha256,
            cancellationToken);
        var attempt = await _editRepository.GetAttemptAsync(request.CompilationAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Compilation attempt '{request.CompilationAttemptId}' was not found.");
        var provenanceJson = JsonSerializer.Serialize(new
        {
            editSessionId = editSession.Id,
            compilationAttemptId = attempt.Id,
            attemptOrdinal = attempt.Ordinal,
            promptRevisionId = revision.Id,
            revisionOrdinal = revision.Ordinal,
            sourceImageSha256 = editSession.SourceImageSha256,
            promptSha256 = revision.PromptSha256,
            attempt.CompilerSchemaVersion,
            attempt.SystemPromptVersion,
            resolvedModelSnapshot = JsonSerializer.Deserialize<JsonElement>(attempt.ResolvedModelSnapshotJson)
        }, JsonOptions);

        var record = new SceneImageRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            PromptRecordId = source.PromptRecordId,
            PromptSnapshot = revision.Prompt,
            Status = SceneImageStatus.Pending,
            Operation = SceneImageOperation.Edit,
            SourceImageId = source.Id,
            EditSessionId = editSession.Id,
            EditCompilationAttemptId = attempt.Id,
            EditPromptRevisionId = revision.Id,
            EditIntentSnapshot = attempt.RawIntent,
            EditCompilerProvenanceJson = provenanceJson,
            ImageSize = source.ImageSize,
            Style = source.Style,
            SettingsJson = source.SettingsJson,
            BeatId = source.BeatId,
            Pov = source.Pov
        };
        await _repository.InsertImageAsync(record, cancellationToken);
        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneImageEditing,
            JsonSerializer.Serialize(new SceneImageEditingJobPayload
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                ImageRecordId = record.Id
            }),
            dedupeKey: $"{BackgroundJobTypes.SceneImageEditing}:{record.Id}");

        _logger.LogInformation("Enqueued scene image edit: SessionId={SessionId}, InteractionId={InteractionId}, ImageRecordId={ImageRecordId}, SourceImageId={SourceImageId}", session.Id, interaction.Id, record.Id, source.Id);
        return record;
    }

    public Task<SceneImagePromptRecord?> GetPromptAsync(string sessionId, string promptId, CancellationToken cancellationToken = default)
        => _repository.GetPromptAsync(promptId, cancellationToken);

    public Task<SceneImagePromptRecord?> GetLatestPromptAsync(string sessionId, string interactionId, CancellationToken cancellationToken = default)
        => _repository.GetLatestPromptAsync(sessionId, interactionId, cancellationToken);

    public Task<SceneImagePromptRecord?> GetLatestCompletedPromptAsync(
        string sessionId,
        string interactionId,
        string beatAnalysisId,
        string beatId,
        string pov,
        CancellationToken cancellationToken = default)
        => _repository.GetLatestCompletedPromptAsync(
            sessionId, interactionId, beatAnalysisId, beatId, pov, cancellationToken);

    public async Task UpdatePromptOutputAsync(string sessionId, string promptId, string outputPrompt, CancellationToken cancellationToken = default)
    {
        await _repository.UpdatePromptOutputAsync(promptId, outputPrompt, cancellationToken);

        _logger.LogInformation(
            "Updated scene image prompt output: SessionId={SessionId}, PromptRecordId={PromptRecordId}",
            sessionId,
            promptId);
    }

    public Task<IReadOnlyList<SceneImageRecord>> ListImagesByInteractionAsync(string sessionId, string interactionId, CancellationToken cancellationToken = default)
        => _repository.ListImagesByInteractionAsync(sessionId, interactionId, cancellationToken);

    public Task<IReadOnlyList<SceneImageRecord>> ListImagesBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => _repository.ListImagesBySessionAsync(sessionId, cancellationToken);

    public Task<Dictionary<string, int>> CountImagesByInteractionAsync(string sessionId, CancellationToken cancellationToken = default)
        => _repository.CountImagesByInteractionAsync(sessionId, cancellationToken);

    public async Task DeleteImageAsync(string sessionId, string imageId, CancellationToken cancellationToken = default)
    {
        var image = await _repository.GetImageAsync(imageId, cancellationToken);
        if (image is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(image.FileRelativePath))
        {
            await _storage.DeleteAsync(image.FileRelativePath, cancellationToken);
        }

        await _repository.DeleteImageAsync(imageId, cancellationToken);

        _logger.LogInformation(
            "Deleted scene image: SessionId={SessionId}, ImageId={ImageId}",
            sessionId,
            imageId);
    }

    private async Task<RolePlaySession> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required for scene image generation.");
        }

        var session = await _sessionService.LoadRolePlaySessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' was not found for scene image generation.");

        return session;
    }

    private static RolePlayInteraction FindInteraction(RolePlaySession session, string interactionId)
    {
        if (string.IsNullOrWhiteSpace(interactionId))
        {
            throw new InvalidOperationException("Interaction id is required for scene image generation.");
        }

        return session.Interactions.FirstOrDefault(x => string.Equals(x.Id, interactionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Interaction '{interactionId}' was not found in role-play session '{session.Id}'.");
    }
}
