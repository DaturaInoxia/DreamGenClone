namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// What a character identity build is producing. The kind selects the build's step plan — the steps, their
/// order, the handler that runs each one and the prompt template that handler resolves — so a new target
/// (the B-122 body pack) is a new plan plus its handlers, never a second pipeline (FR21-035).
/// </summary>
public enum CharacterIdentityTargetKind
{
    /// <summary>The five-view face pack: front → validate → de-clothe → crop → enhance → angles → promote.</summary>
    Face = 1,

    /// <summary>The body-complete pack (B-122 Phase 0): full-body references, clothed and unclothed.</summary>
    Body = 2
}

/// <summary>
/// The handlers a step plan may name. A step plan is data, so it can name a handler the app cannot run;
/// every plan read is validated against this list and an unknown handler fails fast naming both the handler
/// and the step, instead of the build silently doing nothing at that step.
///
/// The handler is also the step's template-key namespace: <see cref="AngleFace"/> owns
/// <c>identity.angle.*</c>, so a body angle handler owns its own keys rather than inheriting the face ones.
/// </summary>
public static class CharacterIdentityBuildHandlers
{
    /// <summary>Acquires the base image for the build (generate or upload).</summary>
    public const string Front = "front";

    /// <summary>The eye-level gate: measure, classify against the configured threshold, allow a manual override.</summary>
    public const string ValidateEye = "validate.eye";

    /// <summary>Garment removal through the configured editor model and the step's template.</summary>
    public const string GarmentRemoval = "edit.garment";

    /// <summary>Framing normalisation (ImageSharp).</summary>
    public const string Crop = "edit.crop";

    /// <summary>Upscale through the configured upscaler, then Lanczos to the configured long edge.</summary>
    public const string Enhance = "edit.enhance";

    /// <summary>The four face views, each an edit of the canonical front (or of the previous view on its side).</summary>
    public const string AngleFace = "angle.face";

    /// <summary>Promotes the accepted view set into a draft identity pack, tagged per view.</summary>
    public const string PromoteFacePack = "promote.facePack";

    /// <summary>B-122 Phase 0: acquires the full-body base — the clothed front, or the unclothed one it is derived into.</summary>
    public const string FrontBody = "front.body";

    /// <summary>B-122 Phase 0: the body-invariant gate on the acquired base.</summary>
    public const string ValidateBodyBase = "validate.body.base";

    /// <summary>B-122 Phase 0: one requested body view (a canonical view, or an extended rotation/position).</summary>
    public const string AngleBody = "angle.body";

    /// <summary>B-122 Phase 0: the body-invariant gate on the views produced from the base.</summary>
    public const string ValidateBodyView = "validate.body.view";

    /// <summary>B-122 Phase 0: promotes the accepted body views into a pack with an explicit scope and canonical body pointer.</summary>
    public const string PromoteBodyPack = "promote.bodyPack";

    /// <summary>Every handler the app can run. Adding a handler means adding it here and implementing it.</summary>
    public static readonly IReadOnlyList<string> Known =
    [
        Front,
        ValidateEye,
        GarmentRemoval,
        Crop,
        Enhance,
        AngleFace,
        PromoteFacePack,
        FrontBody,
        ValidateBodyBase,
        AngleBody,
        ValidateBodyView,
        PromoteBodyPack
    ];

    public static bool IsKnown(string? handlerKey)
        => !string.IsNullOrWhiteSpace(handlerKey)
            && Known.Contains(handlerKey.Trim(), StringComparer.Ordinal);

    /// <summary>Fails fast naming the handler and the step it was named for.</summary>
    public static string RequireKnown(string? handlerKey, CharacterIdentityTargetKind kind, CharacterIdentityBuildStep step)
    {
        if (!IsKnown(handlerKey))
        {
            throw new InvalidOperationException(
                $"The step plan for target kind '{kind}' names handler '{handlerKey}' for step '{step}', which is "
                + $"not a handler this app implements. Known handlers: {string.Join(", ", Known)}.");
        }

        return handlerKey!.Trim();
    }
}

/// <summary>
/// One entry of a target kind's step plan: which step it is, where it sits in the order, which handler runs
/// it, and the prompt template that handler resolves (null when the handler has no single template — the eye
/// gate and the promote step have none, and the angle handler owns one template per view).
/// </summary>
public sealed record CharacterIdentityStepDefinition(
    CharacterIdentityTargetKind Kind,
    int Order,
    CharacterIdentityBuildStep Step,
    string HandlerKey,
    string? TemplateKey);

/// <summary>
/// A target kind's persisted pipeline: the ordered steps the build machinery walks. The machinery itself is
/// kind-agnostic — it creates a row per step, advances to the first incomplete one, and treats the plan's
/// last step as terminal — so adding a target kind changes no part of it.
/// </summary>
public sealed record CharacterIdentityStepPlan(
    CharacterIdentityTargetKind Kind, IReadOnlyList<CharacterIdentityStepDefinition> Steps)
{
    /// <summary>The steps in pipeline order.</summary>
    public IReadOnlyList<CharacterIdentityStepDefinition> Ordered =>
        Steps.OrderBy(definition => definition.Order).ToList();

    /// <summary>The step that finishes the build — the plan's last, never a hardcoded step.</summary>
    public CharacterIdentityBuildStep TerminalStep => Ordered[^1].Step;

    public bool Contains(CharacterIdentityBuildStep step) => Ordered.Any(definition => definition.Step == step);

    public int IndexOf(CharacterIdentityBuildStep step)
        => Ordered.ToList().FindIndex(definition => definition.Step == step);

    /// <summary>Fails fast when a step does not belong to this kind's pipeline.</summary>
    public CharacterIdentityStepDefinition Require(CharacterIdentityBuildStep step)
        => Ordered.FirstOrDefault(definition => definition.Step == step)
            ?? throw new InvalidOperationException(
                $"Step '{step}' is not part of the step plan for target kind '{Kind}' "
                + $"(steps: {string.Join(", ", Ordered.Select(definition => definition.Step))}).");

    /// <summary>Fails fast when the step's handler has no prompt template on this plan.</summary>
    public string RequireTemplateKey(CharacterIdentityBuildStep step)
    {
        var definition = Require(step);
        if (string.IsNullOrWhiteSpace(definition.TemplateKey))
        {
            throw new InvalidOperationException(
                $"The step plan for target kind '{Kind}' names no prompt template for step '{step}' "
                + $"(handler '{definition.HandlerKey}').");
        }

        return definition.TemplateKey!;
    }
}
