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
    public void TurnsInCurrentEncounter_DefaultsToZero()
    {
        var state = new AdaptiveScenarioState();
        Assert.Equal(0, state.TurnsInCurrentEncounter);
    }

    [Fact]
    public void CurrentEncounterNumber_CanBeSetAndRetrieved()
    {
        var state = new AdaptiveScenarioState { CurrentEncounterNumber = 3 };
        Assert.Equal(3, state.CurrentEncounterNumber);
    }

    [Fact]
    public void TurnsInCurrentEncounter_CanBeSetAndRetrieved()
    {
        var state = new AdaptiveScenarioState { TurnsInCurrentEncounter = 5 };
        Assert.Equal(5, state.TurnsInCurrentEncounter);
    }
}
