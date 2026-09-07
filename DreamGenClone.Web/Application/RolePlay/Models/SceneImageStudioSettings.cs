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
}
