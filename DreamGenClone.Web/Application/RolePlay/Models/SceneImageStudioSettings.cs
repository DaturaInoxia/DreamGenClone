using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>User-controlled image generation attributes for the studio.</summary>
public sealed class SceneImageStudioSettings
{
    /// <summary>realistic | cinematic | anime | cartoon | painterly | sketch | free text …</summary>
    public string Style { get; set; } = "realistic";

    public string ImageSize { get; set; } = "1024x1024";

    public string? AspectRatio { get; set; }

    /// <summary>Honored only when the resolved provider content policy is adult-allowed.</summary>
    public bool AllowExplicitImage { get; set; } = true;

    /// <summary>
    /// Optional camera angle override applied to the Omniscient (external fly-on-the-wall) POV. When
    /// null the frame defaults to a neutral wide composition. Ignored for participant POVs.
    /// </summary>
    public string? OmniscientAngle { get; set; }

    /// <summary>
    /// Optional fixed ComfyUI sampler seed. When set the render is reproducible; when null the
    /// client draws a random seed each call (matching the studio's previous behavior).
    /// </summary>
    public long? Seed { get; set; }

    /// <summary>
    /// User-editable negative prompt (guard terms). Defaults to the server's SDXL guard set; blank
    /// reverts to the automatic deterministic negative.
    /// </summary>
    public string? NegativePrompt { get; set; } = SdxlSceneImagePromptBuilder.DefaultNegativePrompt;

    /// <summary>CFG scale override (Juggernaut-validated default 5.0). Null = model-family default.</summary>
    public double? Cfg { get; set; } = 5.0;

    /// <summary>Sampling step count (30). Null = model-family default.</summary>
    public int? Steps { get; set; } = 30;

    /// <summary>Sampler name (Juggernaut-validated default dpmpp_2m_sde).</summary>
    public string? SamplerName { get; set; } = "dpmpp_2m_sde";

    /// <summary>Scheduler name (karras).</summary>
    public string? Scheduler { get; set; } = "karras";

    /// <summary>CLIP skip layer ("" = none, matching the validated test prompts).</summary>
    public string? ClipSkip { get; set; } = "";
}
