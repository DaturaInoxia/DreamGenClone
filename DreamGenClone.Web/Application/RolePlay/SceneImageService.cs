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
    private readonly ISessionService _sessionService;
    private readonly ISceneImageRepository _repository;
    private readonly ISceneImageStorageService _storage;
    private readonly IBackgroundJobQueue _backgroundJobQueue;
    private readonly ILogger<SceneImageService> _logger;

    public SceneImageService(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImageStorageService storage,
        IBackgroundJobQueue backgroundJobQueue,
        ILogger<SceneImageService> logger)
    {
        _sessionService = sessionService;
        _repository = repository;
        _storage = storage;
        _backgroundJobQueue = backgroundJobQueue;
        _logger = logger;
    }

    public async Task<SceneImagePromptRecord> EnqueuePromptAsync(ScenePromptRequest request, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        var interaction = FindInteraction(session, request.InteractionId);

        var record = new SceneImagePromptRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
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

        var record = new SceneImageRecord
        {
            SessionId = session.Id,
            InteractionId = request.InteractionId,
            PromptRecordId = promptRecord.Id,
            PromptSnapshot = request.Prompt,
            Status = SceneImageStatus.Pending,
            ImageSize = request.ImageSize,
            SettingsJson = settingsJson,
            RegenerateOfId = request.RegenerateOfId
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

    public Task<SceneImagePromptRecord?> GetPromptAsync(string sessionId, string promptId, CancellationToken cancellationToken = default)
        => _repository.GetPromptAsync(promptId, cancellationToken);

    public Task<SceneImagePromptRecord?> GetLatestPromptAsync(string sessionId, string interactionId, CancellationToken cancellationToken = default)
        => _repository.GetLatestPromptAsync(sessionId, interactionId, cancellationToken);

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
