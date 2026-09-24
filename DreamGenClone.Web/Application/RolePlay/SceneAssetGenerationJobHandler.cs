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
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Runs a text-to-image scene asset generation (Juggernaut) for a pending
/// <see cref="SceneAssetKind.PromptGenerated"/> asset, saves the bytes to the asset library, and
/// marks the asset Complete/Failed.
/// </summary>
public sealed class SceneAssetGenerationJobHandler : IBackgroundJobHandler, IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneAssetRepository _repository;
    private readonly ISceneAssetStorageService _storage;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly IImageGenerationClient _imageClient;
    private readonly IPoseConditionedImageClient _poseClient;
    private readonly IPoseImageModelResolver _poseResolver;
    private readonly IStancePoseSkeletonProvider _skeletons;
    private readonly IBodyAngleSkeletonProvider _angleSkeletons;
    private readonly IIdentityConditionedImageClient _identityClient;
    private readonly IReferenceConditionedImageClient _referenceClient;
    private readonly IReferenceStrategyResolver _referenceStrategies;
    private readonly ICharacterImageIdentityRepository _identityRepository;
    private readonly ICharacterImageAssetStorageService _identityStorage;

    /// <summary>
    /// Optional: only an identity-conditioned render needs it, and such a render fails fast when it is absent
    /// rather than substituting a different face. Shared with the scene render path so "which approved face
    /// does this pack contribute" has one implementation.
    /// </summary>
    private readonly IdentityFaceReferenceResolver? _identityFaceResolver;
    private readonly ILogger<SceneAssetGenerationJobHandler> _logger;

    public SceneAssetGenerationJobHandler(
        ISceneAssetRepository repository,
        ISceneAssetStorageService storage,
        IModelResolutionService modelResolutionService,
        IImageGenerationClient imageClient,
        IPoseConditionedImageClient poseClient,
        IPoseImageModelResolver poseResolver,
        IStancePoseSkeletonProvider skeletons,
        IBodyAngleSkeletonProvider angleSkeletons,
        IIdentityConditionedImageClient identityClient,
        IReferenceConditionedImageClient referenceClient,
        IReferenceStrategyResolver referenceStrategies,
        ICharacterImageIdentityRepository identityRepository,
        ICharacterImageAssetStorageService identityStorage,
        ILogger<SceneAssetGenerationJobHandler> logger,
        IdentityFaceReferenceResolver? identityFaceResolver = null)
    {
        _repository = repository;
        _storage = storage;
        _modelResolutionService = modelResolutionService;
        _imageClient = imageClient;
        _poseClient = poseClient;
        _poseResolver = poseResolver;
        _skeletons = skeletons;
        _angleSkeletons = angleSkeletons;
        _identityClient = identityClient;
        _referenceClient = referenceClient;
        _referenceStrategies = referenceStrategies;
        _identityRepository = identityRepository;
        _identityStorage = identityStorage;
        _identityFaceResolver = identityFaceResolver;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneAssetGeneration;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
        => await HandleAsync(job.PayloadJson, cancellationToken);

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => await HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneAssetGenerationJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene asset generation payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AssetId))
            throw new InvalidOperationException("Scene asset generation payload requires an AssetId.");
        if (string.IsNullOrWhiteSpace(payload.ImageId))
            throw new InvalidOperationException("Scene asset generation payload requires an ImageId.");
        if (string.IsNullOrWhiteSpace(payload.ModelId))
            throw new InvalidOperationException("Scene asset generation payload requires an exact ModelId.");
        if (string.IsNullOrWhiteSpace(payload.ImageSize))
            throw new InvalidOperationException("Scene asset generation payload requires an ImageSize.");

        var asset = await _repository.GetAsync(payload.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{payload.AssetId}' was not found.");
        if (!string.IsNullOrWhiteSpace(payload.CandidateBatchId))
        {
            await _repository.UpdateCandidateFieldsAsync(
                asset.Id,
                payload.CandidateBatchId,
                SceneAssetCandidateDecision.Undecided,
                null,
                null,
                cancellationToken);
        }
        var image = await _repository.GetImageAsync(payload.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset image '{payload.ImageId}' was not found.");
        if (!string.Equals(image.AssetId, asset.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene asset generation image does not belong to the payload asset.");
        if (image.Status == SceneAssetStatus.Complete)
            return;
        if (image.Kind != SceneAssetKind.PromptGenerated)
            throw new InvalidOperationException("Scene asset generation jobs require a PromptGenerated image.");

        image.Status = SceneAssetStatus.Pending;
        image.StartedUtc ??= DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertImageAsync(image, cancellationToken);

        try
        {
            var model = await _modelResolutionService.ResolveImageModelByIdAsync(payload.ModelId, cancellationToken);

            // Whether this text is already model-ready is STATED by the image, never guessed from the text's shape.
            // An image that names its prompt compiler carries that compiler's family framing and its own negative;
            // recompiling it would repeat the Pony quality string and push the prompt past its qualified length.
            var precompiled = !string.IsNullOrWhiteSpace(image.PromptCompilerId);
            string compiledPrompt;
            string? compilerId;
            string? compilerVersion;
            string? negativePrompt;
            if (precompiled)
            {
                compiledPrompt = image.Prompt;
                compilerId = image.PromptCompilerId;
                compilerVersion = null;
                negativePrompt = image.NegativePrompt;
            }
            else
            {
                var compilation = SceneAssetPromptCompiler.Compile(
                    image.Prompt,
                    asset.Type ?? throw new InvalidOperationException("Scene asset generation requires an explicit asset type."),
                    model);
                compiledPrompt = compilation.Prompt;
                compilerId = compilation.CompilerId;
                compilerVersion = compilation.CompilerVersion;
                // That compiler authors no negative: the documents' negatives are empty by design, so there is none
                // to pass. The client is given null rather than an empty string to keep "no negative authored"
                // distinguishable from "the author chose an empty one".
                negativePrompt = null;
            }

            image.AssociationMetadataJson = JsonSerializer.Serialize(new
            {
                semanticDescription = image.Prompt,
                compiledPrompt,
                compilerId,
                compilerVersion,
                requestedModelId = payload.ModelId,
                imageSize = payload.ImageSize,
                negativePrompt,
                referenceApplicationsJson = payload.ReferenceApplicationsJson
            }, JsonOptions);
            await _repository.UpsertImageAsync(image, cancellationToken);

            // HOW this model carries a pose is decided ONCE, by the same resolver that owns identity. A model with a
            // qualified OpenPose ControlNet graph keeps the dedicated graph; a native-reference model takes the
            // skeleton as one more reference image in the same call (measured 2026-09-23). A model that qualifies
            // neither fails with its reason rather than rendering an unconditioned image.
            ReferenceStrategyResolution? poseStrategy = null;
            if (!string.IsNullOrWhiteSpace(payload.PoseStance))
                poseStrategy = await ResolvePoseStrategyAsync(payload.ModelId, cancellationToken);
            var poseIsNative = poseStrategy is not null
                && string.Equals(poseStrategy.Strategy, ReferenceStrategyResolver.IdentityNativeMultiReference, StringComparison.OrdinalIgnoreCase);

            var bytes = !string.IsNullOrWhiteSpace(payload.BodyAngleView)
                ? await RenderBodyAngleAsync(image, model, payload, compiledPrompt, negativePrompt, cancellationToken)
                : string.IsNullOrWhiteSpace(payload.IdentityFaceAssetId)
                    ? string.IsNullOrWhiteSpace(payload.PoseStance)
                        ? await _imageClient.GenerateAsync(model, compiledPrompt, payload.ImageSize, negativePrompt, null, cancellationToken)
                            ?? throw new InvalidOperationException("The image model returned no image bytes.")
                        : poseIsNative
                            ? await RenderNativePoseAsync(image, model, payload, compiledPrompt, negativePrompt, cancellationToken)
                            : await RenderPoseConditionedAsync(image, payload, compiledPrompt, negativePrompt, cancellationToken)
                    : await RenderIdentityConditionedAsync(
                        model, image, payload, compiledPrompt, negativePrompt,
                        asset.Type ?? throw new InvalidOperationException("Scene asset generation requires an explicit asset type."),
                        cancellationToken,
                        poseIsNative);
            image.ModelSnapshotJson = JsonSerializer.Serialize(new
            {
                requestedModelId = payload.ModelId,
                model.ModelIdentifier,
                model.ProviderName,
                model.SceneImageModelFamily,
                model.PromptDialect,
                compilerId,
                compilerVersion
            }, JsonOptions);
            await CompleteWithBytesAsync(image, $"{image.Id}.png", bytes, cancellationToken);

            _logger.LogInformation("Scene asset image generated: AssetId={AssetId}, ImageId={ImageId}, Model={Model}", asset.Id, image.Id, model.ModelIdentifier);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            image.Status = SceneAssetStatus.Failed;
            image.ErrorMessage = ex.Message;
            image.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertImageAsync(image, cancellationToken);
            _logger.LogWarning("Scene asset image generation failed: AssetId={AssetId}, ImageId={ImageId}, Error={Error}", asset.Id, image.Id, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// How this model carries a pose, decided by the one resolver that owns capability questions. Fails with the
    /// model's own reason when it qualifies neither an OpenPose ControlNet graph nor native references, rather than
    /// rendering an unconditioned image that would look identical to a posed one.
    /// </summary>
    private async Task<ReferenceStrategyResolution> ResolvePoseStrategyAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("Pose conditioning requires the exact registered image model id.");
        }

        var strategy = await ReferenceStrategyResolver.ResolvePoseAsync(_referenceStrategies, modelId, cancellationToken);
        if (!strategy.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Pose conditioning was requested, but this model cannot carry it: {strategy.Reason}");
        }

        return strategy;
    }

    /// <summary>
    /// The committed stance skeleton as a reference image. The stance must be one the provider has verified — the
    /// provider refuses an unverified stance rather than substituting one. ControlNet strength does not apply to a
    /// reference-image pose, so it is deliberately not used on this route.
    /// </summary>
    private async Task<ReferenceConditionedImageInput> ReadPoseSkeletonAsync(
        SceneAssetGenerationJobPayload payload,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<BodyReferenceStance>(payload.PoseStance, ignoreCase: false, out var stance))
        {
            throw new InvalidOperationException(
                $"'{payload.PoseStance}' is not a body reference stance, so its pose skeleton cannot be resolved.");
        }

        var skeletonBytes = await _skeletons.ReadAsync(stance, cancellationToken)
            ?? throw new InvalidOperationException($"Pose skeleton for stance '{stance}' could not be read.");
        if (skeletonBytes.Length == 0)
        {
            throw new InvalidOperationException($"Pose skeleton for stance '{stance}' contains no image bytes.");
        }

        return new ReferenceConditionedImageInput
        {
            SemanticRole = $"pose reference ({stance} OpenPose skeleton)",
            FileName = _skeletons.FileNameFor(stance),
            Content = skeletonBytes
        };
    }

    /// <summary>
    /// A pose with no identity on a native-reference model: the skeleton IS the reference set, so this is a
    /// one-reference native render rather than a ControlNet graph. 2.1 reads a skeleton in a reference slot as pose
    /// guidance (measured 2026-09-23), which is why no ControlNet adapter is needed here.
    /// </summary>
    private async Task<byte[]> RenderNativePoseAsync(
        SceneAssetImage image,
        ResolvedImageModel model,
        SceneAssetGenerationJobPayload payload,
        string compiledPrompt,
        string? negativePrompt,
        CancellationToken cancellationToken)
    {
        var skeleton = await ReadPoseSkeletonAsync(payload, cancellationToken);
        _logger.LogInformation(
            "Scene asset pose render via NATIVE reference: ImageId={ImageId}, Model={Model}, Stance={Stance}, "
            + "Skeleton={Skeleton}, ControlNetStrength=not-applicable",
            image.Id, model.ModelIdentifier, payload.PoseStance, skeleton.FileName);

        return await _referenceClient.GenerateWithReferencesAsync(
            model,
            new ReferenceConditionedImageRequest
            {
                PositivePrompt = compiledPrompt,
                NegativePrompt = negativePrompt ?? string.Empty,
                Size = payload.ImageSize,
                Seed = null,
                References = [skeleton],
                CorrelationId = image.Id
            },
            cancellationToken);
    }

    /// <summary>
    /// A canonical angle rendered as a GENERATION from two references: the accepted body image (the build) and the
    /// committed angle skeleton (the turn). Measured 2026-09-23 — this is the shape all four canonical angles pass
    /// with, and it is a different request from the rotation edit, which is still available.
    /// </summary>
    private async Task<byte[]> RenderBodyAngleAsync(
        SceneAssetImage image,
        ResolvedImageModel model,
        SceneAssetGenerationJobPayload payload,
        string compiledPrompt,
        string? negativePrompt,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SceneImageReferenceBodyView>(payload.BodyAngleView, ignoreCase: false, out var view))
        {
            throw new InvalidOperationException(
                $"'{payload.BodyAngleView}' is not a canonical body view, so its angle skeleton cannot be resolved. "
                + $"The committed set is: {string.Join(", ", BodyAngleSkeletons.Available)}.");
        }

        // Resolved from the ONE angle library, so a view with no committed skeleton fails HERE — with the library's own
        // message naming the committed set — rather than after a strategy and a storage round trip. The front is the
        // base (generated from the body card) and any other angle is an extended view (an edit of an accepted view).
        var angleSkeletonFile = BodyAngleSkeletons.Require(view).FileName;

        if (string.IsNullOrWhiteSpace(payload.BodyAngleSourceImageId))
        {
            throw new InvalidOperationException(
                $"The {view} angle render names no accepted source body. An angle render is that body turned, so this "
                + "refuses rather than inventing a body.");
        }

        // HOW this model carries references decides whether the two inputs can travel at all. The measured route is the
        // model's OWN reference slots; a model that needs an applied IP-Adapter/PuLID mechanism has nowhere to put a
        // BODY image, and failing here is the honest answer — a fallback would render an unconditioned image that
        // looks exactly like a successful angle render.
        var strategy = await ReferenceStrategyResolver.ResolveIdentityAsync(
            _referenceStrategies, payload.ModelId, cancellationToken);
        if (!strategy.IsAvailable)
        {
            throw new InvalidOperationException(
                $"An angle render sends the accepted body and the angle skeleton as reference images, but this model "
                + $"cannot carry references: {strategy.Reason}");
        }

        if (!string.Equals(strategy.Strategy, ReferenceStrategyResolver.IdentityNativeMultiReference, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "An angle render sends the accepted body and the angle skeleton as reference images, but this model "
                + $"carries references through '{strategy.Strategy}' instead of its own reference slots, so a BODY "
                + "reference has nowhere to go. Select a model that takes reference images, or use the rotation edit.");
        }

        var source = await _repository.GetImageAsync(payload.BodyAngleSourceImageId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The accepted source body '{payload.BodyAngleSourceImageId}' of this angle render was not found, so "
                + "the render cannot be based on that body.");
        if (string.IsNullOrWhiteSpace(source.FileRelativePath))
        {
            throw new InvalidOperationException(
                $"The accepted source body '{source.Id}' has no stored file, so it cannot be a reference.");
        }

        byte[] sourceBytes;
        await using (var stored = await _storage.OpenReadAsync(source.FileRelativePath, cancellationToken))
        using (var buffer = new MemoryStream())
        {
            await stored.CopyToAsync(buffer, cancellationToken);
            sourceBytes = buffer.ToArray();
        }

        if (sourceBytes.Length == 0)
        {
            throw new InvalidOperationException($"The accepted source body '{source.Id}' contains no image bytes.");
        }

        var skeletonBytes = await _angleSkeletons.ReadAsync(view, cancellationToken);

        // ORDER IS MEASURED: accepted body first, skeleton second. It is the shape the four canonical-angle cases and
        // the identity cases were both proven with, so it is stated once, here, rather than left to each caller.
        List<ReferenceConditionedImageInput> references =
        [
            new ReferenceConditionedImageInput
            {
                SemanticRole = $"accepted body (the build the {view} render must keep)",
                FileName = $"body-source-{source.Id}.png",
                Content = sourceBytes
            },
            new ReferenceConditionedImageInput
            {
                SemanticRole = $"angle reference ({view} OpenPose skeleton)",
                FileName = angleSkeletonFile,
                Content = skeletonBytes
            }
        ];

        // The approved identity FACE is an OPTIONAL third reference, and whether it travels is the operator's switch.
        // Measured 2026-09-23 (case body-angle-34-left-front-plus-skeleton-plus-face): adding it changes the render by a
        // mean absolute pixel difference of 2.89/255, because the accepted body already carries the identity — so it is
        // never assumed, and asking for it is honoured rather than refused as redundant.
        if (!string.IsNullOrWhiteSpace(payload.IdentityFaceAssetId))
        {
            if (string.IsNullOrWhiteSpace(payload.IdentityPackId))
            {
                throw new InvalidOperationException(
                    "Identity conditioning names a face asset but no pack, so the reference cannot be verified. Enqueue "
                    + "the render again through the body panel's identity option.");
            }

            var face = await (_identityFaceResolver
                    ?? throw new InvalidOperationException(
                        "Identity conditioning requires the identity face reference resolver."))
                .ResolveExactFaceAsync(1, payload.IdentityPackId, payload.IdentityFaceAssetId, cancellationToken);

            byte[] faceBytes;
            await using (var faceStream = await _identityStorage.OpenReadAsync(face.FileRelativePath, cancellationToken))
            using (var faceBuffer = new MemoryStream())
            {
                await faceStream.CopyToAsync(faceBuffer, cancellationToken);
                faceBytes = faceBuffer.ToArray();
            }

            if (faceBytes.Length == 0)
            {
                throw new InvalidOperationException($"Identity face asset '{face.FaceAssetId}' contains no image bytes.");
            }

            references.Add(new ReferenceConditionedImageInput
            {
                SemanticRole = $"approved identity face ({face.FaceView?.ToString() ?? "unspecified view"})",
                FileName = $"identity-{face.FaceAssetId}.png",
                Content = faceBytes
            });
        }

        _logger.LogInformation(
            "Body angle render via NATIVE reference: ImageId={ImageId}, Model={Model}, View={View}, SourceImageId={SourceImageId}, "
            + "AngleSkeleton={Skeleton}, References={References}",
            image.Id, model.ModelIdentifier, view, source.Id, angleSkeletonFile, references.Count);
        return await _referenceClient.GenerateWithReferencesAsync(
            model,
            new ReferenceConditionedImageRequest
            {
                PositivePrompt = compiledPrompt,
                NegativePrompt = negativePrompt ?? string.Empty,
                Size = payload.ImageSize,
                Seed = null,
                References = references,
                CorrelationId = image.Id
            },
            cancellationToken);
    }

    /// <summary>
    /// Renders through the pose-conditioned client on the pinned local ComfyUI model. The resolver fails fast unless
    /// the model declares and qualifies the PoseControlNet capability — there is no silent text-only fallback, because
    /// falling back would produce an unconditioned image that looks exactly like a successful one.
    /// </summary>
    private async Task<byte[]> RenderPoseConditionedAsync(
        SceneAssetImage image,
        SceneAssetGenerationJobPayload payload,
        string compiledPrompt,
        string? negativePrompt,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<BodyReferenceStance>(payload.PoseStance, ignoreCase: false, out var stance))
        {
            throw new InvalidOperationException(
                $"'{payload.PoseStance}' is not a body reference stance, so its pose skeleton cannot be resolved. "
                + $"The verified set is: {string.Join(", ", BodyStanceSkeletons.Available)}.");
        }

        if (payload.PoseStrength is not { } strength || strength is <= 0 or > 1)
        {
            throw new InvalidOperationException(
                $"Pose conditioning for stance '{stance}' requires a strength in (0, 1], but was "
                + $"{payload.PoseStrength?.ToString() ?? "not set"}.");
        }

        // Resolved by the REQUESTED registered model id, exactly as the scene-image pose path does: the resolver
        // reads the capability configuration keyed by that id, not by the provider's checkpoint identifier.
        var poseModel = await _poseResolver.ResolveAsync(payload.ModelId, cancellationToken);
        var skeleton = await _skeletons.ReadAsync(stance, cancellationToken);

        _logger.LogInformation(
            "Scene asset pose-conditioned render: ImageId={ImageId}, Stance={Stance}, Skeleton={Skeleton}, "
            + "Checkpoint={Checkpoint}, ControlNet={ControlNet}, Strength={Strength}",
            image.Id, stance, _skeletons.FileNameFor(stance), poseModel.ModelIdentifier,
            poseModel.ControlNetAdapterRef, strength);

        return await _poseClient.GenerateAsync(
            poseModel,
            new PoseConditionedImageRequest
            {
                PositivePrompt = compiledPrompt,
                NegativePrompt = negativePrompt ?? string.Empty,
                Size = payload.ImageSize,
                Seed = null,
                PoseImageBytes = skeleton,
                Strength = strength,
                CorrelationId = image.Id
            },
            cancellationToken);
    }

    /// <summary>
    /// Renders through the identity-conditioned client. The pack and its face asset are RE-READ here rather than
    /// trusted from the queue, so a reference that was superseded, unapproved or deleted between queueing and
    /// rendering fails the render instead of silently producing a different person.
    /// </summary>
    private async Task<byte[]> RenderIdentityConditionedAsync(
        ResolvedImageModel model,
        SceneAssetImage image,
        SceneAssetGenerationJobPayload payload,
        string compiledPrompt,
        string? negativePrompt,
        SceneAssetType assetType,
        CancellationToken cancellationToken,
        bool poseIsNative = false)
    {
        if (string.IsNullOrWhiteSpace(payload.IdentityPackId))
        {
            throw new InvalidOperationException(
                "Identity conditioning names a face asset but no pack, so the reference cannot be verified. Enqueue "
                + "the render again through the body panel's identity option.");
        }

        // The pack, its approval, and the exact face are re-read here rather than trusted from the queue, so a
        // reference that was superseded, unapproved or deleted between queueing and rendering fails the render
        // instead of silently producing a different person. Shared with the scene render path: one
        // implementation of "which approved face does this pack contribute".
        var face = await (_identityFaceResolver
                ?? throw new InvalidOperationException(
                    "Identity conditioning requires the identity face reference resolver."))
            .ResolveExactFaceAsync(1, payload.IdentityPackId, payload.IdentityFaceAssetId ?? string.Empty, cancellationToken);

        byte[] referenceBytes;
        await using (var source = await _identityStorage.OpenReadAsync(face.FileRelativePath, cancellationToken))
        using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, cancellationToken);
            referenceBytes = buffer.ToArray();
        }

        if (referenceBytes.Length == 0)
        {
            throw new InvalidOperationException($"Identity face asset '{face.FaceAssetId}' contains no image bytes.");
        }

        // HOW this model carries identity is decided by the same resolver the panel asks, so an offered switch is never
        // one the render refuses. Two mechanisms exist and they are NOT interchangeable: a configured IP-Adapter/PuLID
        // graph conditions the sampler's model input, while a native-reference model takes the face as an image
        // alongside the prompt in one call. Falling back from one to the other would produce an unconditioned image
        // that looks exactly like a conditioned one, so an unavailable strategy fails here.
        var strategy = await ReferenceStrategyResolver.ResolveIdentityAsync(
            _referenceStrategies, payload.ModelId, cancellationToken);
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
            if (!string.IsNullOrWhiteSpace(payload.PoseStance) && !poseIsNative)
            {
                throw new InvalidOperationException(
                    "Pose conditioning cannot be combined with native-reference identity: this model carries the pose "
                    + "through a ControlNet graph while the identity travels as a reference image, so the skeleton would "
                    + "be silently dropped. Render the pose and the identity as separate requests, or select a model "
                    + "that carries both as references.");
            }

            // ONE reference today: the approved FACE, plus the pose skeleton when the model carries poses natively.
            // This list is the single place a further reference would be added — when a pack carries a canonical BODY
            // reference as well (CharacterImageIdentityPack.CanonicalFullBodyAssetId), a native-reference model can
            // take both in the same call: the face for identity, the body for proportions. Nothing else on this path
            // would change: the strategy, the pack/face validation and the refusal rules are about the MECHANISM, not
            // about how many references travel with it.
            List<ReferenceConditionedImageInput> references =
            [
                new ReferenceConditionedImageInput
                {
                    SemanticRole = $"approved identity face for the body reference ({face.FaceView?.ToString() ?? "unspecified view"})",
                    FileName = $"identity-{face.FaceAssetId}.png",
                    Content = referenceBytes
                }
            ];

            // Pose goes LAST (faces, then approved scene assets, then the pose skeleton) — a deterministic convention,
            // not a capability requirement: the host proof landed identity and pose together in any slot order.
            if (poseIsNative)
                references.Add(await ReadPoseSkeletonAsync(payload, cancellationToken));

            _logger.LogInformation(
                "Scene asset identity render via NATIVE reference: ImageId={ImageId}, Model={Model}, Pack={PackId} v{Version}, "
                + "Face={FaceId}, Angle={FaceView}, References={References}",
                image.Id, model.ModelIdentifier, face.PackId, face.PackVersion, face.FaceAssetId, face.FaceView, references.Count);

            return await _referenceClient.GenerateWithReferencesAsync(
                model,
                new ReferenceConditionedImageRequest
                {
                    PositivePrompt = compiledPrompt,
                    NegativePrompt = negativePrompt ?? string.Empty,
                    Size = payload.ImageSize,
                    Seed = null,
                    References = references,
                    CorrelationId = image.Id
                },
                cancellationToken);
        }

        // The model must declare and qualify an identity MECHANISM; the resolver fails fast otherwise, because a
        // text-only fallback would produce an unconditioned image indistinguishable from a conditioned one.
        var identityModel = await _modelResolutionService.ResolveIdentityImageModelByIdAsync(payload.ModelId, cancellationToken);

        // Pose conditioning COMPOSES with identity: the skeleton drives the pose (ControlNet) while the face reference
        // drives the identity (IP-Adapter/PuLID), and they touch different edges of the same graph. The pose model is
        // resolved separately and contributes only its ControlNet adapter.
        byte[]? skeleton = null;
        string? controlNetRef = null;
        if (!string.IsNullOrWhiteSpace(payload.PoseStance))
        {
            if (!Enum.TryParse<BodyReferenceStance>(payload.PoseStance, ignoreCase: false, out var combinedStance))
            {
                throw new InvalidOperationException(
                    $"'{payload.PoseStance}' is not a body reference stance, so its pose skeleton cannot be resolved.");
            }

            var poseModel = await _poseResolver.ResolveAsync(payload.ModelId, cancellationToken);
            skeleton = await _skeletons.ReadAsync(combinedStance, cancellationToken);
            controlNetRef = poseModel.ControlNetAdapterRef;
            _logger.LogInformation(
                "Identity render also pose-conditioned: Stance={Stance}, Skeleton={Skeleton}, ControlNet={ControlNet}, "
                + "Strength={Strength}",
                combinedStance, _skeletons.FileNameFor(combinedStance), controlNetRef, payload.PoseStrength);
        }

        _logger.LogInformation(
            "Scene asset identity-conditioned render: ImageId={ImageId}, AssetType={AssetType}, Pack={PackId} v{Version}, "
            + "Face={FaceId}, Mechanism={Mechanism}, Checkpoint={Checkpoint}, PoseConditioned={PoseConditioned}",
            image.Id, assetType, face.PackId, face.PackVersion, face.FaceAssetId, identityModel.Mechanism, identityModel.ModelIdentifier,
            skeleton is not null);

        return await _identityClient.GenerateAsync(
            identityModel,
            new IdentityControlledImageRequest
            {
                PositivePrompt = compiledPrompt,
                NegativePrompt = negativePrompt ?? string.Empty,
                Size = payload.ImageSize,
                Seed = null,
                ReferenceImageBytes = referenceBytes,
                PoseImageBytes = skeleton,
                ControlNetAdapterRef = controlNetRef,
                PoseStrength = payload.PoseStrength,
                CorrelationId = image.Id
            },
            cancellationToken);
    }

    private async Task CompleteWithBytesAsync(SceneAssetImage image, string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes);
        var stored = await _storage.SaveAsync(fileName, stream, cancellationToken);
        image.Status = SceneAssetStatus.Complete;
        image.FileRelativePath = stored.RelativePath;
        image.MediaType = stored.MediaType;
        image.Width = stored.Width;
        image.Height = stored.Height;
        image.ByteLength = stored.ByteLength;
        image.Sha256 = stored.Sha256;
        image.CompletedUtc = DateTime.UtcNow;
        image.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertImageAsync(image, cancellationToken);
    }
}
