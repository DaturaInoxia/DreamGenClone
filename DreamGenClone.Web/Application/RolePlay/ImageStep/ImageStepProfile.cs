using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// A feature a step can offer. Flags rather than booleans so one value says the whole set, and adding a feature does
/// not change the shape of every call site.
/// </summary>
[Flags]
public enum ImageStepFeature
{
    None = 0,

    /// <summary>The step presents the composer's own prompt area (as opposed to owning its prompt elsewhere).</summary>
    PromptPanel = 1,

    /// <summary>
    /// The step can DRAFT its prompt from the operator's input - the Generate Prompt control. A step without it is one
    /// whose prompt is written by hand or arrives from the host's own flow.
    /// </summary>
    CompilePrompt = 2,

    /// <summary>The step offers the lighting and facial-expression presets (B-133).</summary>
    Presets = 4,

    /// <summary>The step offers the character LoRA picker.</summary>
    CharacterLoras = 8
}

/// <summary>
/// What a STEP KIND offers, decided once for the kind rather than once per host.
///
/// <para>
/// <b>Why this exists.</b> The composer is one component, but its optional features were enabled by whichever host
/// happened to pass the matching parameter and callback, so the feature set was decided seven times - and a host that
/// forgot one silently shipped a poorer surface. That is how the asset creator ended up with no Generate Prompt while
/// the LoRA workspace had one, and how the LoRA picker appeared on the composition page alone. The features a step kind
/// offers are a fact about the KIND, so they are declared here, in the same module that already owns host knowledge
/// (<see cref="ImageStepBlueprintFactory"/>), and read by the composer.
/// </para>
///
/// <para>
/// <b>What this does NOT do.</b> It cannot conjure the data: a feature still needs the host to supply its choices and
/// its callback, because only the host can talk to the services that load them. What the table removes is the DECISION
/// - a host no longer chooses whether a step kind has lighting presets, and "this host forgot feature X" stops being
/// expressible as an accident. It becomes a declared row that is either satisfied or visibly not.
/// </para>
/// </summary>
public sealed record ImageStepProfile(ImageStepKind StepKind, ImageStepFeature Features)
{
    /// <summary>The features every step offers, whatever its kind: the prompt is the step's own output.</summary>
    private const ImageStepFeature Always =
        ImageStepFeature.PromptPanel;

    /// <summary>
    /// The features each kind offers. ONE table, and the only place a feature is turned on or off for a step kind.
    /// </summary>
    /// <remarks>
    /// Two entries are deliberate rather than incidental. A LoRA training cell does NOT offer the character LoRA
    /// picker: rendering a training cell WITH the LoRA being trained would be circular, and that exclusion is a
    /// property of the step kind, so it is stated here instead of being left to the workspace to remember. A
    /// composition step does not own a prompt panel because its prompt arrives from the moment it was opened - the
    /// host presents it, and two prompt boxes over one value is the defect this table prevents.
    /// </remarks>
    private static readonly IReadOnlyDictionary<ImageStepKind, ImageStepFeature> ByStepKind =
        new Dictionary<ImageStepKind, ImageStepFeature>
        {
            [ImageStepKind.AssetCreate] =
                Always | ImageStepFeature.CompilePrompt | ImageStepFeature.Presets | ImageStepFeature.CharacterLoras,
            [ImageStepKind.Edit] =
                Always | ImageStepFeature.CompilePrompt | ImageStepFeature.Presets | ImageStepFeature.CharacterLoras,
            [ImageStepKind.LoraCell] =
                Always | ImageStepFeature.CompilePrompt | ImageStepFeature.Presets,
            [ImageStepKind.Compose] =
                ImageStepFeature.Presets | ImageStepFeature.CharacterLoras,
            [ImageStepKind.PoseRender] = Always,
            [ImageStepKind.BodyView] = Always,
            [ImageStepKind.IdentityApply] = Always
        };

    /// <summary>
    /// The profile for a step kind. An unknown kind gets the always-on features and NOTHING optional, so a step kind
    /// nobody has decided about cannot silently acquire a control - the same posture as an unqualified model getting no
    /// slot list rather than a guessed one.
    /// </summary>
    public static ImageStepProfile ForKind(ImageStepKind stepKind) =>
        new(stepKind, ByStepKind.TryGetValue(stepKind, out var features) ? features : Always);

    /// <summary>Whether this step kind offers a feature.</summary>
    public bool Allows(ImageStepFeature feature) => (Features & feature) == feature;
}
