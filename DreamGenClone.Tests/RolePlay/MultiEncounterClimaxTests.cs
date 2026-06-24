using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class MultiEncounterClimaxMarkerTests
{
    private static RPTheme BuildThemeWithClimaxGuidance(string guidanceText) => new()
    {
        Id = "test-theme",
        PhaseGuidance =
        [
            new RPThemePhaseGuidance
            {
                Phase = NarrativePhase.Climax,
                GuidanceText = guidanceText
            }
        ]
    };

    [Fact]
    public void IsMultiEncounterClimax_ReturnsTrue_WhenMarkerPresent()
    {
        var theme = BuildThemeWithClimaxGuidance("Some guidance [ClimaxMode:multi-encounter] more text");
        Assert.True(RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax"));
    }

    [Fact]
    public void IsMultiEncounterClimax_ReturnsFalse_WhenMarkerAbsent()
    {
        var theme = BuildThemeWithClimaxGuidance("Some guidance without the marker");
        Assert.False(RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax"));
    }

    [Fact]
    public void IsMultiEncounterClimax_ReturnsFalse_WhenThemeIsNull()
    {
        Assert.False(RolePlayAssistantPrompts.IsMultiEncounterClimax(null, "Climax"));
    }

    [Fact]
    public void IsMultiEncounterClimax_ReturnsFalse_WhenPhaseDoesNotMatch()
    {
        var theme = BuildThemeWithClimaxGuidance("[ClimaxMode:multi-encounter]");
        Assert.False(RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "BuildUp"));
    }

    [Fact]
    public void IsMultiEncounterClimax_IsCaseInsensitive()
    {
        var theme = BuildThemeWithClimaxGuidance("[climaxmode:multi-encounter]");
        Assert.True(RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax"));
    }

    [Fact]
    public void EnsureClimaxModeMutualExclusion_Throws_WhenBothMarkersPresent()
    {
        var theme = BuildThemeWithClimaxGuidance("[ClimaxMode:multi-encounter] [ClimaxMode:quick-finish]");
        var ex = Assert.Throws<InvalidOperationException>(
            () => RolePlayAssistantPrompts.EnsureClimaxModeMutualExclusion(theme, "Climax"));
        Assert.Contains("ClimaxModeConflict", ex.Message, StringComparison.Ordinal);
        Assert.Contains("mutually exclusive", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureClimaxModeMutualExclusion_DoesNotThrow_WhenOnlyMultiEncounterPresent()
    {
        var theme = BuildThemeWithClimaxGuidance("[ClimaxMode:multi-encounter]");
        RolePlayAssistantPrompts.EnsureClimaxModeMutualExclusion(theme, "Climax");
    }

    [Fact]
    public void EnsureClimaxModeMutualExclusion_DoesNotThrow_WhenOnlyQuickFinishPresent()
    {
        var theme = BuildThemeWithClimaxGuidance("[ClimaxMode:quick-finish]");
        RolePlayAssistantPrompts.EnsureClimaxModeMutualExclusion(theme, "Climax");
    }

    [Fact]
    public void EnsureClimaxModeMutualExclusion_DoesNotThrow_WhenNeitherPresent()
    {
        var theme = BuildThemeWithClimaxGuidance("plain guidance with no markers");
        RolePlayAssistantPrompts.EnsureClimaxModeMutualExclusion(theme, "Climax");
    }

    [Fact]
    public void EnsureClimaxModeMutualExclusion_DoesNotThrow_WhenThemeIsNull()
    {
        RolePlayAssistantPrompts.EnsureClimaxModeMutualExclusion(null, "Climax");
    }
}

public sealed class AdaptiveScenarioStateEncounterFieldsTests
{
    [Fact]
    public void CurrentEncounterNumber_DefaultsToZero()
    {
        var state = new AdaptiveScenarioState();
        Assert.Equal(0, state.CurrentEncounterNumber);
    }

    [Fact]
    public void InteractionsInCurrentEncounter_DefaultsToZero()
    {
        var state = new AdaptiveScenarioState();
        Assert.Equal(0, state.InteractionsInCurrentEncounter);
    }

    [Fact]
    public void CurrentEncounterNumber_CanBeSetAndRetrieved()
    {
        var state = new AdaptiveScenarioState { CurrentEncounterNumber = 3 };
        Assert.Equal(3, state.CurrentEncounterNumber);
    }

    [Fact]
    public void InteractionsInCurrentEncounter_CanBeSetAndRetrieved()
    {
        var state = new AdaptiveScenarioState { InteractionsInCurrentEncounter = 5 };
        Assert.Equal(5, state.InteractionsInCurrentEncounter);
    }
}
