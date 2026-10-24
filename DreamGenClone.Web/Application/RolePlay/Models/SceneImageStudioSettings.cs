using System.Text.Json.Serialization;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>
/// How the compiled prompt states explicitness, per render. The COMPILER's default is "match the depicted scene";
/// this setting is the operator's explicit override of that default, stated to the compiler as an authoritative line.
/// </summary>
public enum SceneImageExplicitness
{
    /// <summary>
    /// Preserve the scene's explicit content: never soften, censor or elide what the scene depicts. The default —
    /// this app is adult-content-first, so nothing is stripped unless the operator says so.
    /// </summary>
    Explicit = 0,

    /// <summary>Describe exactly what the scene depicts; let the compiler judge explicitness (no override).</summary>
    MatchScene = 1,

    /// <summary>Keep the result non-explicit: remove or soften explicit nudity and sexual acts.</summary>
    Sfw = 2
}

/// <summary>
/// Whether a participant POV shows parts of the CAMERA HOLDER's body (hands, arms, genitals, feet) in the frame.
/// </summary>
public enum CameraHolderPartsMode
{
    /// <summary>Today's behaviour: the camera holder is never depicted.</summary>
    None = 0,

    /// <summary>Derive the visible parts from the moment's recorded facts and the operator's own prose.</summary>
    Derived = 1,

    /// <summary>Use the operator's own text in <c>CameraHolderPartsText</c>.</summary>
    Custom = 2
}

/// <summary>The one place an explicitness setting is worded for the compiler, so the builders never disagree.</summary>
public static class SceneImageExplicitnessWording
{
    /// <summary>The authoritative directive appended to the user prompt. Always emitted when an override is set.</summary>
    public static string Directive(SceneImageExplicitness explicitness) => explicitness switch
    {
        SceneImageExplicitness.Explicit =>
            "preserve the scene's explicit content — do not soften, censor or elide what is depicted; if the scene is explicit, write it explicitly.",
        SceneImageExplicitness.MatchScene =>
            "match the scene exactly as depicted — neither add nor remove explicitness.",
        SceneImageExplicitness.Sfw =>
            "keep the result non-explicit — remove or soften explicit nudity and sexual acts, and describe any remaining partial nudity in the most covered terms consistent with the scene.",
        _ => throw new InvalidOperationException(
            $"Explicitness '{explicitness}' has no compiler directive. Add one deliberately: an unworded explicitness " +
            "override would silently do nothing.")
    };
}

/// <summary>Phase 2 image-generation controls used by the studio and its production services.</summary>
public sealed class SceneImageStudioSettings
{
    /// <summary>
    /// B-157 T7.1: the operator's correction to the frame this render's brief recorded — which person stands where.
    ///
    /// <para>
    /// Null or empty means the compiled frame is used exactly as compiled, which is what every render did before this
    /// existed. It is applied where the blocking is READ, so an override adjusts the brief's own frame instead of
    /// replacing it, and the preview shows the same rule the render applies.
    /// </para>
    /// </summary>
    public SceneBlockingOverride? SceneBlockingOverride { get; set; }
    /// <summary>realistic | cinematic | anime | cartoon | painterly | sketch | free text …</summary>
    public string Style { get; set; } = "realistic";

    /// <summary>
    /// How the compiled prompt states explicitness for this render. Defaults to <see cref="SceneImageExplicitness.Explicit"/>
    /// (preserve explicit content); the operator can switch to SFW or back to the compiler's match-the-scene default.
    /// Persisted beside the other render settings so every render is reproducible.
    /// </summary>
    public SceneImageExplicitness Explicitness { get; set; } = SceneImageExplicitness.Explicit;

    /// <summary>
    /// Whether a participant POV shows parts of the camera holder's body in the frame. Defaults to
    /// <see cref="CameraHolderPartsMode.None"/> (never depicted); Derived extracts the parts from the moment data, and
    /// Custom uses <see cref="CameraHolderPartsText"/>.
    /// </summary>
    public CameraHolderPartsMode CameraHolderParts { get; set; } = CameraHolderPartsMode.None;

    /// <summary>
    /// The operator's own description of the camera holder's visible parts, used when
    /// <see cref="CameraHolderParts"/> is <see cref="CameraHolderPartsMode.Custom"/> (e.g. "his hands grip her hips,
    /// his erect penis presses between her thighs"). Required when Custom is chosen.
    /// </summary>
    public string? CameraHolderPartsText { get; set; }

    public string ImageSize { get; set; } = "1024x1024";

    public string? AspectRatio { get; set; }

    /// <summary>
    /// Optional camera angle override applied to the Omniscient (external fly-on-the-wall) POV. When
    /// null the frame defaults to a neutral wide composition. Ignored for participant POVs.
    /// </summary>
    public string? OmniscientAngle { get; set; }

    /// <summary>
    /// Optional fixed ComfyUI sampler seed. When set the render is reproducible; when null the
    /// production request receives an execution seed for a fresh render.
    /// </summary>
    public long? Seed { get; set; }

    /// <summary>CFG scale control for the qualified photographic production recipe.</summary>
    [JsonPropertyName("guidance")]
    public double? Cfg { get; set; } = 5.0;

    /// <summary>Sampling step count for the qualified photographic production recipe.</summary>
    public int? Steps { get; set; } = 30;

