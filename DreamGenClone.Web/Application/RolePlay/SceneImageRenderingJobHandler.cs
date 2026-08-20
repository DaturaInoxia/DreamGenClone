using System.Diagnostics;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Renders an image from a prompt snapshot using the configured image model. Marks the image
/// record Generating → Complete/Failed. Enforces the provider content policy deterministically
/// (SFW clamp before sending to a filtered provider — never bypasses).
/// </summary>
public sealed class SceneImageRenderingJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneImageRepository _repository;
    private readonly ISceneImageStorageService _storage;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly IImageGenerationClient _imageClient;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly ILogger<SceneImageRenderingJobHandler> _logger;

    public SceneImageRenderingJobHandler(
        ISceneImageRepository repository,
        ISceneImageStorageService storage,
        IModelResolutionService modelResolutionService,
        IImageGenerationClient imageClient,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<SceneImageRenderingJobHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _modelResolutionService = modelResolutionService;
        _imageClient = imageClient;
        _debugEventSink = debugEventSink;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneImageRendering;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneImageRenderingJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image rendering job payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.SessionId))
            throw new InvalidOperationException("Scene image rendering payload is missing SessionId.");
        if (string.IsNullOrWhiteSpace(payload.InteractionId))
            throw new InvalidOperationException("Scene image rendering payload is missing InteractionId.");
        if (string.IsNullOrWhiteSpace(payload.ImageRecordId))
            throw new InvalidOperationException("Scene image rendering payload is missing ImageRecordId.");

        var image = await _repository.GetImageAsync(payload.ImageRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image record '{payload.ImageRecordId}' was not found.");

        if (image.Status == SceneImageStatus.Complete)
        {
            _logger.LogDebug("Skipping scene image rendering; already complete: ImageRecordId={ImageRecordId}", image.Id);
            return;
        }

        // Mark generating so the UI shows progress (monotonic forward transition).
        image.Status = SceneImageStatus.Generating;
        image.StartedUtc ??= DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.InsertImageAsync(image, cancellationToken);

        try
        {
            // Resolve the image model + provider content policy (fail-fast, no fallback).
            var resolved = await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken);

            var prompt = image.PromptSnapshot;

            // Hard content-policy guarantee: never send explicit content to a SFW-filtered provider.
            // Deterministic clamp, logged — never silently skipped, never auto-escalated.
            if (resolved.ContentPolicy == ImageContentPolicy.SfwFiltered
                && !prompt.Contains(SceneImagePromptPreprocessor.SfwClampSuffix, StringComparison.OrdinalIgnoreCase))
            {
                prompt = $"{prompt.TrimEnd()}, {SceneImagePromptPreprocessor.SfwClampSuffix}";
                _logger.LogWarning(
                    "Scene image prompt clamped to SFW (content_policy_clamped): SessionId={SessionId}, ImageRecordId={ImageRecordId}",
                    payload.SessionId,
                    image.Id);
            }

            var stopwatch = Stopwatch.StartNew();
            var bytes = await _imageClient.GenerateAsync(resolved, prompt, image.ImageSize, cancellationToken);
            stopwatch.Stop();

            if (bytes is null || bytes.Length == 0)
            {
                throw new ImageGenerationException(
                    $"Provider {resolved.ProviderName} returned no image data.",
                    resolved.ProviderName,
                    reasonCode: "empty_response");
            }

            var fileName = $"{image.Id}.png";
            await using (var stream = new MemoryStream(bytes))
            {
                image.FileRelativePath = await _storage.SaveAsync(payload.SessionId, fileName, stream, cancellationToken);
            }

            image.ModelIdentifier = resolved.ModelIdentifier;
            image.ProviderName = resolved.ProviderName;
            image.ContentPolicy = resolved.ContentPolicy;
            image.Status = SceneImageStatus.Complete;
            image.CompletedUtc = DateTime.UtcNow;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertImageAsync(image, cancellationToken);

            await WriteDebugEventAsync("SceneImageResponseReceived", payload.SessionId, payload.InteractionId, new
            {
                recordId = image.Id,
                stage = "renderer",
                status = "Complete",
                bytes = bytes.Length,
                durationMs = stopwatch.ElapsedMilliseconds
            }, cancellationToken);

            _logger.LogInformation(
                "Scene image rendering completed: SessionId={SessionId}, InteractionId={InteractionId}, ImageRecordId={ImageRecordId}, Model={ModelIdentifier}, Provider={ProviderName}, Bytes={Bytes}, DurationMs={DurationMs}",
                payload.SessionId,
                payload.InteractionId,
                image.Id,
                resolved.ModelIdentifier,
                resolved.ProviderName,
                bytes.Length,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            image.Status = SceneImageStatus.Failed;
            image.ErrorMessage = ex.Message;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.InsertImageAsync(image, cancellationToken);

            _logger.LogWarning(
                "Scene image rendering failed: SessionId={SessionId}, ImageRecordId={ImageRecordId}, Error={ErrorMessage}",
                payload.SessionId,
                image.Id,
                ex.Message);

            throw;
        }
    }

    private async Task WriteDebugEventAsync<T>(string kind, string sessionId, string interactionId, T metadata, CancellationToken cancellationToken)
    {
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = sessionId,
            InteractionId = interactionId,
            EventKind = kind,
            Severity = "Info",
            Summary = kind,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
        }, cancellationToken);
    }
}
