namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class WorkspaceSettingsState
{
    public const int MinPanelWidth = 240;
    public const int MaxPanelWidth = 480;
    public const int DefaultPanelWidth = 320;

    private int _settingsPanelWidth = DefaultPanelWidth;

    public int SettingsPanelWidth
    {
        get => _settingsPanelWidth;
        set => _settingsPanelWidth = Math.Clamp(value, MinPanelWidth, MaxPanelWidth);
    }

    public void ResetPanelWidth() => _settingsPanelWidth = DefaultPanelWidth;

    /// <summary>Master on/off for scene image generation. When false the workspace hides the
    /// per-interaction "Generate image" trigger.</summary>
    public bool ImageGenerationEnabled { get; set; } = true;

    /// <summary>Free-text style cue (e.g. "cinematic lighting, 35mm") seeding the studio.</summary>
    public string? ImageStyleSuffix { get; set; }

    /// <summary>Default image size (e.g. "1024x1024").</summary>
    public string ImageSize { get; set; } = "1024x1024";

    /// <summary>Honored only when the resolved image provider content policy is adult-allowed.</summary>
    public bool AllowExplicitImage { get; set; }
}
