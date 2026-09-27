using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Verifies the B-082 sticky continuation-settings override resolution: the override wins
/// over theme markers / phase defaults, unset fields fall through unchanged, and the engine
/// marker resolution preserves the Reset / Climax phase gates.
/// </summary>
public sealed class ContinuationOverrideResolverTests
{
    private static ResolvedIntensityData Intensity(SceneDirection? dir = null) => new()
    {
        ProseStyleDirective = "p",
        VoiceDirective = "v",
        ToneDirective = "t",
        FocusDirective = "f",
        HeatLevelDirective = "h",
        SceneDirection = dir,
    };

    private static ResolvedWritingStyleData Style(int min = 200, int max = 400) => new()
    {
        Example = "e",
        PhaseRuleOfThumb = "r",
        StyleHint = "s",
        ImmersionDirective = "i",
        ActionDirective = "a",
        WordTargetMin = min,
        WordTargetMax = max,
        NarrativeWordTargetMin = min,
        NarrativeWordTargetMax = max,
    };

    private static RolePlaySession SessionWith(ContinuationOverride? ov) => new()
    {
        Id = Guid.NewGuid().ToString(),
        ContinuationOverride = ov,
    };

    // ── ApplySceneDirection ────────────────────────────────────

    [Fact]
    public void ApplySceneDirection_NullOverride_ReturnsSameInstance()
    {
        var intensity = Intensity(new SceneDirection { Pacing = ScenePacing.Fast });
        Assert.Same(intensity, ContinuationOverrideResolver.ApplySceneDirection(intensity, null));
    }

    [Fact]
    public void ApplySceneDirection_OverrideWins_UnsetFieldsUntouched()
    {
        var intensity = Intensity(new SceneDirection
        {
            Pacing = ScenePacing.Fast,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.Medium,
        });

        var result = ContinuationOverrideResolver.ApplySceneDirection(
            intensity,
            new ContinuationOverride { Pacing = ScenePacing.Slow });

        Assert.Equal(ScenePacing.Slow, result.SceneDirection!.Pacing);
        Assert.Equal(BeatScope.Short, result.SceneDirection.BeatScope);
        Assert.Equal(TimeShiftPolicy.Medium, result.SceneDirection.TimeShift);
    }

    [Fact]
    public void ApplySceneDirection_OverrideConstructsDirection_WhenBaseIsNull()
    {
        var intensity = Intensity(null);
        var result = ContinuationOverrideResolver.ApplySceneDirection(
            intensity,
            new ContinuationOverride { Granularity = NarrativeGranularity.Macro });

        Assert.NotNull(result.SceneDirection);
        Assert.Equal(NarrativeGranularity.Macro, result.SceneDirection!.Granularity);
        Assert.Equal(ScenePacing.Medium, result.SceneDirection.Pacing);
    }

    // ── ApplyWordCount ─────────────────────────────────────────

    [Fact]
    public void ApplyWordCount_NullOverride_ReturnsSameInstance()
    {
        var style = Style();
        Assert.Same(style, ContinuationOverrideResolver.ApplyWordCount(style, null));
    }

    [Fact]
    public void ApplyWordCount_OverrideWins()
    {
        var result = ContinuationOverrideResolver.ApplyWordCount(
            Style(200, 400),
            new ContinuationOverride { WordTargetMin = 500, WordTargetMax = 1000 });

        Assert.Equal(500, result.WordTargetMin);
        Assert.Equal(1000, result.WordTargetMax);
        Assert.Equal("override", result.WordTargetMarker);
    }

    // ── Engine marker resolution ───────────────────────────────

    private static RPTheme MultiEncounterTheme() => new()
    {
        Id = "t",
        PhaseGuidance =
        [
            new RPThemePhaseGuidance { Phase = NarrativePhase.Climax, GuidanceText = "[ClimaxMode:multi-encounter] [Aftermath:husband-contrast]" }
        ],
    };

    [Fact]
    public void ResolveMultiEncounterClimax_NullOverride_DefersToTheme()
    {
        Assert.True(ContinuationOverrideResolver.ResolveMultiEncounterClimax(SessionWith(null), MultiEncounterTheme()));
        Assert.False(ContinuationOverrideResolver.ResolveMultiEncounterClimax(SessionWith(null), new RPTheme()));
        Assert.False(ContinuationOverrideResolver.ResolveMultiEncounterClimax(SessionWith(null), null));
    }

    [Fact]
    public void ResolveMultiEncounterClimax_OverrideForcesOnAndOff()
    {
        var noMarkerTheme = new RPTheme();
        Assert.True(ContinuationOverrideResolver.ResolveMultiEncounterClimax(
            SessionWith(new ContinuationOverride { ForceMultiEncounterClimax = true }), noMarkerTheme));
        Assert.False(ContinuationOverrideResolver.ResolveMultiEncounterClimax(
            SessionWith(new ContinuationOverride { ForceMultiEncounterClimax = false }), MultiEncounterTheme()));
    }

