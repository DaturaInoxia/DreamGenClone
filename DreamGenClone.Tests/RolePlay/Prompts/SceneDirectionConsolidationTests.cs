using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// B-085/B-089: the consolidated "Scene Direction" block rendered by FinalInstructionSlot
/// (Slot 17) — Tempo (density, all positions) + Span (duration, lead actor only) — plus
/// the single-path Beat Style resolution.
/// </summary>
public sealed class SceneDirectionConsolidationTests
{
    private static PromptBuildContext CreateContext(
        PromptVariant variant,
        int? positionInTurn,
        SceneDirection sceneDirection,
        int turnsInCurrentBeat = 0,
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
            TurnIndex = 3,
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

    private static SceneDirection ShortDirection() => new()
    {
        Pacing = ScenePacing.Medium,
        BeatScope = BeatScope.Short,
        TimeShift = TimeShiftPolicy.Medium,
        Granularity = NarrativeGranularity.Meso,
    };

    private readonly FinalInstructionSlot _slot = new(NullLogger<FinalInstructionSlot>.Instance);

    [Fact]
    public async Task LeadActor_RendersTempoAndSpan()
    {
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, ShortDirection(), turnsInCurrentBeat: 1),
            CancellationToken.None);

        // ShortDirection: Pacing=Medium, BeatScope=Short, TimeShift=Medium, Granularity=Meso
        // → Tempo=Steady, Span=Scene.
        Assert.Contains("HARD CONSTRAINT — Tempo: Steady.", text);
        Assert.Contains("HARD CONSTRAINT — Span: Scene.", text);
        Assert.Contains("turn 2 of 3", text);
        // The old trio must be gone — no Pacing/TimeShift/Granularity HCs.
        Assert.DoesNotContain("HARD CONSTRAINT — Scene Pacing", text);
        Assert.DoesNotContain("HARD CONSTRAINT — Time Shift", text);
        Assert.DoesNotContain("HARD CONSTRAINT — Granularity", text);
    }

    [Fact]
    public async Task SubsequentActor_GetsPaceLine_OmitsTempoAndSpan()
    {
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 2, ShortDirection(), turnsInCurrentBeat: 1),
            CancellationToken.None);

        // B-094/D-1: only the first actor sets the pace — position 2+ get the tempo-independent
        // subsequent-actor line, never a Tempo value, and never a Span duration directive.
        Assert.Contains("HARD CONSTRAINT — Subsequent actor: The first actor has set the pace — continue the beat at that pace", text);
        Assert.DoesNotContain("HARD CONSTRAINT — Tempo:", text);
        Assert.DoesNotContain("HARD CONSTRAINT — Span", text);
    }

    [Fact]
    public async Task NarrativeVariant_OmitsSceneDirectionBlock()
    {
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Narrative, positionInTurn: null, ShortDirection(), turnsInCurrentBeat: 1),
            CancellationToken.None);

        Assert.DoesNotContain("HARD CONSTRAINT — Tempo", text);
        Assert.DoesNotContain("HARD CONSTRAINT — Span", text);
    }

    [Fact]
    public async Task EpisodicSheetActive_OmitsGenericSpan()
    {
        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, ShortDirection(), turnsInCurrentBeat: 2, currentBeatCode: "3b"),
            CancellationToken.None);

        Assert.DoesNotContain("HARD CONSTRAINT — Span", text);
        // Tempo still applies.
        Assert.Contains("HARD CONSTRAINT — Tempo: Steady.", text);
    }

    [Theory]
    [InlineData(BeatScope.Single, 0, "single turn")]
    [InlineData(BeatScope.Single, 5, "single turn")]
    [InlineData(BeatScope.Short, 0, "turn 1 of 3")]
    [InlineData(BeatScope.Short, 1, "turn 2 of 3")]
    [InlineData(BeatScope.Short, 2, "turn 3 of 3")]
    [InlineData(BeatScope.Extended, 0, "turn 1 of 5")]
    [InlineData(BeatScope.Extended, 2, "turn 3 of 5")]
    [InlineData(BeatScope.Extended, 4, "turn 5 of 5")]
    [InlineData(BeatScope.Extended, 5, "bring it to its climax")]
    public async Task RenderedSpan_DiffersByScopeAndPosition(BeatScope scope, int turnsInBeat, string expectedSubstring)
    {
        var dir = new SceneDirection
        {
            Pacing = ScenePacing.Medium,
            BeatScope = scope,
            TimeShift = TimeShiftPolicy.Medium,
            Granularity = NarrativeGranularity.Meso,
        };

        var text = await _slot.WriteAsync(
            CreateContext(PromptVariant.Character, positionInTurn: 1, dir, turnsInCurrentBeat: turnsInBeat),
            CancellationToken.None);

        var span = SceneDirection.SpanFrom(scope);
        Assert.Contains($"HARD CONSTRAINT — Span: {span}.", text);
        Assert.Contains(expectedSubstring, text);
    }

    [Fact]
    public void ResolveBeatScope_OverrideWinsWhenSet()
    {
        var baseDir = new SceneDirection { BeatScope = BeatScope.Short };
        var ov = new ContinuationOverride { BeatScope = BeatScope.Extended };
        Assert.Equal(BeatScope.Extended, ContinuationOverrideResolver.ResolveBeatScope(baseDir, ov));
    }

    [Fact]
    public void ResolveBeatScope_FallsBackToBaseWhenNoOverride()
    {
        var baseDir = new SceneDirection { BeatScope = BeatScope.Single };
        Assert.Equal(BeatScope.Single, ContinuationOverrideResolver.ResolveBeatScope(baseDir, null));
        Assert.Equal(BeatScope.Single, ContinuationOverrideResolver.ResolveBeatScope(baseDir, new ContinuationOverride()));
    }
}
