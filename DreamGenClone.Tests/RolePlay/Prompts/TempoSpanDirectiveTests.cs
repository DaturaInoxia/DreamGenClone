using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// B-089 — Tempo (density) + Span (duration) directives. Verifies the finalized §3.7
/// wording, the Tempo×Span reconciliation (Span wins on WHEN to conclude, Tempo wins on
/// HOW MUCH to compress), Tempo/Span override mapping, and the T8 fail-fast validation.
/// </summary>
public sealed class TempoSpanDirectiveTests
{
    private static PromptBuildContext CreateContext(
        PromptVariant variant,
        int? positionInTurn,
        SceneDirection sceneDirection,
        int turnsInCurrentBeat = 0,
        string? currentBeatCode = null,
        int? turnIndex = 3)
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
                SceneDirection = sceneDirection,
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

    private readonly FinalInstructionSlot _slot = new(NullLogger<FinalInstructionSlot>.Instance);

    // ── Tempo derivation (raw bundle → SceneTempo) ──

    [Fact]
    public void TempoFrom_Slow_IsLinger()
        => Assert.Equal(SceneTempo.Linger, SceneDirection.TempoFrom(ScenePacing.Slow, TimeShiftPolicy.None, NarrativeGranularity.Micro));

    [Fact]
    public void TempoFrom_Medium_IsSteady()
        => Assert.Equal(SceneTempo.Steady, SceneDirection.TempoFrom(ScenePacing.Medium, TimeShiftPolicy.Small, NarrativeGranularity.Meso));

    [Fact]
    public void TempoFrom_Fast_SmallShift_IsPush()
        => Assert.Equal(SceneTempo.Push, SceneDirection.TempoFrom(ScenePacing.Fast, TimeShiftPolicy.Medium, NarrativeGranularity.Meso));

    [Fact]
    public void TempoFrom_Fast_LargeShift_IsLeap()
        => Assert.Equal(SceneTempo.Leap, SceneDirection.TempoFrom(ScenePacing.Fast, TimeShiftPolicy.Large, NarrativeGranularity.Macro));

    [Fact]
    public void TempoBundle_RoundTrips_ThroughTempoFrom()
    {
        foreach (var tempo in new[] { SceneTempo.Linger, SceneTempo.Steady, SceneTempo.Push, SceneTempo.Leap })
        {
            var (pacing, timeShift, granularity) = SceneDirection.TempoBundle(tempo);
            Assert.Equal(tempo, SceneDirection.TempoFrom(pacing, timeShift, granularity));
        }
    }

    // ── Span mapping ──

    [Fact]
    public void Span_FromBeatScope_MapsCorrectly()
    {
        Assert.Equal(SceneSpan.Moment, SceneDirection.SpanFrom(BeatScope.Single));
        Assert.Equal(SceneSpan.Scene, SceneDirection.SpanFrom(BeatScope.Short));
        Assert.Equal(SceneSpan.ExtendedArc, SceneDirection.SpanFrom(BeatScope.Extended));
    }

    [Fact]
    public void SpanTurnBudget_MapsCorrectly()
    {
        Assert.Equal(1, SceneDirection.SpanTurnBudget(SceneSpan.Moment));
        Assert.Equal(3, SceneDirection.SpanTurnBudget(SceneSpan.Scene));
        Assert.Equal(5, SceneDirection.SpanTurnBudget(SceneSpan.ExtendedArc));
    }

    // ── Finalized Tempo wording (§3.7) ──

