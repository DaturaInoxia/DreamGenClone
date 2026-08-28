using System.Diagnostics;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Produces a short "what the model sees" description for an edit session's source image, so the
/// user can word their edit intent to match (or correct) the vision model's perception.
/// </summary>
public sealed class SceneImageEditDescriptionJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISceneImageEditRepository _editRepository;
    private readonly ISceneImageRepository _imageRepository;
    private readonly ISceneImageStorageService _storage;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly IMultimodalCompletionClient _completionClient;
    private readonly IRolePlayDebugEventSink _debugEvents;

    public SceneImageEditDescriptionJobHandler(
        ISceneImageEditRepository editRepository,
        ISceneImageRepository imageRepository,
        ISceneImageStorageService storage,
        IMultimodalModelResolutionService modelResolver,
        IMultimodalCompletionClient completionClient,
        IRolePlayDebugEventSink debugEvents)
    {
        _editRepository = editRepository;
        _imageRepository = imageRepository;
        _storage = storage;
        _modelResolver = modelResolver;
        _completionClient = completionClient;
        _debugEvents = debugEvents;
    }

    public string JobType => BackgroundJobTypes.SceneImageEditDescription;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneImageEditDescriptionJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image edit description payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.EditSessionId))
            throw new InvalidOperationException("Scene image edit description payload requires an edit session id.");

        var editSession = await _editRepository.GetSessionAsync(payload.EditSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Edit session '{payload.EditSessionId}' was not found.");
        if (!string.IsNullOrWhiteSpace(editSession.DescriptionText))
            return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var source = await _imageRepository.GetImageAsync(editSession.SourceImageId, cancellationToken)
                ?? throw new InvalidOperationException($"Source scene image '{editSession.SourceImageId}' was not found.");
            if (source.Status != SceneImageStatus.Complete || string.IsNullOrWhiteSpace(source.FileRelativePath))
                throw new InvalidOperationException("Description requires a stored, completed source image.");
            var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);

            await using var stream = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken);
            var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
            SceneImageMultimodalInput.Validate(input, resolved);

            await _completionClient.CheckHealthAsync(resolved, cancellationToken);
            var completion = await _completionClient.GenerateAsync(
                resolved,
                new MultimodalCompletionRequest(
                    SceneImageDescriptionPromptBuilder.BuildSystemMessage(),
                    SceneImageDescriptionPromptBuilder.BuildUserMessage(),
                    new MultimodalImageInput(input.MediaType, input.Bytes, input.Width, input.Height, input.Sha256),
                    SceneImageDescriptionPromptBuilder.ResponseSchemaName,
                    SceneImageDescriptionPromptBuilder.CreateResponseSchema()),
                cancellationToken);

            var description = ParseDescription(completion.Content);
            await _editRepository.SetDescriptionAsync(editSession.Id, description, DateTime.UtcNow, cancellationToken);
            await WriteEventAsync("SceneImageEditDescriptionCompleted", editSession, resolved, stopwatch.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteEventAsync("SceneImageEditDescriptionFailed", editSession, null, stopwatch.ElapsedMilliseconds, cancellationToken, ex.Message);
            throw;
        }
    }

    private static string ParseDescription(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            throw new InvalidOperationException("Scene image edit description returned empty output.");
        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("description", out var description)
                || description.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(description.GetString()))
                throw new InvalidOperationException("Scene image edit description response must contain a non-empty 'description' string.");
            return description.GetString()!.Trim();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Scene image edit description returned malformed JSON.", ex);
        }
    }

    private Task WriteEventAsync(
        string kind,
        SceneImageEditSession editSession,
        ResolvedMultimodalModel? resolved,
        long durationMs,
        CancellationToken cancellationToken,
        string? error = null) => _debugEvents.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = editSession.SessionId,
            InteractionId = editSession.InteractionId,
            CorrelationId = editSession.Id,
            EventKind = kind,
            Severity = error is null ? "Info" : "Warning",
            ModelIdentifier = resolved?.ModelIdentifier,
            ProviderName = resolved?.ProviderName,
            DurationMs = checked((int)durationMs),
            Summary = kind,
            MetadataJson = JsonSerializer.Serialize(new { editSessionId = editSession.Id, sourceImageId = editSession.SourceImageId, error }, JsonOptions)
        }, cancellationToken);
}