    /// <summary>Sampler name for the qualified photographic production recipe.</summary>
    [JsonPropertyName("sampler")]
    public string? SamplerName { get; set; } = "dpmpp_2m_sde";

    /// <summary>Scheduler name (karras).</summary>
    public string? Scheduler { get; set; } = "karras";

    /// <summary>CLIP skip layer ("" = none, matching the validated test prompts).</summary>
    public string? ClipSkip { get; set; } = "";

    /// <summary>
    /// Optional OpenPose ControlNet conditioning for a pose-controlled composition (B-117). When set
    /// the render job routes to the pose-conditioned client on the pinned local ComfyUI SDXL model.
    /// </summary>
    public SceneImagePoseReference? PoseReference { get; set; }

    /// <summary>
    /// User-authored per-element overrides/removals for the canonical prompt payload (the Composition
    /// Composer "Prompt input" inspector). Applied as hard substitutions to the compiled brief's
    /// semantic snapshot at prompt-build time so the prompt compiler sees exactly one value per
    /// element — never the original plus an override. Null/empty = the compiled brief is used
    /// unchanged (default behavior).
    /// </summary>
    public ScenePromptOverrides? PromptOverrides { get; set; }

    /// <summary>
    /// The character LoRAs this render applies, in chain order (one per bound character). Null/empty means NO
    /// LoRA: the graph then emits no <c>LoraLoader</c> node and the render is byte-for-byte the render this app
    /// produced before LoRA identity existed.
    ///
    /// This is the OPERATOR's choice, recorded per render so that (a) two versions of a character's LoRA can be
    /// compared on the same beat and seed, and (b) a multi-character frame can be reproduced exactly. Identity
    /// strategies are siblings: choosing a LoRA here does not disable the model's reference/IP-Adapter route,
    /// which still applies to every render that selects no LoRA.
    /// </summary>
    public List<SceneImageCharacterLoraSelection>? CharacterLoras { get; set; }

    /// <summary>
    /// The NON-IDENTITY LoRAs this render applies (unlock / act / anatomy / style), in the operator's chosen order.
    /// Null/empty means NO scene LoRA: the graph then emits no loader node for them and the render is identical to
    /// one made before the scene-LoRA catalog existed.
    ///
    /// <para>
    /// This is the OPERATOR's multi-select, resolved against the <c>SceneLora</c> catalog and filtered to the
    /// selected model's family (B-137 §4). The model row carries no LoRA stack, so nothing here can be
    /// force-applied: the render applies exactly what was picked, and character identity LoRAs are chained AFTER
    /// these so identity stays closest to the subject.
    /// </para>
    /// </summary>
    public List<SceneImageLoraSelection>? SceneLoras { get; set; }
}

/// <summary>
/// One scene LoRA the operator selected for a render: which catalog file, and at what strength.
///
/// <para>
/// Keyed by FILE NAME rather than by a catalog row id: the file name is what ComfyUI loads and what the audit
/// event records, so a stored selection stays readable (and reproducible) even if the catalog row is later
/// re-created. The row is still resolved at render time, so a selection naming a file that is not in the catalog,
/// or that belongs to another family, fails the render instead of being dropped quietly.
/// </para>
/// </summary>
public sealed class SceneImageLoraSelection
{
    /// <summary>The catalog row's ComfyUI file name, e.g. <c>krea2_nsfw_v4_v43exp.safetensors</c>.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Model and clip strength for this LoRA. Required and positive whenever a file is named: a LoRA applied at a
    /// strength nobody chose is a different LoRA, so an unstated strength is refused at resolution time rather
    /// than guessed (the same rule the character-LoRA selection follows).
    /// </summary>
    public double? Strength { get; set; }
}

/// <summary>
/// Optional OpenPose ControlNet conditioning attached to a composition render (B-117). When present,
/// the render is routed to the pose-conditioned client on the resolved local ComfyUI SDXL model
/// instead of a bare text-to-image call. The pose image is stored through the scene image storage
/// service and its relative path is persisted here.
/// </summary>
public sealed class SceneImagePoseReference
{
    /// <summary>Stored pose skeleton image path (as returned by <c>SaveAsync</c>), e.g.
    /// "{sessionId}/{fileName}.png".</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>ControlNet conditioning strength (0 &lt; strength &lt;= 1).</summary>
    public double Strength { get; set; } = 0.8;
}

/// <summary>
/// One character LoRA the operator selected for a render: which trained artifact, and at what strength.
///
/// The strength is deliberately nullable-with-no-default rather than a magic number: a LoRA applied at a
/// strength nobody chose is a different identity from the one that was trained, so an unstated strength is
/// refused at resolution time instead of being guessed (the same rule the Qwen editor LoRA already follows).
/// </summary>
public sealed class SceneImageCharacterLoraSelection
{
    /// <summary>The <c>CharacterLoraArtifact</c> row to apply. Must be Qualified and must belong to the resolved
    /// render model; a mismatch fails the render rather than quietly rendering a different person.</summary>
    public string ArtifactId { get; set; } = string.Empty;

    /// <summary>Model and clip strength for this LoRA. Required and positive whenever an artifact is named.</summary>
    public double? Strength { get; set; }
}
