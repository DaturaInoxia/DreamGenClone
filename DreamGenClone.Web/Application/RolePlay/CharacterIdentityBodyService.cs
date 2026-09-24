using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The body target's one-at-a-time acquisition (B-122 Phase 0).
///
/// The shape of the target: the <b>base</b> is the canonical front in a state (clothed, then unclothed), and every
/// other view is a same-image edit of an accepted view — the three-quarters of the base, the profiles of their own
/// three-quarter — exactly the chain the face angles use. Nothing here invents a body: the prompt is resolved from
/// the one template store with the body card line pasted in, the edit runs through the one shared edit primitive,
/// and the image lands in the same build container as every other artifact.
/// </summary>
public sealed class CharacterIdentityBodyService : ICharacterIdentityBodyService
{    private readonly ICharacterIdentityBuildRepository _repository;
    private readonly ICharacterIdentityBuildService _builds;
    private readonly ICharacterBodyCardRepository _bodyCards;
    private readonly IImageWorkflowTemplateService _templates;
    private readonly ISceneAssetService _assets;
    private readonly IReferenceImageQualityAnalyzer _quality;
    private readonly IBodyReferenceBriefFactory _briefs;
    private readonly IModelResolutionService _models;
    private readonly IPoseImageModelResolver _poses;
    private readonly IReferenceStrategyResolver _referenceStrategies;
    private readonly ILogger<CharacterIdentityBodyService> _logger;

    public CharacterIdentityBodyService(
        ICharacterIdentityBuildRepository repository,
        ICharacterIdentityBuildService builds,
        ICharacterBodyCardRepository bodyCards,
        IImageWorkflowTemplateService templates,
        ISceneAssetService assets,
        IReferenceImageQualityAnalyzer quality,
        IBodyReferenceBriefFactory briefs,
        IModelResolutionService models,
        IPoseImageModelResolver poses,
        IReferenceStrategyResolver referenceStrategies,
        ILogger<CharacterIdentityBodyService> logger)
    {
        _repository = repository;
        _builds = builds;
        _bodyCards = bodyCards;
        _templates = templates;
        _assets = assets;
        _quality = quality;
        _briefs = briefs;
        _models = models;
        _poses = poses;
        _referenceStrategies = referenceStrategies;
        _logger = logger;
    }

    public async Task<SceneAsset> EnsureContainerAsync(string buildId, CancellationToken cancellationToken = default)
    {
        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(build.FrontContainerAssetId))
        {
            return await _assets.GetAssetAsync(build.FrontContainerAssetId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The body container asset '{build.FrontContainerAssetId}' of build '{build.Id}' was not found.");
        }

        var container = await _assets.CreateAssetAsync("Body", SceneAssetType.CharacterBody, cancellationToken: cancellationToken);
        await _builds.SetFrontContainerAsync(build.Id, container.Id, cancellationToken);
        return container;
    }

    public async Task<CharacterBodyViewSettings> ResolveViewSettingsAsync(
        string characterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new InvalidOperationException(
                "A character template id is required to resolve its body view settings.");
        }

