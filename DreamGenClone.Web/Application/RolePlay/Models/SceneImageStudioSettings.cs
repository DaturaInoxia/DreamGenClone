using System.Text.Json.Serialization;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>Phase 2 image-generation controls used by the studio and its production services.</summary>
public sealed class SceneImageStudioSettings
{
    /// <summary>realistic | cinematic | anime | cartoon | painterly | sketch | free text …</summary>
    public string Style { get; set; } = "realistic";

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
