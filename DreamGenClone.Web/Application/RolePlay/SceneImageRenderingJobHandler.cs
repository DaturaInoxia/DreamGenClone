using System.Diagnostics;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;
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
    private readonly IPonySceneImagePromptBuilder _preprocessor;
    private readonly ISdxlSceneImagePromptBuilder _sdxlPreprocessor;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly ILogger<SceneImageRenderingJobHandler> _logger;

    public SceneImageRenderingJobHandler(
        ISceneImageRepository repository,
        ISceneImageStorageService storage,
        IModelResolutionService modelResolutionService,
        IImageGenerationClient imageClient,
        IPonySceneImagePromptBuilder preprocessor,
        ISdxlSceneImagePromptBuilder sdxlPreprocessor,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<SceneImageRenderingJobHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _modelResolutionService = modelResolutionService;
        _imageClient = imageClient;
        _preprocessor = preprocessor;
        _sdxlPreprocessor = sdxlPreprocessor;
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
            var modelFamily = SceneImageModelFamilyResolver.Classify(resolved.ModelIdentifier);
            if (modelFamily == SceneImageModelFamily.Unknown)
            {
                throw new InvalidOperationException(
                    $"Unsupported scene-image model family for checkpoint '{resolved.ModelIdentifier}'. " +
                    "Register a Pony or SDXL/Juggernaut model as the RolePlaySceneImage default in Model Manager.");
            }

            var prompt = image.PromptSnapshot;

            // Hard content-policy guarantee: never send explicit content to a SFW-filtered provider.
            // Deterministic clamp, logged — never silently skipped, never auto-escalated. The clamp
            // suffix is model-family aware (Pony vs SDXL prose), never a silent default.
            var sfwClampSuffix = modelFamily == SceneImageModelFamily.Sdxl
                ? _sdxlPreprocessor.SfwClampSuffix
                : PonySceneImagePromptBuilder.SfwClampSuffix;
            if (resolved.ContentPolicy == ImageContentPolicy.SfwFiltered
                && !prompt.Contains(sfwClampSuffix, StringComparison.OrdinalIgnoreCase))
            {
                prompt = $"{prompt.TrimEnd()}, {sfwClampSuffix}";
                _logger.LogWarning(
                    "Scene image prompt clamped to SFW (content_policy_clamped): SessionId={SessionId}, ImageRecordId={ImageRecordId}",
                    payload.SessionId,
                    image.Id);
            }

            var stopwatch = Stopwatch.StartNew();
            var negative = await ResolveNegativePromptAsync(image, modelFamily, cancellationToken);
            var injectedPrompt = InjectPlaceholders(prompt, image.SettingsJson);
            var seed = ResolveSeed(image.SettingsJson);

            // Permanent observability: record the EXACT payload the app submits to ComfyUI so the
            // submitted positive/negative/seed/checkpoint can be audited against the script or
            // provider results, and verified unchanged from the user's pasted prompt.
            await WriteDebugEventAsync("SceneImageRequestSubmitted", payload.SessionId, payload.InteractionId, new
            {
                recordId = image.Id,
                checkpoint = resolved.ModelIdentifier,
                provider = resolved.ProviderName,
                protocol = resolved.ImageProtocol,
                size = image.ImageSize,
                seed = seed.HasValue ? seed.Value.ToString() : "random",
                positive = injectedPrompt,
                negative = negative ?? "(baseline client negative)"
            }, cancellationToken);

            var bytes = await _imageClient.GenerateAsync(resolved, injectedPrompt, image.ImageSize, negative, seed, cancellationToken);
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

    /// <summary>
    /// Substitutes the option placeholders ({{style}}, {{size}}, {{angle}}) in a generated prompt
    /// with the current studio settings snapshot. Unchanged placeholders are replaced with the
    /// provider-agnostic values or a neutral fallback so no "<c>{{...}}</c>" literal reaches the model.
    /// </summary>
    private static string InjectPlaceholders(string prompt, string settingsJson)
    {
        var style = "realistic";
        var size = "1024x1024";
        var angle = "frame the complete visible event";

        if (!string.IsNullOrWhiteSpace(settingsJson))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(settingsJson, JsonOptions);
                if (settings is not null)
                {
                    if (!string.IsNullOrWhiteSpace(settings.Style)) style = settings.Style.Trim();
                    if (!string.IsNullOrWhiteSpace(settings.ImageSize)) size = settings.ImageSize.Trim();
                    if (!string.IsNullOrWhiteSpace(settings.OmniscientAngle)) angle = settings.OmniscientAngle.Trim();
                    else if (!string.IsNullOrWhiteSpace(settings.AspectRatio)) angle = $"{settings.AspectRatio} aspect ratio";
                }
            }
            catch (JsonException)
            {
                // Fall through to neutral defaults; a malformed settings snapshot must not block a render.
            }
        }

        var result = prompt
            .Replace("{{style}}", style, StringComparison.Ordinal)
            .Replace("{{size}}", size, StringComparison.Ordinal)
            .Replace("{{angle}}", angle, StringComparison.Ordinal);
        return result.Trim();
    }

    /// <summary>
    /// Resolves the optional fixed sampler seed from the studio settings snapshot. Returns null when
    /// unset or unparsable so the ComfyUI client falls back to a random seed per render.
    /// </summary>
    private static long? ResolveSeed(string settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(settingsJson, JsonOptions);
            return settings?.Seed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the deterministic negative prompt for this render using the beat snapshot + POV stored
    /// on the prompt record. Returns null when no beat/POV is available (e.g. legacy images) so the
    /// downstream client falls back to its own baseline negative.
    /// </summary>
    private async Task<string?> ResolveNegativePromptAsync(SceneImageRecord image, SceneImageModelFamily modelFamily, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(image.BeatId) || string.IsNullOrWhiteSpace(image.Pov))
            return null;
        if (string.IsNullOrWhiteSpace(image.PromptRecordId))
            return null;

        var promptRecord = await _repository.GetPromptAsync(image.PromptRecordId, cancellationToken);
        if (promptRecord is null || string.IsNullOrWhiteSpace(promptRecord.BeatSnapshotJson))
            return null;

        SceneImageBeat beat;
        try
        {
            beat = JsonSerializer.Deserialize<SceneImageBeat>(promptRecord.BeatSnapshotJson, JsonOptions)
                ?? throw new InvalidOperationException("Beat snapshot is invalid.");
        }
        catch (JsonException)
        {
            return null;
        }

        if (beat.SchemaVersion != SceneImageBeatAnalysisService.CurrentSchemaVersion)
            return null;

        try
        {
            // Model-family aware negative: SDXL uses its heavier guard set, Pony its short set.
            return modelFamily == SceneImageModelFamily.Sdxl
                ? _sdxlPreprocessor.BuildDeterministicBeatNegativePrompt(beat, image.Pov)
                : _preprocessor.BuildDeterministicBeatNegativePrompt(beat, image.Pov);
        }
        catch
        {
            return null;
        }
    }
}