    [Fact]
    public void ResolveAftermathHusbandContrast_ResetAlwaysFalse()
    {
        var session = SessionWith(new ContinuationOverride { ForceAftermathHusbandContrast = true });
        Assert.False(ContinuationOverrideResolver.ResolveAftermathHusbandContrast(session, MultiEncounterTheme(), "Reset"));
    }

    [Fact]
    public void ResolveAftermathHusbandContrast_NullOverride_DefersToTheme()
    {
        Assert.True(ContinuationOverrideResolver.ResolveAftermathHusbandContrast(SessionWith(null), MultiEncounterTheme(), "Climax"));
        Assert.False(ContinuationOverrideResolver.ResolveAftermathHusbandContrast(SessionWith(null), new RPTheme(), "Climax"));
    }

    [Fact]
    public void ResolveAftermathHusbandContrast_OverrideForcesOnAndOff()
    {
        Assert.True(ContinuationOverrideResolver.ResolveAftermathHusbandContrast(
            SessionWith(new ContinuationOverride { ForceAftermathHusbandContrast = true }), new RPTheme(), "Committed"));
        Assert.False(ContinuationOverrideResolver.ResolveAftermathHusbandContrast(
            SessionWith(new ContinuationOverride { ForceAftermathHusbandContrast = false }), MultiEncounterTheme(), "Committed"));
    }

    // ── Multi-encounter lifecycle: guard and counter must share one decision path ──────
    //
    // The boundary minIxns guard reads TurnsInCurrentEncounter, and the multi-encounter
    // lifecycle block in RunRolePlayV2PipelinesAsync is the only code that increments it.
    // Resolving the marker at one site and the override at the other left the 4-turn minimum
    // inert: the guard demanded >= 4 turns while the counter stayed at 0 forever. These tests
    // pin the shared decision path (the resolver) rather than the marker helper.

    [Fact]
    public void MultiEncounterDecision_IsTheResolver_NotTheMarkerAlone()
    {
        // A marker-less theme (e.g. exhibitionism-v3) with the override on: the marker helper
        // says NO, the resolver the guard and the lifecycle both use says YES. If these ever
        // diverge again, the guard and the counter would be reading different decisions.
        var markerlessTheme = new RPTheme { Id = "markerless" };
        var session = SessionWith(new ContinuationOverride { ForceMultiEncounterClimax = true });

        Assert.False(RolePlayAssistantPrompts.IsMultiEncounterClimax(markerlessTheme, "Climax"));
        Assert.True(ContinuationOverrideResolver.ResolveMultiEncounterClimax(session, markerlessTheme));
    }

    [Fact]
    public void MultiEncounterCounter_Accumulates_WhenOnlyTheOverrideEnablesIt()
    {
        // Mirrors the lifecycle increment gate in RunRolePlayV2PipelinesAsync: resolves through
        // the override-aware decision, not the marker.
        var markerlessTheme = new RPTheme { Id = "markerless" };
        var session = SessionWith(new ContinuationOverride { ForceMultiEncounterClimax = true });
        var state = new AdaptiveScenarioState { CurrentTimeSkipPhase = TimeSkipPhase.None };

        var isMulti = ContinuationOverrideResolver.ResolveMultiEncounterClimax(session, markerlessTheme);
        var generatedSinceLastEval = 1;
        if (isMulti && state.CurrentTimeSkipPhase == TimeSkipPhase.None)
            state.TurnsInCurrentEncounter += generatedSinceLastEval;

        Assert.Equal(1, state.TurnsInCurrentEncounter);
        // The guard can now actually be satisfied: the counter it reads is no longer frozen at 0.
        Assert.True(state.TurnsInCurrentEncounter < 4); // still below the minimum on turn 1
    }

    [Fact]
    public void MultiEncounterCounter_StaysZero_WhenNeitherOverrideNorMarkerEnableIt()
    {
        var markerlessTheme = new RPTheme { Id = "markerless" };
        var state = new AdaptiveScenarioState { CurrentTimeSkipPhase = TimeSkipPhase.None };

        var isMulti = ContinuationOverrideResolver.ResolveMultiEncounterClimax(SessionWith(null), markerlessTheme);
        if (isMulti && state.CurrentTimeSkipPhase == TimeSkipPhase.None)
            state.TurnsInCurrentEncounter += 1;

        Assert.False(isMulti);
        Assert.Equal(0, state.TurnsInCurrentEncounter);
    }

    [Fact]
    public void ResolveMultiEncounterClimax_OverrideApplies_WhenThemeCouldNotBeLoaded()
    {
        // The lifecycle resolves before the theme is necessarily readable, so a forced-on
        // override must still win over a null theme (and a forced-off must still force off).
        Assert.True(ContinuationOverrideResolver.ResolveMultiEncounterClimax(
            SessionWith(new ContinuationOverride { ForceMultiEncounterClimax = true }), null));
        Assert.False(ContinuationOverrideResolver.ResolveMultiEncounterClimax(
            SessionWith(new ContinuationOverride { ForceMultiEncounterClimax = false }), null));
    }
}
