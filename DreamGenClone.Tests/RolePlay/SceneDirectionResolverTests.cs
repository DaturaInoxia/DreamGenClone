using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Verifies that SceneDirectionResolver correctly applies 2-tier precedence
/// (theme markers > phase defaults) and marker scoping rules.
/// </summary>
public sealed class SceneDirectionResolverTests
{
    // ── NormalizePhase ──────────────────────────────────────────

    [Fact]
    public void NormalizePhase_BuildUp_CaseInsensitive_ReturnsBuildUp()
    {
        var result = SceneDirectionResolver.Resolve("buildup", null, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Medium, result.Pacing); // Phase default
    }

    [Fact]
    public void NormalizePhase_Climax_ReturnsClimaxDefaults()
    {
        var result = SceneDirectionResolver.Resolve("Climax", null, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Fast, result.Pacing);
        Assert.Equal(TimeShiftPolicy.Medium, result.TimeShift);
    }

    [Fact]
    public void NormalizePhase_Unknown_ReturnsMediumDefaults()
    {
        var result = SceneDirectionResolver.Resolve("nonexistent", null, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Medium, result.Pacing);
        Assert.Equal(BeatScope.Short, result.BeatScope);
        Assert.Equal(TimeShiftPolicy.Small, result.TimeShift);
    }

    // ── Phase defaults ──────────────────────────────────────────

    [Fact]
    public void NoTheme_NoMarkers_BuildUp_ReturnsPhaseDefaults()
    {
        var result = SceneDirectionResolver.Resolve("BuildUp", null, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Medium, result.Pacing);
        Assert.Equal(BeatScope.Short, result.BeatScope);
        Assert.Equal(TimeShiftPolicy.Small, result.TimeShift);
        Assert.Equal(DeepeningPolicy.None, result.Deepening);
    }

    [Fact]
    public void NoTheme_NoMarkers_Climax_ReturnsPhaseDefaults()
    {
        var result = SceneDirectionResolver.Resolve("Climax", null, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Fast, result.Pacing);
        Assert.Equal(TimeShiftPolicy.Medium, result.TimeShift);
        Assert.Equal(DeepeningPolicy.None, result.Deepening);
    }

    [Fact]
    public void NoTheme_NoMarkers_Reset_ReturnsPhaseDefaults()
    {
        var result = SceneDirectionResolver.Resolve("Reset", null, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Slow, result.Pacing);
        Assert.Equal(BeatScope.Single, result.BeatScope);
        Assert.Equal(TimeShiftPolicy.None, result.TimeShift);
    }

    // ── Theme markers ───────────────────────────────────────────

    [Fact]
    public void ThemeMarker_PacingFast_OverridesDefault()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.Climax, GuidanceText = "[Pacing:fast] Advance quickly." }
            ]
        };
        var result = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Fast, result.Pacing);
    }

    [Fact]
    public void ThemeMarker_PacingSlow_OverridesDefault()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.BuildUp, GuidanceText = "[Pacing:slow] Take your time." }
            ]
        };
        var result = SceneDirectionResolver.Resolve("BuildUp", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Slow, result.Pacing);
    }

    [Fact]
    public void ThemeMarker_PacingMedium_OverridesFastClimaxDefault()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.Climax, GuidanceText = "[Pacing:medium] Steady pace." }
            ]
        };
        var result = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Medium, result.Pacing); // Overrides Climax's Fast default
    }

    [Fact]
    public void ThemeMarker_TimeShiftWithinTimeframe_EnablesTimeShift()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.BuildUp, GuidanceText = "[TimeShift:within-timeframe]" }
            ]
        };
        var result = SceneDirectionResolver.Resolve("BuildUp", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(TimeShiftPolicy.Small, result.TimeShift);
    }

    [Fact]
    public void ThemeMarker_DeepeningSubsequentActors_SetsPolicy()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.Climax, GuidanceText = "[Pacing:fast] [Deepening:subsequent-actors]" }
            ]
        };
        var result = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Fast, result.Pacing);
        Assert.Equal(DeepeningPolicy.SubsequentActors, result.Deepening);
    }

    [Fact]
    public void ThemeMarker_BeatStyleEpisodic_WorksInAnyPhase()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.BuildUp, GuidanceText = "[BeatStyle:episodic]" }
            ]
        };
        var result = SceneDirectionResolver.Resolve("BuildUp", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(BeatScope.Extended, result.BeatScope);
    }

    [Fact]
    public void ThemeMarker_BeatStyleSingle_WorksInAnyPhase()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.Committed, GuidanceText = "[BeatStyle:single]" }
            ]
        };
        var result = SceneDirectionResolver.Resolve("Committed", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(BeatScope.Single, result.BeatScope);
    }

    [Fact]
    public void ThemeMarker_BeatStyleEpisodic_SetsExtendedBeatScope()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.Climax, GuidanceText = "[BeatStyle:episodic]" }
            ]
        };
        var result = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(BeatScope.Extended, result.BeatScope);
    }

    // ── Marker scoping: marker only active in its declared phase ─

    [Fact]
    public void MarkerScoping_ClimaxMarker_DoesNotAffectBuildUp()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.Climax, GuidanceText = "[Pacing:fast]" }
            ]
        };
        var result = SceneDirectionResolver.Resolve("BuildUp", theme, ClimaxSubPhase.None, PromptIntent.Message);
        // BuildUp should still use its phase default, not the Climax marker
        Assert.Equal(ScenePacing.Medium, result.Pacing);
    }

    [Fact]
    public void MarkerScoping_BuildUpMarker_DoesNotAffectClimax()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.BuildUp, GuidanceText = "[Pacing:slow]" }
            ]
        };
        var result = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        Assert.Equal(ScenePacing.Fast, result.Pacing); // Climax default, not BuildUp's slow
    }

    // ── Conflicting markers: Deepening overrides pacing for position 2+ ──

    [Fact]
    public void ConflictingMarkers_DeepeningSubsequentActors_OverridesPacingFast()
    {
        var theme = new RPTheme
        {
            Id = "test-theme",
            PhaseGuidance = [
                new RPThemePhaseGuidance { Phase = NarrativePhase.Climax, GuidanceText = "[Pacing:fast] [Deepening:subsequent-actors]" }
            ]
        };
        var result = SceneDirectionResolver.Resolve("Climax", theme, ClimaxSubPhase.None, PromptIntent.Message);
        // Both markers are resolved; Deepening is independent of Pacing
        Assert.Equal(ScenePacing.Fast, result.Pacing);
        Assert.Equal(DeepeningPolicy.SubsequentActors, result.Deepening);
    }

    // ── Climax sub-phase ─────────────────────────────────────────

    [Fact]
    public void ClimaxSubPhase_PassedThrough_ForClimax()
    {
        var result = SceneDirectionResolver.Resolve("Climax", null, ClimaxSubPhase.Early, PromptIntent.Message);
        Assert.Equal(ClimaxSubPhase.Early, result.ClimaxSubPhase);
    }

    [Fact]
    public void ClimaxSubPhase_Ignored_ForNonClimax()
    {
        var result = SceneDirectionResolver.Resolve("BuildUp", null, ClimaxSubPhase.Mid, PromptIntent.Message);
        Assert.Equal(ClimaxSubPhase.None, result.ClimaxSubPhase);
    }
}
