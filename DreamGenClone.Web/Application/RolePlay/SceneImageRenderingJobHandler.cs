using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Renders an image from a prompt snapshot using the configured image model. Marks the image
/// record Generating → Complete/Failed.
/// </summary>
public sealed class SceneImageRenderingJobHandler : IBackgroundJobHandler, IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneImageRepository _repository;
    private readonly ISceneImageStorageService _storage;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly IImageGenerationClient _imageClient;
    private readonly IIdentityConditionedImageClient _identityClient;
    private readonly IIdentityControlledRequestCompiler _identityRequestCompiler;
    private readonly ISceneImagePromptCompilerRegistry _compilerRegistry;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly ILogger<SceneImageRenderingJobHandler> _logger;
    private readonly IProducedImageRepository _producedImages;

    public SceneImageRenderingJobHandler(
        ISceneImageRepository repository,
        ISceneImageStorageService storage,
        IModelResolutionService modelResolutionService,
        IImageGenerationClient imageClient,
        IIdentityConditionedImageClient identityClient,
        IIdentityControlledRequestCompiler identityRequestCompiler,
        ISceneImagePromptCompilerRegistry compilerRegistry,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<SceneImageRenderingJobHandler> logger,
        IProducedImageRepository producedImages)
    {
        _repository = repository;
        _storage = storage;
        _modelResolutionService = modelResolutionService;
        _imageClient = imageClient;
        _identityClient = identityClient;
        _identityRequestCompiler = identityRequestCompiler;
        _compilerRegistry = compilerRegistry;
        _debugEventSink = debugEventSink;
        _logger = logger;
        _producedImages = producedImages;
    }

    public string JobType => BackgroundJobTypes.SceneImageRendering;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
        => await HandlePayloadAsync(job.PayloadJson, cancellationToken);

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => await HandlePayloadAsync(job.PayloadJson, cancellationToken);

    private async Task HandlePayloadAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneImageRenderingJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image rendering job payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.SessionId))
            throw new InvalidOperationException("Scene image rendering payload is missing SessionId.");
        if (string.IsNullOrWhiteSpace(payload.InteractionId))
            throw new InvalidOperationException("Scene image rendering payload is missing InteractionId.");
        if (string.IsNullOrWhiteSpace(payload.ImageRecordId))
            throw new InvalidOperationException("Scene image rendering payload is missing ImageRecordId.");

        var image = await _repository.GetImageAsync(payload.ImageRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image record '{payload.ImageRecordId}' was not found.");

        if (image.Status is SceneImageStatus.Complete or SceneImageStatus.Cancelled)
        {
            _logger.LogDebug("Skipping scene image rendering; already terminal: ImageRecordId={ImageRecordId}, Status={Status}", image.Id, image.Status);
            return;
        }

        // Mark generating so the UI shows progress (monotonic forward transition).
        image.Status = SceneImageStatus.Generating;
        image.StartedUtc ??= DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.InsertImageAsync(image, cancellationToken);

        ResolvedImageModel? resolved = null;
        try
        {
            // Resolve the image model + provider content policy (fail-fast, no fallback). A user-pinned
            // model (RequestedModelId) wins; otherwise the configured default for RolePlaySceneImage.
            resolved = string.IsNullOrWhiteSpace(image.RequestedModelId)
                ? await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken)
                : await _modelResolutionService.ResolveImageModelByIdAsync(image.RequestedModelId, cancellationToken);
            var compiler = _compilerRegistry.Resolve(resolved.SceneImageModelFamily, resolved.PromptDialect);

            var prompt = image.PromptSnapshot;

            var stopwatch = Stopwatch.StartNew();
            var negative = await ResolveNegativePromptAsync(image, compiler, cancellationToken);
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

            byte[] bytes;
            if (image.RenderMode == SceneImageRenderMode.IdentityControlled)
            {
                bytes = await RenderIdentityControlledAsync(image, injectedPrompt, negative, seed, payload, cancellationToken);
            }
            else
            {
                bytes = await _imageClient.GenerateAsync(resolved, injectedPrompt, image.ImageSize, negative, seed, cancellationToken, ResolveGenerationOptions(image.SettingsJson));
            }
            stopwatch.Stop();

            if (bytes is null || bytes.Length == 0)
            {
                throw new ImageGenerationException(
                    SceneImageRefusalMessage.ForUser(
                        resolved.ModelIdentifier,
                        resolved.ProviderName,
                        SceneImageRefusalMode.EmptyOutput),
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
            image.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            image.Status = SceneImageStatus.Complete;
            image.CompletedUtc = DateTime.UtcNow;
            image.UpdatedUtc = DateTime.UtcNow;
            if (!await _repository.TryCompleteImageAsync(image, cancellationToken))
            {
                _logger.LogInformation("Skipping scene image completion after concurrent terminal transition: ImageRecordId={ImageRecordId}", image.Id);
                return;
            }

            var producedImage = new ProducedImage
            {
                Kind = ProducedImageKind.MomentImage,
                SessionId = payload.SessionId,
                InteractionId = payload.InteractionId,
                VisionSource = ProducedImageVisionSource.BeatMetadata,
                VisionText = image.PromptSnapshot,
                PromptCompiled = image.PromptSnapshot,
                NegativePrompt = negative,
                Seed = seed,
                ModelId = resolved.ModelIdentifier,
                EndpointId = resolved.ProviderName,
                StoragePath = image.FileRelativePath,
                Status = ProducedImageStatus.Undecided,
                RefusalMode = SceneImageRefusalMode.None
            };
            await _producedImages.InsertAsync(producedImage, cancellationToken);

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
            var refusalMode = ex is ImageGenerationException imageException
                ? ResolveRefusalMode(imageException)
                : SceneImageRefusalMode.None;
            var failureMessage = refusalMode == SceneImageRefusalMode.None || resolved is null
                ? ex.Message
                : SceneImageRefusalMessage.ForUser(resolved.ModelIdentifier, resolved.ProviderName, refusalMode);
            image.Status = SceneImageStatus.Failed;
            image.ErrorMessage = failureMessage;
            image.UpdatedUtc = DateTime.UtcNow;
            if (!await _repository.TryFailImageAsync(image, cancellationToken))
            {
                _logger.LogInformation("Skipping scene image failure after concurrent terminal transition: ImageRecordId={ImageRecordId}", image.Id);
                return;
            }

            if (refusalMode != SceneImageRefusalMode.None && resolved is not null)
            {
                await WriteDebugEventAsync("SceneImageRefusalRecorded", payload.SessionId, payload.InteractionId, new
                {
                    recordId = image.Id,
                    model = resolved.ModelIdentifier,
                    provider = resolved.ProviderName,
                    mode = refusalMode.ToString(),
                    message = failureMessage
                }, cancellationToken);
            }

            _logger.LogWarning(
                "Scene image rendering failed: SessionId={SessionId}, ImageRecordId={ImageRecordId}, Error={ErrorMessage}",
                payload.SessionId,
                image.Id,
                failureMessage);

            throw;
        }
    }

    private static SceneImageRefusalMode ResolveRefusalMode(ImageGenerationException exception)
    {
        if (string.Equals(exception.ReasonCode, "empty_response", StringComparison.Ordinal))
            return SceneImageRefusalMode.EmptyOutput;
        if (string.Equals(exception.ReasonCode, "payment_required", StringComparison.Ordinal))
            return SceneImageRefusalMode.None;
        if (exception.StatusCode is >= 400 and < 500)
            return SceneImageRefusalMode.PolicyError;
        return SceneImageRefusalMode.None;
    }

    private async Task<byte[]> RenderIdentityControlledAsync(
        SceneImageRecord image,
        string prompt,
        string? negative,
        long? seed,
        SceneImageRenderingJobPayload payload,
        CancellationToken cancellationToken)
    {
        // Identity models are resolved through the identity path only: mechanism, strength and
        // adapter ref are required configuration. Missing/invalid config fails fast here. A user-pinned
        // model (RequestedModelId) wins; otherwise the configured default identity model.
        var identityModel = string.IsNullOrWhiteSpace(image.RequestedModelId)
            ? await _modelResolutionService.ResolveIdentityImageModelAsync(null, cancellationToken)
            : await _modelResolutionService.ResolveIdentityImageModelByIdAsync(image.RequestedModelId, cancellationToken);

        var compiled = await _identityRequestCompiler.CompileAsync(
            new IdentityRequestCompilationInput(image, prompt, negative ?? string.Empty, seed),
            cancellationToken);

        await WriteDebugEventAsync("IdentityRenderRequestSubmitted", payload.SessionId, payload.InteractionId, new
        {
            recordId = image.Id,
            checkpoint = identityModel.ModelIdentifier,
            mechanism = identityModel.Mechanism,
            strength = identityModel.IdentityStrength,
            packs = compiled.References,
            seed = seed.HasValue ? seed.Value.ToString() : "random",
            positive = prompt,
            negative = negative ?? string.Empty
        }, cancellationToken);

        return await _identityClient.GenerateAsync(identityModel, compiled.Request, cancellationToken);
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
    /// Resolves the optional ComfyUI sampler/CLIP overrides from the studio settings snapshot.
    /// Returns null when none are set so the client applies its model-family default recipe.
    /// </summary>
    private static SceneImageGenerationOptions? ResolveGenerationOptions(string settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(settingsJson, JsonOptions);
            if (settings is null) return null;
            int? clipSkip = int.TryParse(settings.ClipSkip, out var parsed) ? parsed : null;
            return new SceneImageGenerationOptions
            {
                Cfg = settings.Cfg,
                Steps = settings.Steps,
                SamplerName = settings.SamplerName,
                Scheduler = settings.Scheduler,
                ClipSkip = clipSkip
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the user-editable negative prompt from the studio settings snapshot. Returns null when
    /// unset or blank so the deterministic beat negative (or client baseline) applies instead.
    /// </summary>
    private static string? ResolveNegativeOverride(string settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(settingsJson, JsonOptions);
            var negative = settings?.NegativePrompt;
            return string.IsNullOrWhiteSpace(negative) ? null : negative.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the negative prompt for this render. A user-editable negative in the studio settings
    /// snapshot takes precedence; otherwise the deterministic beat negative (beat snapshot + POV) is
    /// used. Returns null when neither is available so the client falls back to its baseline negative.
    /// </summary>
    private async Task<string?> ResolveNegativePromptAsync(SceneImageRecord image, ISceneImagePromptCompiler compiler, CancellationToken cancellationToken)
    {
        var overrideNegative = ResolveNegativeOverride(image.SettingsJson);
        if (!string.IsNullOrWhiteSpace(overrideNegative))
            return overrideNegative;

        if (!string.IsNullOrWhiteSpace(image.CompiledMediaBriefId))
        {
            if (string.IsNullOrWhiteSpace(image.ProductionGroupId))
                throw new InvalidOperationException("A canonical production render has a compiled Still brief without a production group.");
            return compiler.CanonicalNegativePrompt;
        }

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
            return compiler.BuildNegativePrompt(beat, image.Pov);
        }
        catch
        {
            return null;
        }
    }
}
