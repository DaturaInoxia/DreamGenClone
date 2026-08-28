using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// Verifies ContinuationOverrideSlot (Slot 21) renders only the otherwise-unconsumed
/// scene-direction dimensions (Beat Style, Time Shift, Granularity, Scene Presence) when an
/// explicit override is present, and stays dormant otherwise.
/// </summary>
public sealed class ContinuationOverrideSlotTests
{
    private readonly ContinuationOverrideSlot _slot = new(NullLogger<ContinuationOverrideSlot>.Instance);

    private static PromptBuildContext CreateContext(ContinuationOverride? ov) => new()
    {
        Session = new RolePlaySession { Id = Guid.NewGuid().ToString() },
        ActorProfile = new ActorProfile
        {
            Kind = ActorProfileKind.Player,
            ActorName = "Ken",
            ActorRole = "Hero",
            PresentCharacterIds = [],
            AllCharacterIds = [],
        },
        Variant = PromptVariant.Character,
        Phase = "Committed",
        TurnIndex = 3,
        PositionInTurn = 1,
        TurnActorCount = 2,
        PromptText = "Continue naturally.",
        MaxPromptChars = 35000,
        Scenario = new ResolvedScenarioData
        {
            ScenarioId = "s",
            Name = "n",
            Description = "d",
            PlotDescription = "p",
            WorldDescription = "w",
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
        Override = ov,
    };

    [Fact]
    public void ShouldWrite_False_WhenOverrideNull()
    {
        Assert.False(_slot.ShouldWrite(CreateContext(null)));
    }

    [Fact]
    public void ShouldWrite_False_WhenOnlyPacingOrWordCountSet()
    {
        Assert.False(_slot.ShouldWrite(CreateContext(new ContinuationOverride { Pacing = ScenePacing.Fast })));
        Assert.False(_slot.ShouldWrite(CreateContext(new ContinuationOverride { WordTargetMin = 500, WordTargetMax = 900 })));
    }

    [Fact]
    public void ShouldWrite_False_EvenWhenUnconsumedDimensionSet()
    {
        // B-085: slot retired — dimensions now render from resolved SceneDirection in Slot 17.
        Assert.False(_slot.ShouldWrite(CreateContext(new ContinuationOverride { BeatScope = BeatScope.Extended })));
        Assert.False(_slot.ShouldWrite(CreateContext(new ContinuationOverride { TimeShift = TimeShiftPolicy.Large })));
        Assert.False(_slot.ShouldWrite(CreateContext(new ContinuationOverride { Granularity = NarrativeGranularity.Macro })));
    }

    [Fact]
    public async Task WriteAsync_Empty_EvenWhenUnconsumedDimensionsSet()
    {
        // B-085: slot retired — content moved to FinalInstructionSlot (Slot 17).
        var text = await _slot.WriteAsync(
            CreateContext(new ContinuationOverride
            {
                Pacing = ScenePacing.Fast,
                BeatScope = BeatScope.Extended,
                Granularity = NarrativeGranularity.Macro,
            }),
            CancellationToken.None);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task WriteAsync_Empty_WhenOnlyConsumedDimensionsSet()
    {
        var text = await _slot.WriteAsync(
            CreateContext(new ContinuationOverride { Pacing = ScenePacing.Fast }),
            CancellationToken.None);

        Assert.Equal(string.Empty, text);
    }
}
