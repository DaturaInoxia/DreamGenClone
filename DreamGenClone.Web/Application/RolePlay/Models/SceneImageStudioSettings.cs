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

    /// <summary>
    /// User-editable negative prompt (guard terms). The active model pipeline determines its
    /// configured guard set when this value is blank.
    /// </summary>
    public string? NegativePrompt { get; set; } = SdxlSceneImagePromptBuilder.DefaultNegativePrompt;

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
