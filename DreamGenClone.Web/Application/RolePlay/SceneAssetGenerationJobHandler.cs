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
    // B-135 D10: the negative prompt is DECLARED on the checkpoint's compiler profile. This path reads it from the
    // same place the scene render path does, so there is one source for the one string.
    private readonly IImageCompilerProfileResolver _compilerProfileResolver;
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

    /// <summary>
    /// Optional for the same reason as <see cref="_identityFaceResolver"/>: only a render that conditions on the
    /// character's build needs it, and such a render fails fast when it is absent rather than leaving the build to
    /// the model.
    /// </summary>
    private readonly IdentityBodyReferenceResolver? _identityBodyReferenceResolver;
    private readonly IPoseLibraryService? _poseLibrary;

    /// <summary>
    /// Optional, exactly as <see cref="_identityFaceResolver"/> is: only a render that SELECTS a character LoRA needs
    /// it, and such a render fails fast when it is absent rather than rendering the character without their identity.
    /// </summary>
    private readonly ISceneImageCharacterLoraResolver? _characterLoraResolver;

    /// <summary>
    /// Optional, exactly as <see cref="_characterLoraResolver"/> is: only a render that SELECTED a scene LoRA
    /// (unlock / act / anatomy / style) needs it, and such a render fails fast when it is absent rather than rendering
    /// without the LoRA the operator picked - which would look exactly like a render that applied it.
    /// </summary>
    private readonly ISceneLoraResolver? _sceneLoraResolver;
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
        IImageCompilerProfileResolver compilerProfileResolver,
        ICharacterImageIdentityRepository identityRepository,
        ICharacterImageAssetStorageService identityStorage,
        ILogger<SceneAssetGenerationJobHandler> logger,
        IdentityFaceReferenceResolver? identityFaceResolver = null,
        IdentityBodyReferenceResolver? identityBodyReferenceResolver = null,
        IPoseLibraryService? poseLibrary = null,
        ISceneImageCharacterLoraResolver? characterLoraResolver = null,
        ISceneLoraResolver? sceneLoraResolver = null)
    {
        _repository = repository;
        _storage = storage;
        _modelResolutionService = modelResolutionService;
        _compilerProfileResolver = compilerProfileResolver;
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
        _identityBodyReferenceResolver = identityBodyReferenceResolver;
        _poseLibrary = poseLibrary;
        _characterLoraResolver = characterLoraResolver;
        _sceneLoraResolver = sceneLoraResolver;
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

            // B-135 D10: the negative prompt is DECLARED on the checkpoint's compiler profile and read from there —
            // on this path too. The body compiler used to author its own copy of the Pony guard set and the
            // precompiled image carried it, which was a SECOND source of truth for one string, and it meant a Pony
            // asset prompt got the guard set only when it happened to be precompiled.
            var negativePrompt = (await _compilerProfileResolver.ResolveAsync(model, cancellationToken)).Negative;

            // The seed is decided HERE, once, for two reasons. A caller can PIN one (a catalog position declares a seed,
            // so a re-run reproduces the image) or leave it null to ask for a NEW result; either way the number below
            // is the one the sampler receives, so it can be RECORDED. Drawing it inside the workflow builder - which is
            // what happens when null is passed straight through - makes the value unknowable, and an image nobody can
            // reproduce is an image nobody can build on.
            var seed = payload.Seed ?? Random.Shared.Next(0, int.MaxValue);
            image.Seed = seed;

            // Whether this text is already model-ready is STATED by the image, never guessed from the text's shape.
            // An image that names its prompt compiler carries that compiler's family framing; recompiling it would
            // repeat the Pony quality string and push the prompt past its qualified length.
            var precompiled = !string.IsNullOrWhiteSpace(image.PromptCompilerId);
            string compiledPrompt;
            string? compilerId;
            string? compilerVersion;
            if (precompiled)
            {
                compiledPrompt = image.Prompt;
                compilerId = image.PromptCompilerId;
                compilerVersion = null;
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
            }

            // Character LoRAs ride on the resolved MODEL, which is the same channel the studio's render uses, so the
            // ComfyUI client injects one LoraLoader chain either way. Applied BEFORE the graph is built and before the
            // prompt is snapshotted, because the trigger tokens are part of the prompt that actually rendered. The
            // decision itself lives in CharacterLoraRenderApplication so this path and the studio's cannot drift.
            var loraApplication = await CharacterLoraRenderApplication.ApplyAsync(
                model,
                compiledPrompt,
                payload.CharacterLoras,
                _characterLoraResolver,
                cancellationToken);
            model = loraApplication.Model;
            compiledPrompt = loraApplication.Prompt;

            // Scene LoRAs (non-identity: unlock / act / anatomy / style), resolved from the catalog and filtered to
            // THIS model's family. Applied to the resolved model beside the character LoRAs, which is the same channel
            // the studio's render uses, so both paths build one chain in the one order the client owns (scene first,
            // identity last). A selection that names a file the catalog does not carry, or one catalogued for another
            // family, fails the render by name - the LoRA only binds to the family it was trained against.
            var sceneLoras = await ResolveSceneLorasAsync(model, payload.SceneLoras, cancellationToken);
            if (sceneLoras.Count > 0)
            {
                model = model with { SceneLoras = sceneLoras };
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
                seed,
                referenceApplicationsJson = payload.ReferenceApplicationsJson,
                // The FULL input set, recorded beside the prompt so an image's own row answers "what made this" by
                // itself. The pose, identity, body and LoRA ids travel on the payload and were being dropped here when
                // the metadata was rewritten for the completed image - which is exactly the provenance gap: a completed
                // image carried the prompt but no record of which pose, character or build it rendered. They are read
                // back by SceneAssetImageGenerationDetails for the review surface and the round-trip.
                posePresetId = payload.PosePresetId,
                poseSkeletonRelativePath = payload.PoseSkeletonRelativePath,
                poseStance = payload.PoseStance,
                poseStrength = payload.PoseStrength,
                identityPackId = payload.IdentityPackId,
                identityFaceAssetId = payload.IdentityFaceAssetId,
                bodyReferencePackId = payload.BodyReferencePackId,
                bodyReferenceAssetId = payload.BodyReferenceAssetId,
                bodyAngleView = payload.BodyAngleView,
                bodyAngleSourceImageId = payload.BodyAngleSourceImageId,
                characterLoras = payload.CharacterLoras,
                // Recorded even when empty, so "this image carried no scene LoRA" is a stated fact rather than an
                // absent field. An unlock or an act LoKr in the stack is part of what made the image, and the order
                // they were chained in is part of the recipe. Serialized as the resolved record itself so the stored
                // shape and the read-back type cannot drift.
                sceneLoras,
                // The lighting/expression presets that shaped this render, each with the clause it contributed. Both
                // halves are kept: the clause is what rendered (and what a re-apply replaces), the key is what the
                // picker reselects (B-140 D4).
                appliedPresets = payload.AppliedPresets
            }, JsonOptions);
            await _repository.UpsertImageAsync(image, cancellationToken);

            // HOW this model carries a pose is decided ONCE, by the same resolver that owns identity. A model with a
            // qualified OpenPose ControlNet graph keeps the dedicated graph; a native-reference model takes the
            // skeleton as one more reference image in the same call (measured 2026-09-23). A model that qualifies
            // neither fails with its reason rather than rendering an unconditioned image.
            ReferenceStrategyResolution? poseStrategy = null;
            var hasPose = !string.IsNullOrWhiteSpace(payload.PoseStance)
                || !string.IsNullOrWhiteSpace(payload.PosePresetId);
            if (hasPose)
                poseStrategy = await ResolvePoseStrategyAsync(payload.ModelId, cancellationToken);
            var poseIsNative = poseStrategy is not null
                && string.Equals(poseStrategy.Strategy, ReferenceStrategyResolver.IdentityNativeMultiReference, StringComparison.OrdinalIgnoreCase);

            // A library PRESET is only carried by the native-reference route. Everything else resolves a STANCE, so a
            // preset reaching those paths would be ignored - a render that looks successful and contains no pose. That
            // is refused here rather than dropped, and it is refused NOW because the dispatch below would otherwise
            // take the plain text-to-image branch on a payload whose stance is blank.
            if (!string.IsNullOrWhiteSpace(payload.PosePresetId) && !poseIsNative)
            {
                throw new InvalidOperationException(
                    $"Pose library preset '{payload.PosePresetId}' needs a model that carries the skeleton as a "
                    + $"REFERENCE image, but '{payload.ModelId}' carries a pose through "
                    + $"'{poseStrategy?.Strategy ?? "no mechanism"}'. Use a native-reference model "
                    + "(Qwen-Image-2.1), or send a stance instead of a library preset.");
            }

            // NO "preset cannot be combined with identity or body references" refusal. There was one, and it was WRONG
            // (removed 2026-09-27): RenderIdentityConditionedAsync adds the skeleton as one more native reference
            // beside the face and the body, and a host proof landed all three together on 2026-09-23. The guard
            // described an older dispatch that resolved the pose as a stance, and it made a proven combination
            // unreachable from the app.

            var bytes = !string.IsNullOrWhiteSpace(payload.BodyAngleView)
                ? await RenderBodyAngleAsync(image, model, payload, compiledPrompt, negativePrompt, seed, cancellationToken)
                // A pack reference decides this route: EITHER a face or a body reference is enough, because a view
                // from directly behind carries no face in frame and still conditions on the character's build.
                // Treating the face as the only trigger is what would leave those cells unconditioned.
                : string.IsNullOrWhiteSpace(payload.IdentityFaceAssetId)
                    && string.IsNullOrWhiteSpace(payload.BodyReferenceAssetId)
                    ? string.IsNullOrWhiteSpace(payload.PoseStance) && string.IsNullOrWhiteSpace(payload.PosePresetId)
                        ? await _imageClient.GenerateAsync(model, compiledPrompt, payload.ImageSize, negativePrompt, seed, cancellationToken)
                            ?? throw new InvalidOperationException("The image model returned no image bytes.")
                        : poseIsNative
                            ? await RenderNativePoseAsync(image, model, payload, compiledPrompt, negativePrompt, seed, cancellationToken)
                            : await RenderPoseConditionedAsync(image, payload, compiledPrompt, negativePrompt, seed, cancellationToken)
                    : await RenderIdentityConditionedAsync(
                        model, image, payload, compiledPrompt, negativePrompt,
                        asset.Type ?? throw new InvalidOperationException("Scene asset generation requires an explicit asset type."),
                        seed,
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

            // Tags are written once the image EXISTS to be found: a render that failed carries no tags, because a tag is
            // a claim about a picture and a failed row has no picture. Collected by the one builder that decides which
            // facts become tags, and ADDED rather than replaced so a tag an operator put on the row while it rendered
            // survives (B-140 D2/FR-6).
            await _repository.AddImageTagsAsync(
                image.Id,
                await BuildTagsAsync(payload, asset, sceneLoras, cancellationToken),
                cancellationToken);

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
        var hasPreset = !string.IsNullOrWhiteSpace(payload.PosePresetId);
        var hasStance = !string.IsNullOrWhiteSpace(payload.PoseStance);
        if (hasPreset && hasStance)
        {
            throw new InvalidOperationException(
                $"This render asks for two poses at once: stance '{payload.PoseStance}' and library preset "
                + $"'{payload.PosePresetId}'. A step sends one pose; send one of them.");
        }

        // The pose-library route: the preset's OWN artifact, read by its id so a stale path in the payload cannot make
        // the render read a file other than the preset it names.
        if (hasPreset)
        {
            var presetId = payload.PosePresetId!.Trim();
            var library = _poseLibrary ?? throw new InvalidOperationException(
                $"This render asks for pose library preset '{presetId}', but the pose-library service is not configured "
                + "for asset generation. Register IPoseLibraryService, or use a stance instead.");
            byte[]? presetBytes;
            try
            {
                presetBytes = await library.ReadSkeletonAsync(presetId, cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
            {
                throw new InvalidOperationException(
                    $"Pose library preset '{presetId}' could not be read, so its skeleton cannot condition this render: "
                    + exception.Message);
            }

            if (presetBytes is null || presetBytes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Pose library preset '{presetId}' has no skeleton bytes, so it cannot condition this render. "
                    + "Re-import or re-render that pose in the pose library.");
            }

            return new ReferenceConditionedImageInput
            {
                SemanticRole = $"pose reference ({presetId} library skeleton)",
                FileName = string.IsNullOrWhiteSpace(payload.PoseSkeletonRelativePath)
                    ? $"{presetId}.png"
                    : Path.GetFileName(payload.PoseSkeletonRelativePath),
                Content = presetBytes
            };
        }

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
        long seed,
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
                Seed = seed,
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
        long seed,
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
                Seed = seed,
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
        long seed,
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
                Seed = seed,
                PoseImageBytes = skeleton,
                Strength = strength,
                CorrelationId = image.Id
            },
            cancellationToken);
    }

    /// <summary>
    /// Renders through the reference-conditioned route. The pack and each reference are RE-READ here rather than
    /// trusted from the queue, so a reference that was superseded, unapproved or deleted between queueing and
    /// rendering fails the render instead of silently producing a different person or build.
    /// </summary>
    private async Task<byte[]> RenderIdentityConditionedAsync(
        ResolvedImageModel model,
        SceneAssetImage image,
        SceneAssetGenerationJobPayload payload,
        string compiledPrompt,
        string? negativePrompt,
        SceneAssetType assetType,
        long seed,
        CancellationToken cancellationToken,
        bool poseIsNative = false)
    {
        var hasFaceReference = !string.IsNullOrWhiteSpace(payload.IdentityFaceAssetId);
        var hasBodyReference = !string.IsNullOrWhiteSpace(payload.BodyReferenceAssetId);

        // HOW this model carries a reference is decided ONCE, before either reference is read, because the answer
        // decides whether a second reference is even expressible: a configured IP-Adapter/PuLID graph conditions the
        // sampler's model input through ONE slot, while a native-reference model takes images alongside the prompt in
        // one call. A mechanism that cannot carry both must refuse the render, never drop one silently.
        var strategy = await ReferenceStrategyResolver.ResolveIdentityAsync(
            _referenceStrategies, payload.ModelId, cancellationToken);
        if (!strategy.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Reference conditioning was requested, but this model cannot carry it: {strategy.Reason}");
        }

        var carriesReferencesNatively = string.Equals(
            strategy.Strategy, ReferenceStrategyResolver.IdentityNativeMultiReference, StringComparison.OrdinalIgnoreCase);

        // Shared with the scene render path: one implementation of "which approved face does this pack contribute".
        ResolvedIdentityFaceReference? face = null;
        byte[]? referenceBytes = null;
        if (hasFaceReference)
        {
            if (string.IsNullOrWhiteSpace(payload.IdentityPackId))
            {
                throw new InvalidOperationException(
                    "Identity conditioning names a face asset but no pack, so the reference cannot be verified. Enqueue "
                    + "the render again through the body panel's identity option.");
            }

            face = await (_identityFaceResolver
                    ?? throw new InvalidOperationException(
                        "Identity conditioning requires the identity face reference resolver."))
                .ResolveExactFaceAsync(1, payload.IdentityPackId, payload.IdentityFaceAssetId!, cancellationToken);
            referenceBytes = await ReadIdentityReferenceBytesAsync(
                face.FileRelativePath, $"identity face asset '{face.FaceAssetId}'", cancellationToken);
        }

        ResolvedIdentityBodyReference? bodyReference = null;
        byte[]? bodyReferenceBytes = null;
        if (hasBodyReference)
        {
            if (!carriesReferencesNatively)
            {
                throw new InvalidOperationException(
                    $"A body reference was requested, but model '{payload.ModelId}' carries references through "
                    + $"'{strategy.Strategy}', which has a single reference slot, so the body would be dropped. This "
                    + "render was NOT submitted: select a model that carries references natively, or render the cell "
                    + "without a body reference.");
            }

            bodyReference = await (_identityBodyReferenceResolver
                    ?? throw new InvalidOperationException(
                        "Body reference conditioning requires the identity body reference resolver."))
                .ResolveExactBodyAsync(
                    2, payload.BodyReferencePackId ?? string.Empty, payload.BodyReferenceAssetId!, cancellationToken);
            bodyReferenceBytes = await ReadIdentityReferenceBytesAsync(
                bodyReference.FileRelativePath,
                $"identity body asset '{bodyReference.BodyAssetId}' "
                + $"({bodyReference.BodyView}/{bodyReference.BodyState})",
                cancellationToken);
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

            // The approved FACE (identity) and the approved BODY (build), plus the pose skeleton when the model
            // carries poses natively. Order is face, body, skeleton: the face anchors the person, the body the build,
            // and the skeleton goes LAST. Slot order is a placement convention, not a capability gate — the host
            // proof landed identity, build and pose together in any order (measured 2026-09-23).
            List<ReferenceConditionedImageInput> references = [];
            if (face is not null)
            {
                references.Add(new ReferenceConditionedImageInput
                {
                    SemanticRole = $"approved identity face for the body reference ({face.FaceView?.ToString() ?? "unspecified view"})",
                    FileName = $"identity-{face.FaceAssetId}.png",
                    Content = referenceBytes!
                });
            }

            if (bodyReference is not null)
            {
                references.Add(new ReferenceConditionedImageInput
                {
                    SemanticRole = $"approved body build reference ({bodyReference.BodyView}/{bodyReference.BodyState})",
                    FileName = $"body-{bodyReference.BodyAssetId}.png",
                    Content = bodyReferenceBytes!
                });
            }

            if (poseIsNative)
                references.Add(await ReadPoseSkeletonAsync(payload, cancellationToken));

            _logger.LogInformation(
                "Scene asset reference-conditioned render via NATIVE reference: ImageId={ImageId}, Model={Model}, "
                + "Pack={PackId} v{Version}, Face={FaceId}, Body={BodyId}, References={References}",
                image.Id, model.ModelIdentifier,
                face?.PackId ?? bodyReference!.PackId,
                face?.PackVersion ?? bodyReference!.PackVersion,
                face?.FaceAssetId, bodyReference?.BodyAssetId, references.Count);

            return await _referenceClient.GenerateWithReferencesAsync(
                model,
                new ReferenceConditionedImageRequest
                {
                    PositivePrompt = compiledPrompt,
                    NegativePrompt = negativePrompt ?? string.Empty,
                    Size = payload.ImageSize,
                    Seed = seed,
                    References = references,
                    CorrelationId = image.Id
                },
                cancellationToken);
        }

        // A graph mechanism conditions through ONE slot: the approved face. A body-only render therefore cannot be
        // carried here — and the check above already refused a second reference on this route — so this states the
        // remaining case rather than letting a face-less render reach a mechanism with nothing to condition on.
        if (face is null || referenceBytes is null)
        {
            throw new InvalidOperationException(
                $"Model '{payload.ModelId}' carries references through '{strategy.Strategy}', which conditions on the "
                + "approved FACE. This render named no face reference, so that mechanism has nothing to condition on and "
                + "the render was NOT submitted: select a model that carries references natively.");
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
                Seed = seed,
                ReferenceImageBytes = referenceBytes,
                PoseImageBytes = skeleton,
                ControlNetAdapterRef = controlNetRef,
                PoseStrength = payload.PoseStrength,
                CorrelationId = image.Id
            },
            cancellationToken);
    }

    /// <summary>
    /// Reads one approved pack reference's stored bytes. The label is what the failure names, so a missing file says
    /// which reference it was rather than "an image".
    /// </summary>
    /// <summary>
    /// The tags a completed image is stored with: what the caller DECLARED (character, position, wardrobe, location,
    /// sex position — names only the caller has) merged with what the render DERIVED (pose metadata, scene LoRAs,
    /// applied presets, model), through the one builder that owns that decision.
    ///
    /// <para>
    /// The scene LoRAs are the list the render ALREADY resolved and chained, passed in rather than resolved again: a
    /// second resolution could fail (a catalog row disabled in between) after the image was already saved, which would
    /// turn a completed render into a failed job over its TAGS. The pose preset is re-read, which cannot fail the same
    /// way: a preset that has been deleted reads as null and simply contributes no pose tags.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<string>> BuildTagsAsync(
        SceneAssetGenerationJobPayload payload,
        SceneAsset asset,
        IReadOnlyList<ResolvedSceneLora> sceneLoras,
        CancellationToken cancellationToken)
    {
        PosePreset? pose = null;
        string? poseLibraryName = null;
        if (!string.IsNullOrWhiteSpace(payload.PosePresetId) && _poseLibrary is { } library)
        {
            pose = await library.GetPresetAsync(payload.PosePresetId!.Trim(), cancellationToken);
            if (pose is not null)
            {
                poseLibraryName = (await library.ListLibrariesAsync(cancellationToken))
                    .FirstOrDefault(candidate => string.Equals(candidate.Id, pose.LibraryId, StringComparison.Ordinal))
                    ?.Name;
            }
        }

        return RenderTagBuilder.Build(new RenderTagRequest
        {
            DeclaredTags = payload.DeclaredTags,
            AssetName = asset.Name,
            AssetType = asset.Type,
            ModelId = payload.ModelId,
            Pose = pose,
            PoseLibraryName = poseLibraryName,
            PoseStance = payload.PoseStance,
            SceneLoras = sceneLoras,
            AppliedPresets = payload.AppliedPresets
        });
    }

    private async Task<byte[]> ReadIdentityReferenceBytesAsync(
        string fileRelativePath, string label, CancellationToken cancellationToken)
    {
        byte[] bytes;
        await using (var source = await _identityStorage.OpenReadAsync(fileRelativePath, cancellationToken))
        using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (bytes.Length == 0)
        {
            throw new InvalidOperationException($"The {label} contains no image bytes, so it cannot be sent as a reference.");
        }

        return bytes;
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

    /// <summary>
    /// The NON-IDENTITY scene LoRAs this render applies (unlock / act / anatomy / style), resolved from the catalog
    /// and checked against the render model's family.
    ///
    /// <para>
    /// The validation itself lives in <see cref="SceneLoraResolver"/>, whose raw-selection overload is the ONE owner of
    /// what counts as a valid selection - this path and the studio's both reach it, so they cannot drift. What is owned
    /// HERE is the fail-fast on an unregistered resolver, exactly as for the character-LoRA resolver: a render that
    /// selected none never asks for it, and a render that DID select one must not proceed without it, because rendering
    /// without the selected LoRA would look exactly like a render that applied it.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<ResolvedSceneLora>> ResolveSceneLorasAsync(
        ResolvedImageModel model,
        IReadOnlyList<Models.SceneImageLoraSelection>? selections,
        CancellationToken cancellationToken)
    {
        if (selections is not { Count: > 0 })
        {
            return [];
        }

        var resolver = _sceneLoraResolver
            ?? throw new InvalidOperationException(
                "This render selects scene LoRA(s), but the scene-LoRA resolver is not available, so the selected "
                + "LoRAs cannot be applied. Rendering without them would produce a different image from the one "
                + "requested.");

        return await resolver.ResolveAsync(model, selections, cancellationToken);
    }
}