    [Theory]
    [InlineData(SceneTempo.Linger, "Stay in this exact moment")]
    [InlineData(SceneTempo.Steady, "Advance the scene by one beat, then stop")]
    [InlineData(SceneTempo.Push, "Advance through two to three beats this response")]
    [InlineData(SceneTempo.Leap, "Advance time by a day or more")]
    public async Task TempoDirective_LeadActor_EmitsFinalizedWording(SceneTempo tempo, string expectedFragment)
    {
        var dir = new SceneDirection
        {
            Pacing = SceneDirection.TempoBundle(tempo).Pacing,
            BeatScope = BeatScope.Short,
            TimeShift = SceneDirection.TempoBundle(tempo).TimeShift,
            Granularity = SceneDirection.TempoBundle(tempo).Granularity,
        };

        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir), CancellationToken.None);

        Assert.Contains($"HARD CONSTRAINT — Tempo: {tempo}.", text);
        Assert.Contains(expectedFragment, text);
    }

    // ── B-095: Linger must yield to Span on the final beat turn ──

    [Fact]
    public async Task Linger_FinalBeatTurn_EmitsConcludingVariant()
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Slow,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.None,
            Granularity = NarrativeGranularity.Micro,
        };

        // turnsInCurrentBeat=2 → beat position 3 of 3 (final) — Linger must conclude, not hold.
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 2), CancellationToken.None);

        Assert.Contains("HARD CONSTRAINT — Tempo: Linger.", text);
        Assert.Contains("final turn of this moment", text);
        Assert.Contains("conclude it now within the exact present", text);
        // The stay-in-moment hold line must not appear on the final beat turn.
        Assert.DoesNotContain("One response covers one moment, not a scene", text);
    }

    [Fact]
    public async Task Linger_NonFinalBeatTurn_UsesBaseWording()
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Slow,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.None,
            Granularity = NarrativeGranularity.Micro,
        };

        // turnsInCurrentBeat=0 → beat position 1 of 3 — base Linger hold wording applies.
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 0), CancellationToken.None);

        Assert.Contains("HARD CONSTRAINT — Tempo: Linger.", text);
        Assert.Contains("Stay in this exact moment", text);
        Assert.DoesNotContain("final turn of this moment", text);
    }

    [Theory]
    [InlineData(SceneTempo.Steady)]
    [InlineData(SceneTempo.Push)]
    [InlineData(SceneTempo.Leap)]
    public async Task Tempo_NonLinger_FinalTurn_Unchanged(SceneTempo tempo)
    {
        var dir = new SceneDirection
        {
            Pacing = SceneDirection.TempoBundle(tempo).Pacing,
            BeatScope = BeatScope.Short,
            TimeShift = SceneDirection.TempoBundle(tempo).TimeShift,
            Granularity = SceneDirection.TempoBundle(tempo).Granularity,
        };

        // Non-Linger tempos ignore the final-turn flag — same wording on turn 1 and turn 3.
        var final = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 2), CancellationToken.None);
        Assert.Contains($"HARD CONSTRAINT — Tempo: {tempo}.", final);
        Assert.DoesNotContain("final turn of this moment", final);
    }

    // ── Position 2+ builds on the first actor's pace — they do NOT set pace (B-094/D-1) ──

    [Theory]
    [InlineData(SceneTempo.Linger)]
    [InlineData(SceneTempo.Steady)]
    [InlineData(SceneTempo.Push)]
    [InlineData(SceneTempo.Leap)]
    public async Task TempoDirective_SubsequentActor_GetsPaceLine_NoTempoValue(SceneTempo tempo)
    {
        var dir = new SceneDirection
        {
            Pacing = SceneDirection.TempoBundle(tempo).Pacing,
            BeatScope = BeatScope.Short,
            TimeShift = SceneDirection.TempoBundle(tempo).TimeShift,
            Granularity = SceneDirection.TempoBundle(tempo).Granularity,
        };

        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 2, dir), CancellationToken.None);

        // D-1: only the first actor sets the pace. Position 2+ get ONE tempo-independent line
        // that continues the beat at that pace (no freeze, no tempo value).
        Assert.Contains("HARD CONSTRAINT — Subsequent actor: The first actor has set the pace — continue the beat at that pace from your character's perspective. Move it forward without speeding it up, skipping ahead, or restarting it.", text);
        // The original freeze ("do not advance time / introduce a new beat") must be gone — it
        // trapped the turn in place when combined with Linger.
        Assert.DoesNotContain("Do not advance time, change the pacing, or introduce a new beat", text);
        // The tempo value must not reach position 2+, regardless of the chosen Tempo.
        Assert.DoesNotContain("HARD CONSTRAINT — Tempo:", text);
        // Subsequent actors never receive a Span duration directive.
        Assert.DoesNotContain("HARD CONSTRAINT — Span", text);
    }

    // ── Tempo × Span reconciliation ──
    // Span wins on WHEN to conclude; Tempo wins on HOW MUCH to compress.

    [Fact]
    public async Task TempoPush_SpanScene_Turn1_SpanHoldsConclusion()
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Fast,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.Medium,
            Granularity = NarrativeGranularity.Meso,
        };

        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 0),
            CancellationToken.None);

        // Tempo=Push: compress within the moment, but Span turn 1 of 3 says do NOT conclude.
        Assert.Contains("HARD CONSTRAINT — Tempo: Push.", text);
        Assert.Contains("HARD CONSTRAINT — Span: Scene.", text);
        Assert.Contains("You are on turn 1 of 3 — establish it only. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.", text);
    }

    [Fact]
    public async Task TempoPush_SpanScene_FinalTurn_Aligned()
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Fast,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.Medium,
            Granularity = NarrativeGranularity.Meso,
        };

        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 2),
            CancellationToken.None);

        Assert.Contains("HARD CONSTRAINT — Tempo: Push.", text);
        Assert.Contains("HARD CONSTRAINT — Span: Scene.", text);
        Assert.Contains("bring it to its climax or conclusion now and move on.", text);
    }

    // ── Span ↔ Interaction History turn linkage (B-090) ──
    // The Span directive names the beat's absolute history-turn anchor so the model can map
    // "turn X of Y" back onto the numbered turns in Interaction History. TurnIndex=3 is the
    // absolute turn about to be written; beat start = TurnIndex - turnsInCurrentBeat.

    [Fact]
    public async Task SpanScene_Turn1_AnchorsAbsoluteBeatStart()
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Medium,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.Small,
            Granularity = NarrativeGranularity.Meso,
        };

        // turnsInCurrentBeat=0 → this is beat position 1, so the beat begins at Turn 3.
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 0),
            CancellationToken.None);

        Assert.Contains("HARD CONSTRAINT — Span: Scene.", text);
        Assert.Contains("This moment spans 3 turns, beginning this turn (Turn 3).", text);
    }

    [Fact]
    public async Task SpanScene_MiddleTurn_AnchorsAbsoluteBeatStart()
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Medium,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.Small,
            Granularity = NarrativeGranularity.Meso,
        };

        // turnsInCurrentBeat=1 → beat position 2; beat began at Turn 2 (3 - 1).
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 1),
            CancellationToken.None);

        Assert.Contains("This moment spans 3 turns, which began at Turn 2.", text);
        Assert.Contains("You are on turn 2 of 3 — develop it further.", text);
    }

    [Fact]
    public async Task SpanScene_FinalTurn_AnchorsAbsoluteBeatStart()
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Medium,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.Small,
            Granularity = NarrativeGranularity.Meso,
        };

        // turnsInCurrentBeat=2 → beat position 3 (final); beat began at Turn 1 (3 - 2).
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 2),
            CancellationToken.None);

        Assert.Contains("This moment ends this turn (turn 3 of 3, which began at Turn 1)", text);
        Assert.Contains("bring it to its climax or conclusion now and move on.", text);
    }

    [Fact]
    public async Task SpanScene_NoAbsoluteTurn_FallsBackToRelativeWording()
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Medium,
            BeatScope = BeatScope.Short,
            TimeShift = TimeShiftPolicy.Small,
            Granularity = NarrativeGranularity.Meso,
        };

        // No TurnIndex on the context (single-actor fallback path) → relative-only wording,
        // no absolute turn anchor named.
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: 0, turnIndex: null),
            CancellationToken.None);

        Assert.Contains("This moment spans 3 turns. You are on turn 1 of 3 — establish it only.", text);
        Assert.DoesNotContain("(Turn 3)", text);
        Assert.DoesNotContain("which began at Turn", text);
    }

    // ── Tempo override maps to a coherent raw bundle (T4) ──

    [Fact]
    public void ApplySceneDirectionOverride_TempoOverride_SetsCoherentBundle()
    {
        var baseDir = new SceneDirection { Pacing = ScenePacing.Medium, BeatScope = BeatScope.Short, TimeShift = TimeShiftPolicy.Small, Granularity = NarrativeGranularity.Meso };

        var result = ContinuationOverrideResolver.ApplySceneDirectionOverride(
            baseDir,
            new ContinuationOverride { Tempo = SceneTempo.Leap });

        Assert.Equal(ScenePacing.Fast, result.Pacing);
        Assert.Equal(TimeShiftPolicy.Large, result.TimeShift);
        Assert.Equal(NarrativeGranularity.Macro, result.Granularity);
        Assert.Equal(SceneTempo.Leap, result.Tempo);
    }

    [Fact]
    public void ApplySceneDirectionOverride_SpanOverride_SetsBeatScope()
    {
        var baseDir = new SceneDirection { Pacing = ScenePacing.Medium, BeatScope = BeatScope.Short, TimeShift = TimeShiftPolicy.Small, Granularity = NarrativeGranularity.Meso };

        var result = ContinuationOverrideResolver.ApplySceneDirectionOverride(
            baseDir,
            new ContinuationOverride { Span = SceneSpan.ExtendedArc });

        Assert.Equal(BeatScope.Extended, result.BeatScope);
        Assert.Equal(SceneSpan.ExtendedArc, result.Span);
    }

    [Fact]
    public void ResolveBeatScope_SpanOverride_MapsToBeatScope()
    {
        var baseDir = new SceneDirection { BeatScope = BeatScope.Short };
        var ov = new ContinuationOverride { Span = SceneSpan.Moment };
        Assert.Equal(BeatScope.Single, ContinuationOverrideResolver.ResolveBeatScope(baseDir, ov));
    }

    // ── T8: fail-fast on contradictory Tempo + raw-field override ──

    [Fact]
    public void ValidateCoherentOverride_TempoPlusContradictoryRaw_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContinuationOverrideResolver.ValidateCoherentOverride(
                new ContinuationOverride { Tempo = SceneTempo.Linger, Pacing = ScenePacing.Fast }));
        Assert.Contains("ContinuationOverrideConflict", ex.Message);
    }

    [Fact]
    public void ValidateCoherentOverride_TempoAlone_DoesNotThrow()
    {
        ContinuationOverrideResolver.ValidateCoherentOverride(new ContinuationOverride { Tempo = SceneTempo.Push });
    }

    [Fact]
    public void ValidateCoherentOverride_TempoPlusMatchingRaw_DoesNotThrow()
    {
        // Linger = Slow + None + Micro — matching raw fields are fine.
        ContinuationOverrideResolver.ValidateCoherentOverride(new ContinuationOverride
        {
            Tempo = SceneTempo.Linger,
            Pacing = ScenePacing.Slow,
            TimeShift = TimeShiftPolicy.None,
            Granularity = NarrativeGranularity.Micro,
        });
    }

    [Fact]
    public void ValidateCoherentOverride_NoTempo_DoesNotThrow()
    {
        ContinuationOverrideResolver.ValidateCoherentOverride(new ContinuationOverride { Pacing = ScenePacing.Fast });
        ContinuationOverrideResolver.ValidateCoherentOverride(null);
    }
}