        var settings = await _templates.ResolveSettingsAsync(characterId.Trim(), cancellationToken);
        return new CharacterBodyViewSettings(
            settings.BodyModelId ?? string.Empty, settings.BodyImageSize ?? string.Empty);
    }

    public async Task SaveViewSettingsAsync(
        string characterId, string modelId, string imageSize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new InvalidOperationException(
                "A character template id is required to save its body view settings.");
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException(
                "A body view model is required: no model is assumed for the body set.");
        }

        var size = NormalizeImageSize(imageSize);
        var resolved = await _templates.ResolveSettingsAsync(characterId.Trim(), cancellationToken);

        // A CHARACTER row carrying every resolved value with the body pair replaced: resolution is
        // character → global, so writing a partial row would blank this character's other settings.
        var settings = new ReferenceWorkflowSettings
        {
            CharacterProfileId = characterId.Trim(),
            EditorModelId = resolved.EditorModelId,
            FrontModelId = resolved.FrontModelId,
            BodyModelId = modelId.Trim(),
            BodyImageSize = size,
            UpscalerModelName = resolved.UpscalerModelName,
            EnhanceTargetLongEdge = resolved.EnhanceTargetLongEdge,
            EyeGateMaxAbsIrisDyPercent = resolved.EyeGateMaxAbsIrisDyPercent,
            AngleYawMinAbsPercent = resolved.AngleYawMinAbsPercent,
            QualityGateMinSharpness = resolved.QualityGateMinSharpness,
            CropHeadroomPercent = resolved.CropHeadroomPercent,
            CropTargetAspect = resolved.CropTargetAspect,
            DeriveByMirrorThreeQuarterRight = resolved.DeriveByMirrorThreeQuarterRight,
            DeriveByMirrorProfileRight = resolved.DeriveByMirrorProfileRight,
            DeriveByMirrorThreeQuarterLeft = resolved.DeriveByMirrorThreeQuarterLeft,
            DeriveByMirrorProfileLeft = resolved.DeriveByMirrorProfileLeft,
            EyeToolPythonPath = resolved.EyeToolPythonPath
        };
        await _templates.SaveSettingsAsync(settings, cancellationToken);

        _logger.LogInformation(
            "Body view settings saved: CharacterId={CharacterId}, ModelId={ModelId}, ImageSize={ImageSize}",
            characterId.Trim(), settings.BodyModelId, settings.BodyImageSize);
    }

    /// <summary>Accepts "1024x1536" (either case) and refuses anything that is not a width-by-height pair.</summary>
    private static string NormalizeImageSize(string? imageSize)
    {
        var trimmed = (imageSize ?? string.Empty).Trim();
        var parts = trimmed.Split('x', 'X');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var width) || width < 1
            || !int.TryParse(parts[1], out var height) || height < 1)
        {
            throw new InvalidOperationException(
                $"'{trimmed}' is not an image size. A body view size is a width-by-height pair such as 1024x1536.");
        }

        return $"{width}x{height}";
    }

    public Task<IReadOnlyList<CharacterIdentityBodyView>> ListViewsAsync(
        string buildId, CancellationToken cancellationToken = default)
        => _repository.ListBodyViewsAsync(buildId, cancellationToken);

    public async Task<CharacterIdentityBodyView?> GetViewAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        var views = await _repository.ListBodyViewsAsync(buildId, cancellationToken);
        return views.FirstOrDefault(view => Matches(view, key));
    }

    public Task<CharacterBodyCard?> GetBodyCardAsync(string characterId, CancellationToken cancellationToken = default)
        => _bodyCards.GetAsync(characterId, cancellationToken);

    public async Task<CharacterBodyCard> SaveBodyCardAsync(
        CharacterBodyCard card, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(card.CharacterTemplateId))
        {
            throw new InvalidOperationException("A body card must name the character it belongs to.");
        }

        var saved = await _bodyCards.SaveAsync(card, expectedVersion, cancellationToken);
        _logger.LogInformation(
            "Body card saved: CharacterId={CharacterId}, Version={Version}, Ready={Ready}",
            saved.CharacterTemplateId,
            saved.Version,
            saved.IsReady);
        return saved;
    }

    public async Task<string> ResolvePromptAsync(
        string buildId, CharacterIdentityBodyViewKey key, string modelId, string characterName,
        string? promptOverride = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();

        if (!string.IsNullOrWhiteSpace(promptOverride))
        {
            return promptOverride.Trim();
        }

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);

        if (!IsBaseKey(key) && !key.IsCanonical)
        {
            throw new InvalidOperationException(
                $"The {Describe(key)} is an extended view, so it has no generation prompt: it is produced as an edit "
                + "of its accepted source, whose instruction is the stored rotation instruction. Use "
                + "ResolveEditInstructionAsync / the view's edit action for it.");
        }

        // A canonical ANGLE is rendered from the accepted body, so its prompt is a real generation prompt — the
        // compiled body prompt plus the angle's camera clause — and the operator can read and edit it exactly like the
        // base's. Resolving the text needs no accepted source: it is the RENDER that requires one.
        var compiled = await CompileBaseAsync(
            build, key, modelId, characterName, useIdentity: false, identityFaceAssetId: null, cancellationToken);
        return compiled.Compiled.Positive;
    }

    /// <inheritdoc />
    public async Task<string> ResolveEditInstructionAsync(
        string buildId, CharacterIdentityBodyViewKey key, string characterName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var card = await RequireCompleteBodyCardAsync(build.CharacterTemplateId, cancellationToken);
        var angle = await _templates.ResolveAsync(
            CharacterBodyViewPrompts.KeyFor(key), build.CharacterTemplateId, cancellationToken);

        return Fill(angle.Body, characterName, card.ToPromptLine());
    }

    /// <summary>
    /// Compiles a view's prompt for the resolved model's family, and returns the frozen brief alongside it so
    /// the request's conditionings (identity this round, pose by the caller) travel from the SAME resolution that
    /// produced the prompt.
    ///
    /// For a canonical ANGLE the compiled body prompt is followed by that angle's camera clause — the clause says
    /// where the camera is, while the accepted body supplies the build and the committed angle skeleton supplies the
    /// pose. Both halves are prompt DATA resolved from the one template store; the caller validates the COMBINED text,
    /// because two halves that each read as a whole body can still describe neither.
    /// </summary>
    private async Task<(CompiledBodyPrompt Compiled, BodyReferenceBrief Brief)> CompileBaseAsync(
        CharacterIdentityBuild build, CharacterIdentityBodyViewKey key, string modelId, string characterName,
        bool useIdentity, string? identityFaceAssetId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException(
                $"An image model is required to compile the {Describe(key)} prompt: the prompt dialect is a property "
                + "of the model, so the prompt cannot be built before the model is chosen. Set the body view model "
                + "in Body view settings.");
        }

        var model = await _models.ResolveImageModelByIdAsync(modelId.Trim(), cancellationToken);
        var card = await RequireCompleteBodyCardAsync(build.CharacterTemplateId, cancellationToken);
        var brief = await _briefs.CreateAsync(
            build.CharacterTemplateId, card, key.State, StanceFor(key), useIdentity, identityFaceAssetId,
            cancellationToken);
        var compiled = BodyReferencePromptCompiler.Compile(brief, model, ForbiddenTokens(characterName));

        if (key.IsCanonical && key.View is { } view && view != SceneImageReferenceBodyView.Front)
        {
            var clause = await ResolveRenderClauseAsync(build, key, characterName, card, cancellationToken);
            compiled = compiled with { Positive = $"{compiled.Positive} {clause}".Trim() };
        }

        return (compiled, brief);
    }

    /// <inheritdoc />
    public async Task<BodyIdentityAvailability> ResolveIdentityAvailabilityAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var build = await RequireBodyBuildAsync(buildId, cancellationToken);

        // Identity is offered when the model can carry it by ANY qualified mechanism, not only by a configured
        // IP-Adapter/PuLID graph. Qwen-Image-2.1 has no IdentityMechanism at all — it declares
        // NativeMultiReference and takes the approved face as a reference IMAGE — and the old check, which read
        // IdentityMechanism alone, hid the option for it while the model could do the job.
        //
        // ONE decision, asked of the same resolver the render path asks, so what the operator is offered and what the
        // render will accept are the same fact rather than two derivations of it.
        var settings = await ResolveViewSettingsAsync(build.CharacterTemplateId, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.ModelId))
        {
            return BodyIdentityAvailability.Unavailable(
                "no body view model is set, and identity needs a model that can carry it. Choose one in Body view "
                + "settings above.");
        }

        var strategy = await ReferenceStrategyResolver.ResolveIdentityAsync(
            _referenceStrategies, settings.ModelId, cancellationToken);
        if (!strategy.IsAvailable)
        {
            return BodyIdentityAvailability.Unavailable(
                $"the body view model cannot carry identity: {strategy.Reason}");
        }

        if (string.Equals(
                strategy.Strategy,
                ReferenceStrategyResolver.IdentityReferenceConditioning,
                StringComparison.OrdinalIgnoreCase))
        {
            // A configured MECHANISM still has to resolve end to end — mechanism, strength and adapter reference — and
            // this is the same resolver the render path calls. A native-reference model has no such mechanism, so
            // asking it there would refuse a model that can in fact do the job.
            try
            {
                var identityModel = await _models.ResolveIdentityImageModelByIdAsync(settings.ModelId, cancellationToken);
                _logger.LogInformation(
                    "Body identity conditioning is available: ModelId={ModelId}, Strategy={Strategy}, "
                    + "Mechanism={Mechanism}, Strength={Strength}",
                    settings.ModelId, strategy.Strategy, identityModel.Mechanism, identityModel.IdentityStrength);
            }
            catch (ModelResolutionException error)
            {
                return BodyIdentityAvailability.Unavailable(
                    $"the body view model cannot carry identity: {error.Message}");
            }
        }
        else
        {
            _logger.LogInformation(
                "Body identity conditioning is available: ModelId={ModelId}, Strategy={Strategy}",
                settings.ModelId, strategy.Strategy);
        }

        var availability = await _briefs.ResolveIdentityAvailabilityAsync(build.CharacterTemplateId, cancellationToken);
        return availability.IsAvailable
            ? availability with { Strategy = strategy.Strategy }
            : availability;
    }

    /// <inheritdoc />
    public async Task<BodyPoseAvailability> ResolvePoseAvailabilityAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var settings = await ResolveViewSettingsAsync(build.CharacterTemplateId, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.ModelId))
        {
            return BodyPoseAvailability.Unavailable(
                "no body view model is set, and pose conditioning needs a model that can carry a pose. Choose one in "
                + "Body view settings above.");
        }

        // HOW this model carries a pose, through the ONE decision shared with the render path (a qualified OpenPose
        // ControlNet graph first, then the model's own reference slots). A model that carries references natively
        // takes the skeleton as an image in the same call — measured 2026-09-23 — so asking only about ControlNet
        // would hide a pose the render can in fact perform, and the panel would then drop the pose silently.
        var strategy = await ReferenceStrategyResolver.ResolvePoseAsync(
            _referenceStrategies, settings.ModelId, cancellationToken);
        if (!strategy.IsAvailable)
        {
            return BodyPoseAvailability.Unavailable(
                $"the body view model cannot carry a pose: {strategy.Reason}");
        }

        if (string.Equals(
                strategy.Strategy,
                ReferenceStrategyResolver.IdentityNativeMultiReference,
                StringComparison.OrdinalIgnoreCase))
        {
            // The reference-image route has no ControlNet adapter and no strength: the OpenPose skeleton itself is the
            // conditioning input, so there is no configured value to open the panel on.
            _logger.LogInformation(
                "Body pose conditioning is available: ModelId={ModelId}, Strategy={Strategy}, "
                + "ControlNet=none (skeleton travels as a reference image)",
                settings.ModelId, strategy.Strategy);
            return BodyPoseAvailability.Available(defaultStrength: null, strategy.Strategy);
        }

        // A configured ControlNet still has to resolve end to end — adapter reference, default strength, its own
        // family rules — and this is the same resolver the render path calls.
        try
        {
            var poseModel = await _poses.ResolveAsync(settings.ModelId, cancellationToken);
            _logger.LogInformation(
                "Body pose conditioning is available: ModelId={ModelId}, Strategy={Strategy}, Checkpoint={Checkpoint}, "
                + "ControlNet={ControlNet}, Strength={Strength}",
                settings.ModelId, strategy.Strategy, poseModel.ModelIdentifier, poseModel.ControlNetAdapterRef,
                poseModel.DefaultStrength);

            // The model's CONFIGURED strength travels with the answer: it is the value proven for that model, and on the
            // FLUX path the strength is what decides whether the control image's strokes imprint on the render.
            return BodyPoseAvailability.Available(poseModel.DefaultStrength, strategy.Strategy);
        }
        catch (ModelResolutionException error)
        {
            return BodyPoseAvailability.Unavailable(
                $"the body view model cannot carry a pose skeleton: {error.Message}");
        }
    }

    /// <summary>
    /// The stance of a canonical body view. Every slot in the acquisition set is the SAME standing, feet-on-the-
    /// ground reference — that is what makes ten views comparable to each other — so this is a statement of what the
    /// set is, not a default: the pose variants on <see cref="BodyReferenceStance"/> belong to a request that asks
    /// for one, and no slot does.
    /// </summary>
    private static BodyReferenceStance StanceFor(CharacterIdentityBodyViewKey key)
    {
        if (key.IsCanonical)
        {
            return BodyReferenceStance.Standing;
        }

        throw new InvalidOperationException(
            $"The {Describe(key)} is an extended view, which is produced as an edit of an accepted view, so it has no "
            + "generation stance.");
    }

    /// <summary>
    /// The tokens a compiled base prompt must not contain. A name (and a relation) cannot be rendered, so it is
    /// handed to the compiler's validator rather than merely left out — §2.3 rule 2 is then enforced, not trusted.
    /// </summary>
    private static IReadOnlyList<string> ForbiddenTokens(string? characterName)
        => string.IsNullOrWhiteSpace(characterName) ? [] : [characterName.Trim()];

    /// <summary>The front in either state: the only views produced without a source.</summary>
    private static bool IsBaseKey(CharacterIdentityBodyViewKey key)
        => key.IsCanonical && key.View == SceneImageReferenceBodyView.Front;

    public async Task<CharacterIdentityBodyView> GenerateAsync(
        string buildId, CharacterIdentityBodyViewKey key, string modelId, string imageSize, string characterName,
        string? promptOverride = null, SceneAssetPoseConditioning? pose = null, bool useIdentity = false,
        string? identityFaceAssetId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException($"An image model is required to generate the {Describe(key)}.");
        }

        if (string.IsNullOrWhiteSpace(imageSize))
        {
            throw new InvalidOperationException($"An image size is required to generate the {Describe(key)}.");
        }

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var card = await RequireCompleteBodyCardAsync(build.CharacterTemplateId, cancellationToken);

        // What this request IS, decided ONCE here. The front is the base: generated from the body card, from nothing
        // else. A canonical ANGLE is generated from the accepted body of its own source — that same body, turned —
        // because a render that is not based on the accepted body invents a different build (measured 2026-09-23).
        // An EXTENDED view is neither: it is a rotation of an accepted view, and it says so rather than quietly
        // producing a new body wearing a rotation label.
        var isBase = IsBaseKey(key);
        if (!isBase && !key.IsCanonical)
        {
            throw new InvalidOperationException(
                $"The {Describe(key)} is an extended view, which is produced as an edit of an accepted view, so it is "
                + "not generated from scratch. Use 'Edit from accepted source' for it.");
        }

        var angleView = isBase ? (SceneImageReferenceBodyView?)null : key.View;
        if (angleView is { } requestedAngle && !BodyAngleSkeletons.Has(requestedAngle))
        {
            throw new InvalidOperationException(
                $"The {Describe(key)} has no committed angle skeleton, so it cannot be rendered from the accepted "
                + $"body. The committed angles are: {string.Join(", ", BodyAngleSkeletons.Available)}.");
        }

        // A STANCE pose belongs to a BASE render. An angle view's pose IS its committed angle skeleton — the one the
        // row shows — so a stance here would add a second, contradictory skeleton. Refused rather than dropped, because
        // a silently ignored conditioning is indistinguishable from one that was applied.
        if (!isBase && pose is not null)
        {
            throw new InvalidOperationException(
                $"The {Describe(key)} takes no stance pose: its pose IS the committed angle skeleton for that view, "
                + "which the row shows. Render the base with the stance, or render this angle as it is.");
        }

        // The BACK view shows NO face by design, so the approved face reference is refused for it rather than sent: the
        // model would be shown a face and asked not to show it, which fights the only thing that defines this view, and
        // the identity travels in the accepted body reference anyway (measured 2026-09-23: on a full-body angle render
        // the face reference changes the image by 2.89/255, i.e. it is redundant — so refusing it costs nothing).
        if (angleView == SceneImageReferenceBodyView.Back && useIdentity)
        {
            throw new InvalidOperationException(
                $"The {Describe(key)} shows no face, so it takes no identity face reference: the accepted body IS the "
                + "identity here, and a face reference would ask the model to show the face this view exists to hide.");
        }

        // ONE availability decision, consulted by both the panel (to offer the switch) and here (to refuse before
        // queueing). A second opinion in this method could disagree with what the UI showed the operator.
        if (useIdentity)
        {
            var availability = await ResolveIdentityAvailabilityAsync(build.Id, cancellationToken);
            if (!availability.IsAvailable)
            {
                throw new InvalidOperationException(
                    $"Identity conditioning was requested, but {availability.Reason}");
            }
        }

        // The accepted BASE body an angle render is based on. Resolved BEFORE anything is queued: an angle is that
        // same body turned, so a base that has not been accepted is a refusal, never a fresh attempt at one.
        var angleSource = angleView is null
            ? null
            : await RequireAcceptedBaseAsync(build.Id, key.State, cancellationToken);

        var compilation = await CompileBaseAsync(
            build, key, modelId, characterName, useIdentity, identityFaceAssetId, cancellationToken);
        var compiled = compilation.Compiled;

        // An angle render's prompt is the compiled body prompt plus that angle's camera clause (the compile step does
        // the appending).
        if (angleView is { } renderAngle)
        {
            _logger.LogInformation(
                "Body angle render compiled: BuildId={BuildId}, View={View}, AngleSkeleton={Skeleton}, SourceImageId={SourceImageId}",
                build.Id, renderAngle, BodyAngleSkeletons.Require(renderAngle).FileName, angleSource!.Id);
        }

        var prompt = string.IsNullOrWhiteSpace(promptOverride) ? compiled.Positive : promptOverride.Trim();

        // The structural gate applies to what is SENT, not to what was compiled: an operator's edited text is a prompt
        // too, and validating only the compiled form meant an over-length or name-bearing override travelled to a model
        // unchecked — which is how a 891-character angle prompt survived in the row while every render refused.
        BodyPromptStructureValidator.RequireValid(prompt, compiled.Family, ForbiddenTokens(characterName));

        var container = await EnsureContainerAsync(build.Id, cancellationToken);
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);

        // The brief resolved the identity reference (or none), so this is the SAME decision the prompt was compiled
        // from — never a second lookup that could disagree with it.
        var identity = compilation.Brief.RequiresIdentity
            ? new SceneAssetIdentityConditioning(
                compilation.Brief.IdentityPackId
                    ?? throw new InvalidOperationException("The brief requests identity but names no pack."),
                compilation.Brief.IdentityFaceAssetId
                    ?? throw new InvalidOperationException("The brief requests identity but names no face asset."))
            : null;

        var image = await _assets.AddGeneratedImageAsync(
            container.Id,
            prompt,
            modelId.Trim(),
            imageSize.Trim(),
            cancellationToken,
            candidateBatchId: key.BatchIdFor(build.Id),
            options: new SceneAssetImageGenerationOptions
            {
                // The compiled negative travels WITH the prompt it belongs to. The render path cannot re-derive it:
                // on this path the image is the only record of which compiler authored the text.
                NegativePrompt = compiled.Negative,
                // Stated, so the render path never compiles this text a second time (which would repeat the Pony
                // quality string and push the prompt past its qualified length) — including when the operator edited
                // the text, because what they edited is already this family's dialect.
                PromptCompilerId = BodyReferencePromptCompiler.CompilerId,
                Pose = pose,
                Identity = identity,
                BodyAngle = angleView is { } angle && angleSource is not null
                    ? new SceneAssetBodyAngleConditioning(angle, angleSource.Id)
                    : null
            });

        view.InputArtifactId = angleSource?.Id;
        view.OutputArtifactId = image.Id;
        view.Status = CharacterIdentityAngleStatus.Pending;
        view.ResolvedPromptText = prompt;
        view.ResolvedModelId = modelId.Trim();
        view.FailureReason = null;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);

        _logger.LogInformation(
            "Body view generated: BuildId={BuildId}, Key={Key}, ModelId={ModelId}, Family={Family}, ImageId={ImageId}, "
            + "Kind={Kind}, Omissions={Omissions}",
            build.Id, key.Describe(), view.ResolvedModelId, compiled.Family, image.Id,
            angleView is null ? "base" : "angle-render", compiled.Omissions.Count);

        return view;
    }

    /// <summary>
    /// The camera clause an angle render appends, resolved from the ONE template store and filled with the character
    /// name and the body card line exactly as an edit instruction is — so the angle wording is data an operator can
    /// read, override per character and change without a code change.
    /// </summary>
    private async Task<string> ResolveRenderClauseAsync(
        CharacterIdentityBuild build,
        CharacterIdentityBodyViewKey key,
        string characterName,
        CharacterBodyCard card,
        CancellationToken cancellationToken)
    {
        var clause = await _templates.ResolveAsync(
            CharacterBodyViewPrompts.RenderKeyFor(key), build.CharacterTemplateId, cancellationToken);
        return Fill(clause.Body, characterName, card.ToPromptLine());
    }

    public async Task<CharacterIdentityBodyView> EditFromAcceptedSourceAsync(
        string buildId, CharacterIdentityBodyViewKey key, string modelId, string characterName,
        string? promptOverride = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException($"An editor model is required to create the {Describe(key)}.");
        }

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        await RequireCompleteBodyCardAsync(build.CharacterTemplateId, cancellationToken);
        var source = await RequireAcceptedSourceAsync(build.Id, key, cancellationToken);
        // An edit is a request about an image that already exists, so its prompt is the stored rotation instruction —
        // including for the unclothed front, which is the one view that is BOTH generatable and derivable.
        var prompt = string.IsNullOrWhiteSpace(promptOverride)
            ? await ResolveEditInstructionAsync(build.Id, key, characterName, cancellationToken)
            : promptOverride.Trim();
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);

        var image = await _assets.EnqueueImageEditAsync(
            source.AssetId,
            source.Id,
            prompt,
            modelId.Trim(),
            cancellationToken,
            candidateBatchId: key.BatchIdFor(build.Id));

        view.InputArtifactId = source.Id;
        view.OutputArtifactId = image.Id;
        view.Status = CharacterIdentityAngleStatus.Pending;
        view.ResolvedPromptText = prompt;
        view.ResolvedModelId = modelId.Trim();
        view.FailureReason = null;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);

        _logger.LogInformation(
            "Body view edit queued: BuildId={BuildId}, Key={Key}, SourceImageId={SourceImageId}, ImageId={ImageId}",
            build.Id, key.Describe(), source.Id, image.Id);

        return view;
    }

    public async Task<CharacterIdentityBodyView> RecordSourceAsResultAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var source = await RequireAcceptedSourceAsync(build.Id, key, cancellationToken);
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);

        // The artifact is the accepted source itself: this records the slot, it does not pretend to have produced
        // a new image.
        view.InputArtifactId = source.Id;
        view.OutputArtifactId = source.Id;
        view.Status = CharacterIdentityAngleStatus.Complete;
        view.FailureReason = null;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);
        return view;
    }

    public async Task<CharacterIdentityBodyView> UploadAsync(
        string buildId, CharacterIdentityBodyViewKey key, string fileName, Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException($"A file name is required to upload the {Describe(key)}.");
        }

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var container = await EnsureContainerAsync(build.Id, cancellationToken);
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);
        var uploaded = await _assets.AddUploadedImageAsync(
            container.Id, fileName.Trim(), content, cancellationToken, candidateBatchId: key.BatchIdFor(build.Id));

        view.OutputArtifactId = uploaded.Id;
        view.Status = CharacterIdentityAngleStatus.Complete;
        view.ResolvedPromptText = null;
        view.ResolvedModelId = null;
        view.FailureReason = null;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);
        return view;
    }

    public async Task<CharacterIdentityBodyView> RecordResultAsync(
        string buildId, CharacterIdentityBodyViewKey key, string outputArtifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        if (string.IsNullOrWhiteSpace(outputArtifactId))
        {
            throw new InvalidOperationException($"An image id is required to record the {Describe(key)} result.");
        }

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var image = await _assets.GetImageAsync(outputArtifactId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException(
                $"The image '{outputArtifactId}' recorded for the {Describe(key)} was not found.");
        if (image.Status != SceneAssetStatus.Complete)
        {
            throw new InvalidOperationException(
                $"The image '{image.Id}' recorded for the {Describe(key)} is not complete yet.");
        }

        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);
        view.OutputArtifactId = image.Id;
        view.Status = CharacterIdentityAngleStatus.Complete;
        view.FailureReason = null;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);
        return view;
    }

    public async Task<CharacterIdentityBodyView> AcceptAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);

        // WHICH image this view becomes: the one the operator marked ACCEPTED in this view's deck, when there is one.
        // The row used to accept whatever the LAST request produced, so approving a different attempt in the candidate
        // deck left the view pointing at an image nobody had approved — the operator's report "the clothed front is
        // always the last one generated, not the Accepted one" (2026-09-23). The rule is deterministic and stated: the
        // deck's accepted image wins, otherwise the view's current artifact is accepted.
        var candidates = await _assets.ListImagesByCandidateBatchAsync(key.BatchIdFor(build.Id), cancellationToken);
        var approved = candidates.FirstOrDefault(
            candidate => candidate.CandidateDecision == SceneAssetCandidateDecision.Accepted);
        if (approved is not null && !string.Equals(approved.Id, view.OutputArtifactId, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Body view acceptance takes the deck's ACCEPTED image: BuildId={BuildId}, Key={Key}, ApprovedImageId={Approved}, " +
                "PreviousArtifactId={Previous}",
                build.Id, key.Describe(), approved.Id, view.OutputArtifactId);

            view.OutputArtifactId = approved.Id;
            view.ResolvedPromptText = approved.Prompt;
            view.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertBodyViewAsync(view, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(view.OutputArtifactId))
        {
            throw new InvalidOperationException(
                $"The {Describe(key)} has no image yet, so there is nothing to accept.");
        }

        var image = await _assets.GetImageAsync(view.OutputArtifactId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The image '{view.OutputArtifactId}' of the {Describe(key)} was not found.");
        if (image.Status != SceneAssetStatus.Complete)
        {
            throw new InvalidOperationException($"The {Describe(key)} is not complete yet.");
        }

        // The body-invariant gate: nothing is accepted on the strength of the image being complete.
        RequireFindingsOrOverride(view, key);

        view.Status = CharacterIdentityAngleStatus.Accepted;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);

        // Accepting the clothed front finishes the plan's acquisition step, which is what every other view waits
        // on. The plan seeds that step with the CLOTHED acquisition template, so the unclothed front is angle work
        // like every other view: it is still a base for its own state (every unclothed view derives from it), but
        // it must not complete the Front step a second time — the step is already done by then, and a second
        // completion both conflicts with the plan order and would record a different image against the step.
        if (view.State == SceneImageReferenceBodyState.Clothed && view.View == SceneImageReferenceBodyView.Front)
        {
            await _builds.CompleteStepAsync(
                build.Id,
                CharacterIdentityBuildStep.Front,
                inputArtifactId: view.InputArtifactId,
                outputArtifactId: view.OutputArtifactId,
                resolvedPromptText: view.ResolvedPromptText,
                resolvedModelId: view.ResolvedModelId,
                cancellationToken: cancellationToken);
        }

        _logger.LogInformation(
            "Body view accepted: BuildId={BuildId}, Key={Key}, ImageId={ImageId}, Base={IsBase}",
            build.Id, key.Describe(), view.OutputArtifactId, view.IsBase);

        return view;
    }

    /// <summary>
    /// Accepts one CANDIDATE image as this view's result and then accepts the view, in ONE operator action.
    ///
    /// Two acts existed and were easy to confuse: deciding an image in the candidate deck (a judgement about that
    /// image) and accepting the VIEW (which is what the next step waits on). Accepting an image this view produced IS
    /// accepting the view — the judgement is about the body in that picture — so they travel together, under the same
    /// body-invariant findings gate. Accepting the clothed front therefore still completes the Front step.
    /// </summary>
    public async Task<CharacterIdentityBodyView> AcceptCandidateAsync(
        string buildId, CharacterIdentityBodyViewKey key, string imageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        if (string.IsNullOrWhiteSpace(imageId))
        {
            throw new InvalidOperationException(
                $"An image id is required to accept a candidate of the {Describe(key)}.");
        }

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);

        var image = await _assets.GetImageAsync(imageId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"The candidate image '{imageId}' was not found.");
        if (image.Status != SceneAssetStatus.Complete)
        {
            throw new InvalidOperationException(
                $"The candidate image '{imageId}' is {image.Status}, so it cannot be accepted as this view's result.");
        }

        // Only an image THIS view produced may become its result. Accepting another view's candidate would record a
        // different request's image against this one, which is exactly what the per-view batch exists to prevent.
        var batchId = key.BatchIdFor(build.Id);
        if (!string.Equals(image.CandidateBatchId, batchId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The image '{imageId}' belongs to batch '{image.CandidateBatchId ?? "(none)"}', not this view's batch "
                + $"'{batchId}', so it cannot be accepted as the {Describe(key)}.");
        }

        await _assets.SetImageCandidateDecisionAsync(
            image.Id, SceneAssetCandidateDecision.Accepted, notes: null, cancellationToken);

        // The view's RESULT is the accepted image, and its prompt is that image's prompt: the row then shows and
        // reports what was accepted instead of the last thing that happened to be queued.
        view.OutputArtifactId = image.Id;
        view.ResolvedPromptText = image.Prompt;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);

        // The view's own acceptance (and, for the clothed front, the plan step) runs through the ONE acceptance path,
        // so the findings gate cannot be bypassed by accepting from the candidate deck instead of the row button.
        return await AcceptAsync(buildId, key, cancellationToken);
    }

    /// <summary>
    /// The body-invariant gate, applied where acceptance happens: a view is accepted only when every check is a
    /// recorded pass, or an explicit attributed override has been taken. A missing check is not a pass — the
    /// reviewer who never looked is exactly who this refuses (B-122 B122-013).
    /// </summary>
    private static void RequireFindingsOrOverride(CharacterIdentityBodyView view, CharacterIdentityBodyViewKey key)
    {
        var notPassed = view.Findings.NotPassed;
        if (notPassed.Count == 0 || view.ManualOverrideApplied)
        {
            return;
        }

        var detail = string.Join(
            "; ",
            notPassed.Select(item => item.Verdict == CharacterIdentityBodyCheckVerdict.NotReviewed
                ? $"{CharacterIdentityBodyChecks.Label(item.Check)} (not reviewed)"
                : $"{CharacterIdentityBodyChecks.Label(item.Check)} (failed)"));

        throw new InvalidOperationException(
            $"The {Describe(key)} cannot be accepted: {detail}. Review those checks against the body card and record "
            + "the findings, or record an explicit override with a reason and an author.");
    }

    private static CharacterIdentityBodyCheckVerdict RequireVerdict(CharacterIdentityBodyCheckVerdict verdict)
        => Enum.IsDefined(verdict)
            ? verdict
            : throw new InvalidOperationException($"Unsupported body check verdict '{(int)verdict}'.");

    public async Task<CharacterIdentityBodyView> RecordFindingsAsync(
        string buildId, CharacterIdentityBodyViewKey key,
        IReadOnlyDictionary<CharacterIdentityBodyCheck, CharacterIdentityBodyCheckVerdict> verdicts,
        string reviewer, string? note = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        ArgumentNullException.ThrowIfNull(verdicts);
        if (verdicts.Count == 0)
        {
            throw new InvalidOperationException($"At least one body check is required to review the {Describe(key)}.");
        }

        if (string.IsNullOrWhiteSpace(reviewer))
        {
            throw new InvalidOperationException(
                $"A reviewer is required to record findings for the {Describe(key)}: an unattributed judgement is "
                + "not evidence.");
        }

        foreach (var check in verdicts.Keys)
        {
            CharacterIdentityBodyChecks.Require(check);
            RequireVerdict(verdicts[check]);
        }

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);
        foreach (var (check, verdict) in verdicts)
        {
            view.Findings.Set(check, verdict);
        }

        view.Findings.Note = string.IsNullOrWhiteSpace(note) ? view.Findings.Note : note.Trim();
        view.Findings.ReviewedBy = reviewer.Trim();
        view.Findings.ReviewedUtc = DateTime.UtcNow;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);

        _logger.LogInformation(
            "Body view findings recorded: BuildId={BuildId}, Key={Key}, Reviewer={Reviewer}, NotPassed={NotPassed}",
            build.Id, key.Describe(), view.Findings.ReviewedBy,
            string.Join(", ", view.Findings.NotPassed.Select(item => item.Check.ToString())));

        return view;
    }

    public async Task<CharacterIdentityBodyView> AnalyzeQualityAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);
        if (string.IsNullOrWhiteSpace(view.OutputArtifactId))
        {
            throw new InvalidOperationException(
                $"The {Describe(key)} has no image yet, so there is nothing to analyze.");
        }

        var downloaded = await _assets.OpenImageForDownloadAsync(view.OutputArtifactId, cancellationToken);
        await using var content = downloaded.Stream;
        var (rating, notes) = _quality.Analyze(
            content, downloaded.Image.Width ?? 0, downloaded.Image.Height ?? 0, downloaded.Image.ByteLength);

        view.QualityRating = rating;
        view.QualityNotes = notes;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);

        _logger.LogInformation(
            "Body view quality analyzed: BuildId={BuildId}, Key={Key}, Rating={Rating}",
            build.Id, key.Describe(), rating);

        return view;
    }

    public async Task<CharacterIdentityBodyView> RecordOverrideAsync(
        string buildId, CharacterIdentityBodyViewKey key, string reason, string author,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.Validate();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("A reason is required to record a body view override.");
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            throw new InvalidOperationException("An author is required to record a body view override.");
        }

        var build = await RequireBodyBuildAsync(buildId, cancellationToken);
        var view = await LoadOrCreateAsync(build.Id, key, cancellationToken);
        view.ManualOverrideApplied = true;
        view.ManualOverrideReason = reason.Trim();
        view.ManualOverrideAuthor = author.Trim();
        view.ManualOverrideUtc = DateTime.UtcNow;
        view.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertBodyViewAsync(view, cancellationToken);
        return view;
    }

    private async Task<CharacterIdentityBuild> RequireBodyBuildAsync(
        string buildId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buildId))
        {
            throw new InvalidOperationException("A build id is required.");
        }

        var build = await _builds.GetBuildAsync(buildId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Character identity build '{buildId}' was not found.");
        if (build.TargetKind != CharacterIdentityTargetKind.Body)
        {
            throw new InvalidOperationException(
                $"Build '{build.Id}' is a {build.TargetKind} build; the body target only drives "
                + $"{CharacterIdentityTargetKind.Body} builds.");
        }

        return build;
    }

    private async Task<CharacterBodyCard> RequireCompleteBodyCardAsync(
        string characterProfileId, CancellationToken cancellationToken)
    {
        var card = await _bodyCards.GetAsync(characterProfileId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Character '{characterProfileId}' has no body card yet, so no body reference can be generated. "
                + "Record the body card first.");
        card.RequireReadyForGeneration();
        return card;
    }

    /// <summary>
    /// The view this request is produced from, which must be accepted: the canonical front base for the
    /// three-quarters (and for extended views), and the accepted three-quarter on that side for a profile. A base
    /// has no source — it is generated.
    /// </summary>
    private async Task<SceneAssetImage> RequireAcceptedSourceAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken)
    {
        var sourceKey = SourceKeyFor(key) ?? throw new InvalidOperationException(
            $"The {Describe(key)} is a base: it is generated or uploaded, not derived from another view.");
        var views = await _repository.ListBodyViewsAsync(buildId, cancellationToken);
        var source = views.FirstOrDefault(view => Matches(view, sourceKey))
            ?? throw new InvalidOperationException(
                $"The {Describe(sourceKey)} has not been produced yet, so the {Describe(key)} cannot be created "
                + "from it.");

        if (source.Status != CharacterIdentityAngleStatus.Accepted || string.IsNullOrWhiteSpace(source.OutputArtifactId))
        {
            throw new InvalidOperationException(
                $"The {Describe(sourceKey)} is not accepted yet (status '{source.Status}'), so the {Describe(key)} "
                + "cannot be created from it. Accept it first — a body view is always the same body.");
        }

        return await _assets.GetImageAsync(source.OutputArtifactId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The accepted image '{source.OutputArtifactId}' of the {Describe(sourceKey)} was not found.");
    }

    /// <summary>
    /// The accepted BASE body — the front of this state — that every canonical angle is rendered FROM.
    ///
    /// It is deliberately NOT the edit chain's source: an edit asks an editor to turn the previous image (so a profile
    /// comes off its own three-quarter), while an angle RENDER is "this body seen from another camera position", and
    /// all four angles were measured as "the accepted front, turned" (2026-09-23, cases <c>body-angle-34-*</c> and
    /// <c>body-profile-*</c> — including the profiles, where the front plus the profile skeleton produced a true
    /// edge-on view and placed the calf tattoo on the near leg). One base also keeps the four angles comparable to each
    /// other, which is the entire reason for producing them.
    /// </summary>
    private async Task<SceneAssetImage> RequireAcceptedBaseAsync(
        string buildId, SceneImageReferenceBodyState state, CancellationToken cancellationToken)
    {
        var baseKey = CharacterIdentityBodyViewKey.Canonical(state, SceneImageReferenceBodyView.Front);
        var views = await _repository.ListBodyViewsAsync(buildId, cancellationToken);
        var source = views.FirstOrDefault(view => Matches(view, baseKey))
            ?? throw new InvalidOperationException(
                $"The {Describe(baseKey)} has not been produced yet, so an angle of that body cannot be rendered. "
                + "Generate the base and accept it first.");

        if (source.Status != CharacterIdentityAngleStatus.Accepted || string.IsNullOrWhiteSpace(source.OutputArtifactId))
        {
            throw new InvalidOperationException(
                $"The {Describe(baseKey)} is not accepted yet (status '{source.Status}'), so its angles cannot be "
                + "rendered from it. Accept the base first — an angle is that same body, turned.");
        }

        return await _assets.GetImageAsync(source.OutputArtifactId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The accepted image '{source.OutputArtifactId}' of the {Describe(baseKey)} was not found.");
    }

    /// <summary>
    /// Which view a request is derived from, or null when it is a base. The clothed front is the root — it is
    /// generated or uploaded. The unclothed front is the same body without clothing, so it is an edit of that
    /// accepted base; the three-quarters (and every extended rotation) come off the front base, and a profile off
    /// the three-quarter on its own side — the same chain the face angles use.
    /// </summary>
    private static CharacterIdentityBodyViewKey? SourceKeyFor(CharacterIdentityBodyViewKey key) => key.View switch
    {
        SceneImageReferenceBodyView.Front when key.State == SceneImageReferenceBodyState.Unclothed =>
            CharacterIdentityBodyViewKey.Canonical(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
        SceneImageReferenceBodyView.Front => null,
        SceneImageReferenceBodyView.ProfileLeft => CharacterIdentityBodyViewKey.Canonical(
            key.State, SceneImageReferenceBodyView.ThreeQuarterLeft),
        SceneImageReferenceBodyView.ProfileRight => CharacterIdentityBodyViewKey.Canonical(
            key.State, SceneImageReferenceBodyView.ThreeQuarterRight),
        _ => CharacterIdentityBodyViewKey.Canonical(key.State, SceneImageReferenceBodyView.Front)
    };

    private async Task<CharacterIdentityBodyView> LoadOrCreateAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken)
    {
        var views = await _repository.ListBodyViewsAsync(buildId, cancellationToken);
        var existing = views.FirstOrDefault(view => Matches(view, key));
        if (existing is not null)
        {
            return existing;
        }

        return new CharacterIdentityBodyView
        {
            BuildId = buildId,
            State = key.State,
            View = key.View,
            RotationDeg = key.RotationDeg,
            PositionKey = key.PositionKey
        };
    }

    private static bool Matches(CharacterIdentityBodyView view, CharacterIdentityBodyViewKey key)
        => view.State == key.State
            && view.View == key.View
            && view.RotationDeg == key.RotationDeg
            && string.Equals(
                view.PositionKey?.Trim() ?? string.Empty,
                key.PositionKey?.Trim() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

    private static string Describe(CharacterIdentityBodyViewKey key)
        => key.IsCanonical ? $"{key.State} {key.View} body view" : $"{key.State} extended body view ({key.RotationDeg}°, {key.PositionKey})";

    private static string Fill(string body, string characterName, string cardLine)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("The resolved body prompt is empty.");
        }

        return body
            .Replace("{CharacterName}", string.IsNullOrWhiteSpace(characterName) ? "the subject" : characterName.Trim(), StringComparison.Ordinal)
            .Replace("{BodyCard}", cardLine, StringComparison.Ordinal);
    }
}
