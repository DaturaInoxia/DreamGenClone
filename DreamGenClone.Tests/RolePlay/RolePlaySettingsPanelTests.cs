using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlaySettingsPanelTests
{
    [Fact]
    public void SettingsPanelWidth_Default_IsWithinValidRange()
    {
        var state = new WorkspaceSettingsState();

        Assert.InRange(state.SettingsPanelWidth,
            WorkspaceSettingsState.MinPanelWidth,
            WorkspaceSettingsState.MaxPanelWidth);
    }

    [Fact]
    public void SettingsPanelWidth_SetBelowMin_ClampedToMin()
    {
        var state = new WorkspaceSettingsState();

        state.SettingsPanelWidth = WorkspaceSettingsState.MinPanelWidth - 50;

        Assert.Equal(WorkspaceSettingsState.MinPanelWidth, state.SettingsPanelWidth);
    }

    [Fact]
    public void SettingsPanelWidth_SetAboveMax_ClampedToMax()
    {
        var state = new WorkspaceSettingsState();

        state.SettingsPanelWidth = WorkspaceSettingsState.MaxPanelWidth + 100;

        Assert.Equal(WorkspaceSettingsState.MaxPanelWidth, state.SettingsPanelWidth);
    }

    [Fact]
    public void SettingsPanelWidth_SetToValidValue_Persists()
    {
        var state = new WorkspaceSettingsState();
        const int targetWidth = 360;

        state.SettingsPanelWidth = targetWidth;

        Assert.Equal(targetWidth, state.SettingsPanelWidth);
    }

    [Fact]
    public void ResetPanelWidth_RestoresDefaultPanelWidth()
    {
        var state = new WorkspaceSettingsState();
        state.SettingsPanelWidth = WorkspaceSettingsState.MaxPanelWidth;

        state.ResetPanelWidth();

        Assert.Equal(WorkspaceSettingsState.DefaultPanelWidth, state.SettingsPanelWidth);
    }

    [Fact]
    public void Constants_MinIsLessThanDefaultIsLessThanMax()
    {
        Assert.True(WorkspaceSettingsState.MinPanelWidth <= WorkspaceSettingsState.DefaultPanelWidth);
        Assert.True(WorkspaceSettingsState.DefaultPanelWidth <= WorkspaceSettingsState.MaxPanelWidth);
    }
}
