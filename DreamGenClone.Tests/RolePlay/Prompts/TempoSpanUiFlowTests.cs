using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// B-089/B-090 UI-flow tests. The repo has no bUnit/Playwright harness, so these validate the
/// full chain the Continuation Settings popup drives: popup working-copy mutation
/// (SelectTempo/SelectSpan/Clear/Save) → ContinuationOverride → ContinuationOverrideResolver
/// (coherent bundle) → FinalInstructionSlot → the emitted prompt text. The popup-mirror helpers
/// below reproduce the exact working-copy semantics in ContinuationSettingsPopup.razor so the
/// test asserts the same contract the Razor component implements. Reference patterns:
/// TempoSpanDirectiveTests, ContinuationOverrideSlotTests, ContinuationOverrideResolverTests.
/// </summary>
public sealed class TempoSpanUiFlowTests
{
    private readonly FinalInstructionSlot _slot = new(NullLogger<FinalInstructionSlot>.Instance);

    // ── Popup working-copy mirrors (ContinuationSettingsPopup.razor @code) ──
    // These reproduce SelectTempo/SelectSpan/Clear/SaveAsync exactly. Selecting a Tempo clears
    // the conflicting raw fields (Pacing/TimeShift/Granularity) and selecting a Span clears
    // BeatScope, so the persisted override never carries a Tempo bundle AND a contradictory raw
    // field (which T8's ValidateCoherentOverride would otherwise reject).

    private static ContinuationOverride PopupSelectTempo(ContinuationOverride working, SceneTempo tempo)
    {
        working.Tempo = tempo;
        working.Pacing = null;
        working.TimeShift = null;
        working.Granularity = null;
        return working;
    }

    private static ContinuationOverride PopupClearTempo(ContinuationOverride working)
    {
        working.Tempo = null;
        return working;
    }

    private static ContinuationOverride PopupSelectSpan(ContinuationOverride working, SceneSpan span)
    {
        working.Span = span;
        working.BeatScope = null;
        return working;
    }

    private static ContinuationOverride PopupClearSpan(ContinuationOverride working)
    {
        working.Span = null;
        return working;
    }

    /// <summary>Mirrors SaveAsync: an empty working copy persists as null (clears the override).</summary>
    private static ContinuationOverride? PopupSave(ContinuationOverride working)
        => working.HasAny ? working : null;

    // ── Test scaffolding ──
    // A representative theme default: Steady tempo (Medium/Small/Meso) + Scene span (Short).

    private static readonly SceneDirection ThemeDefaultDirection = new()
    {
        Pacing = ScenePacing.Medium,
        BeatScope = BeatScope.Short,
        TimeShift = TimeShiftPolicy.Small,
        Granularity = NarrativeGranularity.Meso,
    };

    private static SceneDirection ResolveEffectiveDirection(ContinuationOverride? ov, SceneDirection? baseDirection = null)
        => ContinuationOverrideResolver.ApplySceneDirectionOverride(baseDirection ?? ThemeDefaultDirection, ov);

