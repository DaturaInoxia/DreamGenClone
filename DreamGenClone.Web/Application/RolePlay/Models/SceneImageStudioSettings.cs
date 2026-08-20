namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>User-controlled image generation attributes for the studio.</summary>
public sealed class SceneImageStudioSettings
{
    /// <summary>realistic | cinematic | anime | cartoon | painterly | sketch | free text …</summary>
    public string Style { get; set; } = "realistic";

    public string ImageSize { get; set; } = "1024x1024";

    public string? AspectRatio { get; set; }

    /// <summary>Honored only when the resolved provider content policy is adult-allowed.</summary>
    public bool AllowExplicitImage { get; set; }
}
