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
}