    private static PromptBuildContext CreateContext(
        PromptVariant variant,
        int? positionInTurn,
        SceneDirection effectiveDirection,
        int turnsInCurrentBeat = 0,
        int? turnIndex = 37,
        string? currentBeatCode = null)
    {
        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            ScenarioId = "test-scenario",
            PersonaName = "Ken",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = NarrativePhase.Committed,
                CurrentBeatCode = currentBeatCode,
                TurnsInCurrentBeat = turnsInCurrentBeat,
            },
        };

        return new PromptBuildContext
        {
            Session = session,
            ActorProfile = new ActorProfile
            {
                Kind = variant == PromptVariant.Narrative ? ActorProfileKind.Narrative : ActorProfileKind.Player,
                ActorName = variant == PromptVariant.Narrative ? "Narrator" : "Ken",
                ActorRole = "Hero",
                PresentCharacterIds = [],
                AllCharacterIds = [],
            },
            Variant = variant,
            Phase = "Committed",
            TurnIndex = turnIndex,
            PositionInTurn = positionInTurn,
            TurnActorCount = 2,
            PromptText = "Continue naturally.",
            MaxPromptChars = 35000,
            Scenario = new ResolvedScenarioData
            {
                ScenarioId = "test-scenario",
                Name = "Test",
                Description = "Test",
                PlotDescription = "Plot",
                WorldDescription = "World",
                TimeFrame = null,
                Goals = [],
                Conflicts = [],
                WorldRules = [],
                EnvironmentalDetails = [],
                NarrativeGuidelines = [],
                Characters = [],
                Locations = [],
                DefaultSteeringProfileId = null,
                DefaultIntensityProfileId = null,
                DefaultStartingLocationName = null,
            },
            Theme = new ResolvedThemeData(),
            Intensity = new ResolvedIntensityData
            {
                ProseStyleDirective = "p",
                VoiceDirective = "v",
                ToneDirective = "t",
                FocusDirective = "f",
                HeatLevelDirective = "h",
                SceneDirection = effectiveDirection,
            },
            WritingStyle = new ResolvedWritingStyleData
            {
                Example = "e",
                PhaseRuleOfThumb = "r",
                StyleHint = "s",
                ImmersionDirective = "i",
                ActionDirective = "a",
                WordTargetMin = 200,
                WordTargetMax = 400,
                NarrativeWordTargetMin = 200,
                NarrativeWordTargetMax = 400,
            },
            NarrativeTone = new ResolvedNarrativeToneData(),
            EncounterSummaries = [],
            RecentInteractions = [],
            PinnedInteractions = [],
            StagedInteractions = [],
        };
    }

    private async Task<string> PromptForAsync(
        PromptVariant variant,
        int? positionInTurn,
        ContinuationOverride? ov,
        int turnsInCurrentBeat = 0,
        int? turnIndex = 37,
        SceneDirection? baseDirection = null,
        string? currentBeatCode = null)
    {
        var effective = ResolveEffectiveDirection(ov, baseDirection);
        var context = CreateContext(variant, positionInTurn, effective, turnsInCurrentBeat, turnIndex, currentBeatCode);
        return await _slot.WriteAsync(context, CancellationToken.None);
    }

    // ── A1. UI contract: popup working-copy semantics ──

    [Theory]
    [InlineData(SceneTempo.Linger)]
    [InlineData(SceneTempo.Steady)]
    [InlineData(SceneTempo.Push)]
    [InlineData(SceneTempo.Leap)]
    public void PopupSelectTempo_ClearsConflictingRawFields_OverrideStaysCoherent(SceneTempo tempo)
    {
        var working = new ContinuationOverride
        {
            // Simulate a stale legacy override with contradictory raw fields on the session.
            Pacing = ScenePacing.Fast,
            TimeShift = TimeShiftPolicy.Large,
            Granularity = NarrativeGranularity.Montage,
            BeatScope = BeatScope.Single,
        };

        PopupSelectTempo(working, tempo);

        Assert.Equal(tempo, working.Tempo);
        Assert.Null(working.Pacing);
        Assert.Null(working.TimeShift);
        Assert.Null(working.Granularity);
        // The cleared raw fields must never leave a Tempo + contradictory raw combination behind.
        ContinuationOverrideResolver.ValidateCoherentOverride(working);
        Assert.True(working.HasSceneDirectionOverride);
        Assert.True(working.HasAny);
    }

    [Theory]
    [InlineData(SceneSpan.Moment, 1, BeatScope.Single)]
    [InlineData(SceneSpan.Scene, 3, BeatScope.Short)]
    [InlineData(SceneSpan.ExtendedArc, 5, BeatScope.Extended)]
    public void PopupSelectSpan_SetsSpan_ClearsBeatScope(SceneSpan span, int budget, BeatScope expectedScope)
    {
        var working = new ContinuationOverride { BeatScope = BeatScope.Extended };
        PopupSelectSpan(working, span);

        Assert.Equal(span, working.Span);
        Assert.Null(working.BeatScope);
        Assert.Equal(budget, SceneDirection.SpanTurnBudget(span));
        Assert.Equal(expectedScope, SceneDirection.SpanToBeatScope(span));
        Assert.True(working.HasAny);
    }

    [Fact]
    public void PopupSave_EmptyWorking_ReturnsNull_ClearsOverride()
    {
        var working = new ContinuationOverride();
        Assert.Null(PopupSave(working));
    }

    [Fact]
    public void PopupSave_TempoSelected_ReturnsOverride_NotCleared()
    {
        var working = PopupSelectTempo(new ContinuationOverride(), SceneTempo.Leap);
        var saved = PopupSave(working);
        Assert.NotNull(saved);
        Assert.Equal(SceneTempo.Leap, saved.Tempo);
    }

    [Fact]
    public void PopupClearTempo_ClearsTempo_Only()
    {
        var working = PopupSelectTempo(new ContinuationOverride(), SceneTempo.Push);
        PopupSelectSpan(working, SceneSpan.Scene);
        PopupClearTempo(working);

        Assert.Null(working.Tempo);
        Assert.Equal(SceneSpan.Scene, working.Span);
        // Span alone is still a valid, saveable override.
        Assert.True(working.HasAny);
    }

    [Fact]
    public void PopupClearSpan_ClearsSpan_Only()
    {
        var working = PopupSelectTempo(new ContinuationOverride(), SceneTempo.Steady);
        PopupSelectSpan(working, SceneSpan.Scene);
        PopupClearSpan(working);

        Assert.Null(working.Span);
        Assert.Equal(SceneTempo.Steady, working.Tempo);
        Assert.True(working.HasAny);
    }

    [Fact]
    public void StaleOverride_TempoPlusContradictoryRaw_Throws_WhenNotClearedByPopup()
    {
        // Directly-built override (bypassing the popup) that still carries a contradiction.
        // The popup prevents this; the resolver fails fast if it somehow reaches the prompt.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContinuationOverrideResolver.ApplySceneDirectionOverride(
                ThemeDefaultDirection,
                new ContinuationOverride { Tempo = SceneTempo.Linger, Pacing = ScenePacing.Fast }));
        Assert.Contains("ContinuationOverrideConflict", ex.Message);
    }

    // ── B. UI → resolver → prompt output (lead actor) ──

    [Theory]
    [InlineData(SceneTempo.Linger, "Stay in this exact moment")]
    [InlineData(SceneTempo.Steady, "Advance the scene by one beat, then stop")]
    [InlineData(SceneTempo.Push, "Advance through two to three beats this response")]
    [InlineData(SceneTempo.Leap, "Advance time by a day or more")]
    public async Task SelectTempo_LeadActor_Prompt_EmitsFinalizedWording(SceneTempo tempo, string expectedFragment)
    {
        var working = PopupSelectTempo(new ContinuationOverride(), tempo);
        var saved = PopupSave(working)!;

        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, saved);

        Assert.Contains($"HARD CONSTRAINT — Tempo: {tempo}.", text);
        Assert.Contains(expectedFragment, text);
        // Lead actor also receives a Span HC (theme default Scene → 3-turn beat).
        Assert.Contains("HARD CONSTRAINT — Span: Scene.", text);
    }

    [Fact]
    public async Task SelectTempoLeap_ResolvesCoherentBundle_AndPromptMatches()
    {
        var working = PopupSelectTempo(new ContinuationOverride(), SceneTempo.Leap);
        var saved = PopupSave(working)!;

        var effective = ResolveEffectiveDirection(saved);
        Assert.Equal(ScenePacing.Fast, effective.Pacing);
        Assert.Equal(TimeShiftPolicy.Large, effective.TimeShift);
        Assert.Equal(NarrativeGranularity.Macro, effective.Granularity);
        Assert.Equal(SceneTempo.Leap, effective.Tempo);

        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, saved);
        Assert.Contains("HARD CONSTRAINT — Tempo: Leap.", text);
        Assert.Contains("Advance time by a day or more", text);
    }

    [Fact]
    public async Task SelectSpanScene_Turn1_Prompt_EstablishesOnly_WithAbsoluteAnchor()
    {
        var working = PopupSelectSpan(new ContinuationOverride(), SceneSpan.Scene);
        var saved = PopupSave(working)!;

        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, saved, turnsInCurrentBeat: 0, turnIndex: 37);

        Assert.Contains("HARD CONSTRAINT — Span: Scene.", text);
        Assert.Contains("This moment spans 3 turns, beginning this turn (Turn 37).", text);
        Assert.Contains("You are on turn 1 of 3 — establish it only. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.", text);
    }

    [Fact]
    public async Task SelectSpanScene_FinalTurn_Prompt_Concludes_WithAbsoluteAnchor()
    {
        var working = PopupSelectSpan(new ContinuationOverride(), SceneSpan.Scene);
        var saved = PopupSave(working)!;

        // Turns 35, 36 completed this beat; current turn 37 is the concluding turn (3 of 3).
        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, saved, turnsInCurrentBeat: 2, turnIndex: 37);

        Assert.Contains("This moment ends this turn (turn 3 of 3, which began at Turn 35) — bring it to its climax or conclusion now and move on.", text);
    }

    [Fact]
    public async Task SelectSpanExtendedArc_MiddleTurn_Prompt_DevelopsFurther_WithAbsoluteAnchor()
    {
        var working = PopupSelectSpan(new ContinuationOverride(), SceneSpan.ExtendedArc);
        var saved = PopupSave(working)!;

        // 5-turn beat: completed 2 turns (38, 39), current turn 40 = turn 3 of 5, began at Turn 38.
        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, saved, turnsInCurrentBeat: 2, turnIndex: 40);

        Assert.Contains("HARD CONSTRAINT — Span: ExtendedArc.", text);
        Assert.Contains("This moment spans 5 turns, which began at Turn 38.", text);
        Assert.Contains("You are on turn 3 of 5 — develop it further.", text);
    }

    [Fact]
    public async Task SelectSpanMoment_Prompt_ResolveNow()
    {
        var working = PopupSelectSpan(new ContinuationOverride(), SceneSpan.Moment);
        var saved = PopupSave(working)!;

        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, saved);

        Assert.Contains("HARD CONSTRAINT — Span: Moment.", text);
        Assert.Contains("This moment lasts a single turn — resolve it now.", text);
    }

    [Fact]
    public async Task NoOverride_ThemeDefault_TempoAndSpanReflectTheme()
    {
        // Popup "Done" with an empty working copy → null override → theme default direction stays.
        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, PopupSave(new ContinuationOverride()));

        Assert.Contains("HARD CONSTRAINT — Tempo: Steady.", text);
        Assert.Contains("HARD CONSTRAINT — Span: Scene.", text);
    }

    // ── C. Position / variant exclusions ──

    [Theory]
    [InlineData(SceneTempo.Linger)]
    [InlineData(SceneTempo.Steady)]
    [InlineData(SceneTempo.Push)]
    [InlineData(SceneTempo.Leap)]
    public async Task SelectTempo_SubsequentActor_GetsPaceLine_NoSpan(SceneTempo tempo)
    {
        var working = PopupSelectTempo(new ContinuationOverride(), tempo);
        var saved = PopupSave(working)!;

        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 2, saved);

        // D-1: only the first actor sets the pace — position 2+ get the tempo-independent line,
        // never the Tempo value, and never a Span directive.
        Assert.Contains("HARD CONSTRAINT — Subsequent actor: The first actor has set the pace — continue the beat at that pace", text);
        Assert.DoesNotContain("HARD CONSTRAINT — Tempo:", text);
        Assert.DoesNotContain("HARD CONSTRAINT — Span", text);
    }

    [Fact]
    public async Task SelectTempoAndSpan_NarrativeVariant_GetsNeitherTempoNorSpan()
    {
        var working = PopupSelectTempo(PopupSelectSpan(new ContinuationOverride(), SceneSpan.Scene), SceneTempo.Push);
        var saved = PopupSave(working)!;

        var text = await PromptForAsync(PromptVariant.Narrative, positionInTurn: 1, saved);

        Assert.DoesNotContain("HARD CONSTRAINT — Tempo", text);
        Assert.DoesNotContain("HARD CONSTRAINT — Span", text);
    }

    // ── D. Full chain: Tempo + Span together resolve coherently ──

    [Fact]
    public async Task SelectTempoPush_AndSpanScene_ResolvesCoherently_AndPromptEmitsBoth()
    {
        var working = PopupSelectTempo(PopupSelectSpan(new ContinuationOverride(), SceneSpan.Scene), SceneTempo.Push);
        var saved = PopupSave(working)!;

        var effective = ResolveEffectiveDirection(saved);
        Assert.Equal(ScenePacing.Fast, effective.Pacing);
        Assert.Equal(TimeShiftPolicy.Medium, effective.TimeShift);
        Assert.Equal(NarrativeGranularity.Meso, effective.Granularity);
        Assert.Equal(BeatScope.Short, effective.BeatScope);
        Assert.Equal(SceneTempo.Push, effective.Tempo);
        Assert.Equal(SceneSpan.Scene, effective.Span);

        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, saved, turnsInCurrentBeat: 0, turnIndex: 41);
        Assert.Contains("HARD CONSTRAINT — Tempo: Push.", text);
        Assert.Contains("HARD CONSTRAINT — Span: Scene.", text);
        Assert.Contains("This moment spans 3 turns, beginning this turn (Turn 41).", text);
    }

    [Fact]
    public async Task RawFieldOverride_WithoutTempo_StillApplies_BackwardCompatible()
    {
        // Power-user "Advanced" path: raw field alone, no Tempo. Must still resolve + prompt.
        var ov = new ContinuationOverride { Pacing = ScenePacing.Fast };

        var effective = ResolveEffectiveDirection(ov);
        Assert.Equal(ScenePacing.Fast, effective.Pacing);
        Assert.Equal(SceneTempo.Push, effective.Tempo);

        var text = await PromptForAsync(PromptVariant.Character, positionInTurn: 1, ov);
        Assert.Contains("HARD CONSTRAINT — Tempo: Push.", text);
    }
}
