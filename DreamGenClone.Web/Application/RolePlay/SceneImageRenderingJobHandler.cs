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
using DreamGenClone.Web.Application.ModelManager;
using DreamGenClone.Web.Application.RolePlay.Editing;
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
    private readonly IPoseConditionedImageClient _poseClient;
    private readonly IPoseImageModelResolver _poseResolver;
    private readonly ISceneImagePromptCompilerRegistry _compilerRegistry;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly ILogger<SceneImageRenderingJobHandler> _logger;
    private readonly IProducedImageRepository _producedImages;

    /// <summary>
    /// Optional: only a native-reference render needs it, and such a render fails fast when it is absent
    /// rather than degrading to a prompt-only image that silently drops the references.
    /// </summary>
    private readonly IReferenceConditionedImageClient? _referenceConditionedClient;

    /// <summary>
    /// Optional: only a native-reference render that carries approved scene-asset references needs it,
    /// and such a render fails fast when it is absent rather than dropping the references.
    /// </summary>
    private readonly MediaEditReferenceResolver? _referenceResolver;

    /// <summary>Optional: only a native-reference render that carries identity faces needs it.</summary>
    private readonly ICharacterImageAssetStorageService? _identityStorage;

    /// <summary>Owns the ONE identity mechanism decision (IP-Adapter/PuLID graph vs the model's own reference slots).</summary>
    private readonly IReferenceStrategyResolver _referenceStrategyResolver;

    /// <summary>Resolves the approved canonical face a selected identity pack contributes.</summary>
    private readonly IdentityFaceReferenceResolver? _identityFaceResolver;

    public SceneImageRenderingJobHandler(
        ISceneImageRepository repository,
        ISceneImageStorageService storage,
        IModelResolutionService modelResolutionService,
        IImageGenerationClient imageClient,
        IIdentityConditionedImageClient identityClient,
        IIdentityControlledRequestCompiler identityRequestCompiler,
        IPoseConditionedImageClient poseClient,
        IPoseImageModelResolver poseResolver,
        ISceneImagePromptCompilerRegistry compilerRegistry,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<SceneImageRenderingJobHandler> logger,
        IProducedImageRepository producedImages,
        IReferenceConditionedImageClient? referenceConditionedClient = null,
        MediaEditReferenceResolver? referenceResolver = null,
        ICharacterImageAssetStorageService? identityStorage = null,
        IReferenceStrategyResolver? referenceStrategyResolver = null,
        IdentityFaceReferenceResolver? identityFaceResolver = null)
    {
        _repository = repository;
        _storage = storage;
        _modelResolutionService = modelResolutionService;
        _imageClient = imageClient;
        _identityClient = identityClient;
        _identityRequestCompiler = identityRequestCompiler;
        _poseClient = poseClient;
        _poseResolver = poseResolver;
        _compilerRegistry = compilerRegistry;
        _debugEventSink = debugEventSink;
        _logger = logger;
        _producedImages = producedImages;
        _referenceConditionedClient = referenceConditionedClient;
        _referenceResolver = referenceResolver;
        _identityStorage = identityStorage;
        _referenceStrategyResolver = referenceStrategyResolver;
        _identityFaceResolver = identityFaceResolver;
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
                negative = negative ?? "(baseline client negative)",
                // The sampler recipe is part of the audited request: without it a wrong-envelope render (the 2.1
                // cfg-5 / dpmpp_2m_sde case found on 2026-09-24) cannot be told apart from a bad prompt.
                family = resolved.SceneImageModelFamily.ToString(),
                steps = ResolveAuditedSteps(resolved, image.SettingsJson),
                cfg = ResolveAuditedCfg(resolved, image.SettingsJson),
                sampler = ResolveAuditedSampler(resolved, image.SettingsJson),
                scheduler = ResolveAuditedScheduler(resolved, image.SettingsJson)
            }, cancellationToken);

            byte[] bytes;
            var poseReference = ReadPoseReference(image.SettingsJson);

            // HOW this model carries a pose is decided ONCE, by the same resolver that owns every other
            // capability question. Two mechanisms exist and they are not interchangeable: a configured OpenPose
            // ControlNet graph conditions the sampler, while a native-reference model takes the skeleton as one
            // more reference image in the same call (measured 2026-09-23: 2.1 follows the skeleton's geometry
            // even when the prompt says the opposite). A model that qualifies neither fails here with its reason
            // rather than rendering without the pose.
            ReferenceStrategyResolution? poseStrategy = null;
            if (poseReference is not null)
                poseStrategy = await ResolvePoseStrategyAsync(image, resolved, cancellationToken);
            var poseIsNative = poseStrategy is not null
                && string.Equals(poseStrategy.Strategy, ReferenceStrategyResolver.IdentityNativeMultiReference, StringComparison.OrdinalIgnoreCase);

            if (image.RenderMode == SceneImageRenderMode.NativeReference)
            {
                if (poseReference is not null && !poseIsNative)
                {
                    throw new InvalidOperationException(
                        "A native-reference render cannot run the pose ControlNet graph: this model carries a pose by "
                        + $"'{poseStrategy!.Strategy}'. Use the pose-conditioned render instead, or select a model that "
                        + "takes its pose as a reference image.");
                }

                bytes = await RenderNativeReferenceAsync(
                    image, resolved, injectedPrompt, negative ?? string.Empty, seed, payload, cancellationToken,
                    poseReference is null ? null : await BuildPoseReferenceAsync(poseReference, cancellationToken));
            }
            else if (image.RenderMode == SceneImageRenderMode.IdentityControlled)
            {
                if (poseReference is not null && !poseIsNative)
                {
                    throw new InvalidOperationException(
                        "Pose conditioning cannot be combined with identity-controlled rendering: that mechanism "
                        + $"conditions identity through '{poseStrategy!.Strategy}' and the pose through a separate "
                        + "ControlNet graph. Select a model that carries both as references.");
                }

                bytes = await RenderIdentityControlledAsync(
                    image, resolved, injectedPrompt, negative, seed, payload, cancellationToken,
                    poseReference is null ? null : await BuildPoseReferenceAsync(poseReference, cancellationToken));
            }
            else if (poseReference is not null && poseIsNative)
            {
                // A plain render that carries a pose on a native-reference model IS a native-reference render:
                // the skeleton travels as the reference set.
                bytes = await RenderNativeReferenceAsync(
                    image, resolved, injectedPrompt, negative ?? string.Empty, seed, payload, cancellationToken,
                    await BuildPoseReferenceAsync(poseReference, cancellationToken));
            }
            else if (poseReference is not null)
            {
                bytes = await RenderPoseControlledAsync(image, injectedPrompt, negative, seed, poseReference, payload, cancellationToken);
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

    /// <summary>
    /// Native multi-reference render: the references themselves condition the generation in ONE call.
    /// No identity mechanism, no editing pass, no prompt-only fallback — an empty or oversized
    /// reference set fails here rather than silently producing an unconditioned image.
    /// </summary>
    private async Task<byte[]> RenderNativeReferenceAsync(
        SceneImageRecord image,
        ResolvedImageModel resolved,
        string prompt,
        string negative,
        long? seed,
        SceneImageRenderingJobPayload payload,
        CancellationToken cancellationToken,
        ReferenceConditionedImageInput? poseReference = null)
    {
        var references = await BuildNativeReferencesAsync(image, resolved, cancellationToken);
        // Pose goes LAST: the measured order is faces, then approved scene assets, then the pose skeleton. Slot
        // order controls placement, and the proof landed all three in any order, so this is a deterministic
        // convention rather than a capability requirement.
        if (poseReference is not null)
            references.Add(poseReference);

        if (references.Count == 0)
        {
            throw new InvalidOperationException(
                "A native-reference render needs at least one reference image. Select a character identity, an "
                + "approved scene-asset reference (a location, for example), or a pose before rendering, or use the "
                + "prompt-only path.");
        }

        var request = new ReferenceConditionedImageRequest
        {
            PositivePrompt = prompt,
            NegativePrompt = negative,
            Size = image.ImageSize,
            Seed = seed,
            Options = ResolveGenerationOptions(image.SettingsJson),
            References = references,
            CorrelationId = image.Id
        };

        await WriteDebugEventAsync("NativeReferenceRenderSubmitted", payload.SessionId, payload.InteractionId, new
        {
            recordId = image.Id,
            checkpoint = resolved.ModelIdentifier,
            provider = resolved.ProviderName,
            registeredModelId = resolved.RegisteredModelId,
            referenceCount = references.Count,
            references = references.Select(reference => reference.SemanticRole).ToList(),
            seed = seed.HasValue ? seed.Value.ToString() : "random",
            positive = prompt,
            negative
        }, cancellationToken);

        return await (_referenceConditionedClient
                ?? throw new InvalidOperationException(
                    "A native-reference render requires a reference-conditioned image client; the configured provider "
                    + "protocol has none (only ComfyUI-backed Qwen-Image-2.1 does)."))
            .GenerateWithReferencesAsync(resolved, request, cancellationToken);
    }

    /// <summary>
    /// The ordered reference set for a native-reference render: identity faces FIRST (they anchor the
    /// composition — measured 2026-09-23), then the approved scene-asset references (location, pose,
    /// props). Order is request data, and the resolver revalidates every asset against its approved
    /// immutable selection, so a stale reference fails fast instead of being rendered anyway.
    /// </summary>
    private async Task<List<ReferenceConditionedImageInput>> BuildNativeReferencesAsync(
        SceneImageRecord image,
        ResolvedImageModel resolved,
        CancellationToken cancellationToken)
    {
        var references = new List<ReferenceConditionedImageInput>();

        if (!string.IsNullOrWhiteSpace(image.IdentityReferenceBindingsJson))
        {
            var identityStorage = _identityStorage
                ?? throw new InvalidOperationException(
                    "A native-reference render with character identity faces requires the identity asset storage service.");

            var bindings = JsonSerializer.Deserialize<List<NativeReferenceIdentityBinding>>(
                    image.IdentityReferenceBindingsJson, JsonOptions)
                ?? throw new InvalidOperationException("Identity reference bindings are invalid.");

            if (bindings.Count == 0
                || !bindings.Select(binding => binding.Ordinal).SequenceEqual(bindings.Select(binding => binding.Ordinal).OrderBy(ordinal => ordinal)))
            {
                throw new InvalidOperationException("Identity reference bindings must be non-empty and ordinally ordered.");
            }

            foreach (var binding in bindings.OrderBy(binding => binding.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(binding.FileRelativePath) || string.IsNullOrWhiteSpace(binding.Sha256))
                {
                    throw new InvalidOperationException("Identity reference binding is missing exact asset metadata.");
                }

                await using var stream = await identityStorage.OpenReadAsync(binding.FileRelativePath, cancellationToken);
                references.Add(new ReferenceConditionedImageInput
                {
                    SemanticRole = $"identity face for {binding.CharacterName}",
                    FileName = $"{binding.CharacterId}.png",
                    Content = await ReadAllBytesAsync(stream, cancellationToken)
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(image.AppliedReferenceBindingsJson))
        {
            var applications = JsonSerializer.Deserialize<List<ReferenceApplicationSelection>>(
                    image.AppliedReferenceBindingsJson, JsonOptions)
                ?? throw new InvalidOperationException("Applied reference bindings are invalid.");

            var resolver = _referenceResolver
                ?? throw new InvalidOperationException(
                    "A native-reference render with scene-asset references requires the reference resolver.");

            var assetReferences = await resolver.ResolveAsync(
                resolved.RegisteredModelId ?? image.RequestedModelId,
                applications,
                qualifiedStrategy: "NativeMultiReference",
                cancellationToken);

            foreach (var reference in assetReferences)
            {
                await using var stream = await reference.OpenAsync(cancellationToken);
                references.Add(new ReferenceConditionedImageInput
                {
                    SemanticRole = reference.Description,
                    FileName = reference.FileName,
                    Content = await ReadAllBytesAsync(stream, cancellationToken)
                });
            }
        }

        return references;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private sealed record NativeReferenceIdentityBinding
    {
        public int Ordinal { get; init; }
        public string CharacterId { get; init; } = string.Empty;
        public string CharacterName { get; init; } = string.Empty;
        public string? FileRelativePath { get; init; }
        public string? Sha256 { get; init; }
    }

    private async Task<byte[]> RenderIdentityControlledAsync(
        SceneImageRecord image,
        ResolvedImageModel resolved,
        string prompt,
        string? negative,
        long? seed,
        SceneImageRenderingJobPayload payload,
        CancellationToken cancellationToken,
        ReferenceConditionedImageInput? poseReference = null)
    {
        // HOW this model carries identity is ONE decision, asked of the same resolver the picker asks, so a
        // render can never take a mechanism the UI would not offer (and vice versa). The two mechanisms are NOT
        // interchangeable: a configured IP-Adapter/PuLID graph conditions the sampler's model input, while a
        // native-reference model takes the approved face as an image alongside the prompt in one call. Falling
        // back from one to the other would produce an unconditioned image that looks exactly like a conditioned
        // one, so an unavailable strategy fails here instead.
        var identityModelId = string.IsNullOrWhiteSpace(resolved.RegisteredModelId)
            ? image.RequestedModelId
            : resolved.RegisteredModelId;
        if (string.IsNullOrWhiteSpace(identityModelId))
        {
            throw new InvalidOperationException(
                "Identity rendering requires the exact registered image model id.");
        }

        var strategyResolver = _referenceStrategyResolver
            ?? throw new InvalidOperationException(
                "Identity rendering requires the reference capability resolver; without it the identity "
                + "mechanism cannot be decided.");
        var strategy = await ReferenceStrategyResolver.ResolveIdentityAsync(
            strategyResolver, identityModelId, cancellationToken);
        if (!strategy.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Identity conditioning was requested, but this model cannot carry it: {strategy.Reason}");
        }

        if (string.Equals(
                strategy.Strategy,
                ReferenceStrategyResolver.IdentityNativeMultiReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return await RenderNativeIdentityAsync(image, resolved, strategy, prompt, negative, seed, payload, cancellationToken, poseReference);
        }

        // The model must declare and qualify an identity MECHANISM; the resolver above fails fast otherwise.
        // A user-pinned model (RequestedModelId) wins; otherwise the configured default identity model.
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

    /// <summary>
    /// Identity through the model's OWN reference slots: each selected identity pack contributes its approved
    /// canonical face as a reference image in the same call as the prompt. The pack and its face are re-read
    /// here rather than trusted from the queue, so a reference that was superseded, unapproved or deleted
    /// between queueing and rendering fails the render instead of silently producing a different person.
    /// </summary>
    private async Task<byte[]> RenderNativeIdentityAsync(
        SceneImageRecord image,
        ResolvedImageModel resolved,
        ReferenceStrategyResolution strategy,
        string prompt,
        string? negative,
        long? seed,
        SceneImageRenderingJobPayload payload,
        CancellationToken cancellationToken,
        ReferenceConditionedImageInput? poseReference = null)
    {
        var faceResolver = _identityFaceResolver
            ?? throw new InvalidOperationException(
                "A native-reference identity render requires the identity face reference resolver.");
        var identityStorage = _identityStorage
            ?? throw new InvalidOperationException(
                "A native-reference identity render requires the identity asset storage service.");

        var packSelections = IdentityControlledRequestCompiler.DeserializePackSelections(image);
        if (packSelections.Count == 0)
        {
            throw new InvalidOperationException(
                "A native-reference identity render needs at least one selected identity pack.");
        }

        var references = new List<ReferenceConditionedImageInput>(packSelections.Count);
        for (var index = 0; index < packSelections.Count; index++)
        {
            var selection = packSelections[index];
            var face = await faceResolver.ResolveCanonicalFaceAsync(index + 1, selection.PackId, cancellationToken);
            byte[] referenceBytes;
            await using (var stream = await identityStorage.OpenReadAsync(face.FileRelativePath, cancellationToken))
            {
                referenceBytes = await ReadAllBytesAsync(stream, cancellationToken);
            }

            if (referenceBytes.Length == 0)
            {
                throw new InvalidOperationException($"Identity face asset '{face.FaceAssetId}' contains no image bytes.");
            }

            var label = string.IsNullOrWhiteSpace(selection.CharacterLabel)
                ? "character"
                : selection.CharacterLabel;
            references.Add(new ReferenceConditionedImageInput
            {
                SemanticRole = $"approved identity face for {label} ({face.FaceView?.ToString() ?? "unspecified view"})",
                FileName = $"{face.FaceAssetId}.png",
                Content = referenceBytes
            });
        }

        // Pose travels as the last reference: a native-reference model carries a pose as an image, so identity
        // and pose compose in ONE call instead of needing two graphs (the ControlNet route's constraint).
        if (poseReference is not null)
            references.Add(poseReference);

        await WriteDebugEventAsync("IdentityNativeReferenceRenderSubmitted", payload.SessionId, payload.InteractionId, new
        {
            recordId = image.Id,
            checkpoint = resolved.ModelIdentifier,
            provider = resolved.ProviderName,
            registeredModelId = resolved.RegisteredModelId,
            strategy = strategy.Strategy,
            referenceCount = references.Count,
            poseReference = poseReference?.FileName,
            packs = packSelections
                .Select(selection => new { selection.PackId, selection.CharacterLabel })
                .ToList(),
            seed = seed.HasValue ? seed.Value.ToString() : "random",
            positive = prompt,
            negative = negative ?? string.Empty
        }, cancellationToken);

        return await (_referenceConditionedClient
                ?? throw new InvalidOperationException(
                    "A native-reference identity render requires a reference-conditioned image client; the "
                    + "configured provider protocol has none (only ComfyUI-backed models do)."))
            .GenerateWithReferencesAsync(
                resolved,
                new ReferenceConditionedImageRequest
                {
                    PositivePrompt = prompt,
                    NegativePrompt = negative ?? string.Empty,
                    Size = image.ImageSize,
                    Seed = seed,
                    Options = ResolveGenerationOptions(image.SettingsJson),
                    References = references,
                    CorrelationId = image.Id
                },
                cancellationToken);
    }

    private async Task<byte[]> RenderPoseControlledAsync(
        SceneImageRecord image,
        string prompt,
        string? negative,
        long? seed,
        SceneImagePoseReference pose,
        SceneImageRenderingJobPayload payload,
        CancellationToken cancellationToken)
    {
        // This is the ControlNet mechanism branch. It is reached only when the capability resolver says the model
        // carries a pose through an OpenPose ControlNet graph (the native-reference route is handled earlier, as a
        // reference image). The pose resolver then fails fast unless the model also declares + qualifies
        // PoseControlNet on a ComfyUI provider — no silent text-only fallback.
        if (string.IsNullOrWhiteSpace(image.RequestedModelId))
        {
            throw new InvalidOperationException(
                "ControlNet pose conditioning requires a user-pinned local ComfyUI image model. Pin a model that "
                + "qualifies PoseControlNet (e.g. BigLust/Juggernaut/FLUX on the local ComfyUI) in the Studio, then retry.");
        }
        var poseModel = await _poseResolver.ResolveAsync(image.RequestedModelId, cancellationToken);

        if (string.IsNullOrWhiteSpace(pose.StoragePath))
        {
            throw new InvalidOperationException("Pose-controlled rendering requires a stored pose image.");
        }
        if (pose.Strength is <= 0 or > 1)
        {
            throw new InvalidOperationException($"Pose ControlNet strength must be in (0, 1], but was {pose.Strength}.");
        }

        byte[] poseBytes;
        await using (var source = await _storage.OpenReadAsync(pose.StoragePath, cancellationToken))
        using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, cancellationToken);
            poseBytes = buffer.ToArray();
        }
        if (poseBytes.Length == 0)
        {
            throw new InvalidOperationException($"Stored pose image '{pose.StoragePath}' contains no image bytes.");
        }

        await WriteDebugEventAsync("PoseRenderRequestSubmitted", payload.SessionId, payload.InteractionId, new
        {
            recordId = image.Id,
            checkpoint = poseModel.ModelIdentifier,
            controlNet = poseModel.ControlNetAdapterRef,
            strength = pose.Strength,
            poseStoragePath = pose.StoragePath,
            seed = seed.HasValue ? seed.Value.ToString() : "random",
            positive = prompt,
            negative = negative ?? string.Empty
        }, cancellationToken);

        return await _poseClient.GenerateAsync(poseModel, new PoseConditionedImageRequest
        {
            PositivePrompt = prompt,
            NegativePrompt = negative ?? string.Empty,
            Size = image.ImageSize,
            Seed = seed,
            PoseImageBytes = poseBytes,
            Strength = pose.Strength,
            CorrelationId = image.Id
        }, cancellationToken);
    }

    /// <summary>
    /// How this model carries a pose, decided by the one resolver that owns capability questions. Fails fast with the
    /// model's own reason when it qualifies neither an OpenPose ControlNet graph nor native references, rather than
    /// rendering an unconditioned image that would look identical to a posed one.
    /// </summary>
    private async Task<ReferenceStrategyResolution> ResolvePoseStrategyAsync(
        SceneImageRecord image,
        ResolvedImageModel resolved,
        CancellationToken cancellationToken)
    {
        var modelId = string.IsNullOrWhiteSpace(resolved.RegisteredModelId)
            ? image.RequestedModelId
            : resolved.RegisteredModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("Pose conditioning requires the exact registered image model id.");
        }

        var resolver = _referenceStrategyResolver
            ?? throw new InvalidOperationException(
                "Pose conditioning requires the reference capability resolver; without it the pose mechanism "
                + "cannot be decided.");
        var strategy = await ReferenceStrategyResolver.ResolvePoseAsync(resolver, modelId, cancellationToken);
        if (!strategy.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Pose conditioning was requested, but this model cannot carry it: {strategy.Reason}");
        }

        return strategy;
    }

    /// <summary>
    /// The pose skeleton as a reference image. A skeleton is a ControlNet conditioning map, but a native-reference
    /// model reads it as pose guidance (measured 2026-09-23: the figure follows the skeleton's geometry even when the
    /// prompt says the opposite), so one stored file drives both mechanisms.
    /// </summary>
    private async Task<ReferenceConditionedImageInput> BuildPoseReferenceAsync(
        SceneImagePoseReference pose,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pose.StoragePath))
        {
            throw new InvalidOperationException("Pose conditioning requires a stored pose image.");
        }

        byte[] poseBytes;
        await using (var source = await _storage.OpenReadAsync(pose.StoragePath, cancellationToken))
        {
            poseBytes = await ReadAllBytesAsync(source, cancellationToken);
        }

        if (poseBytes.Length == 0)
        {
            throw new InvalidOperationException($"Stored pose image '{pose.StoragePath}' contains no image bytes.");
        }

        return new ReferenceConditionedImageInput
        {
            SemanticRole = "pose reference (OpenPose skeleton)",
            FileName = "pose-skeleton.png",
            Content = poseBytes
        };
    }

    /// <summary>Reads the optional pose conditioning from the settings snapshot; null when absent or malformed.</summary>
    private static SceneImagePoseReference? ReadPoseReference(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(settingsJson, JsonOptions);
            return settings?.PoseReference is { StoragePath.Length: > 0 } ? settings.PoseReference : null;
        }
        catch (JsonException)
        {
            return null;
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
    /// The sampler recipe actually submitted, for the audit event. Qwen-Image-2.1 takes its envelope from the
    /// MODEL's qualification - the studio's sampler/cfg/steps are SDXL-family values and are deliberately not
    /// applied to it (a 2.1 render that received cfg 5 / dpmpp_2m_sde / karras produced blown-out, grainy output,
    /// reported 2026-09-24). Every other family still reports the studio override when one is set, and the
    /// client's documented family default otherwise.
    /// </summary>
    private static string ResolveAuditedValue(
        ResolvedImageModel model, string settingsJson, Func<QwenImage21Refs, string> fromQwen, Func<SceneImageGenerationOptions, string> fromOptions, string familyDefault)
    {
        if (model.SceneImageModelFamily == SceneImageModelFamily.QwenImage21 && model.QwenImage21 is { } refs)
        {
            return fromQwen(refs);
        }

        var options = ResolveGenerationOptions(settingsJson);
        return options is null ? familyDefault : fromOptions(options);
    }

    private static string ResolveAuditedSteps(ResolvedImageModel model, string settingsJson) =>
        ResolveAuditedValue(model, settingsJson, refs => refs.Steps.ToString(), options => options.Steps?.ToString() ?? "model-default", "model-default");

    private static string ResolveAuditedCfg(ResolvedImageModel model, string settingsJson) =>
        ResolveAuditedValue(model, settingsJson, refs => refs.Cfg.ToString("0.###"), options => options.Cfg?.ToString("0.###") ?? "model-default", "model-default");

    private static string ResolveAuditedSampler(ResolvedImageModel model, string settingsJson) =>
        ResolveAuditedValue(model, settingsJson, refs => refs.SamplerName, options => options.SamplerName ?? "model-default", "model-default");

    private static string ResolveAuditedScheduler(ResolvedImageModel model, string settingsJson) =>
        ResolveAuditedValue(model, settingsJson, refs => refs.Scheduler, options => options.Scheduler ?? "model-default", "model-default");

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
