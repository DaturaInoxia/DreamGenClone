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
}
