using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// Verifies FinalInstructionSlot (Slot 17) renders the active theme machine's continuity
/// obligations ("Theme Machine Continuity") for Character prompts, and stays dormant when
/// no machine snapshot exists or when the variant is Narrative.
/// </summary>
public sealed class FinalInstructionSlotTests
{
    private static PromptBuildContext CreateContext(
        PromptVariant variant,
        ThemeMachineSessionSnapshot? snapshot)
    {
        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            ScenarioId = "test-scenario",
            PersonaName = "Ken",
            PersonaDescription = "A traveler.",
            PersonaRole = "Hero",
            MaxPromptChars = 35000,
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = NarrativePhase.Committed,
                CurrentSceneLocation = "The Cabin",
                ThemeMachineSnapshot = snapshot,
            },
        };

        var actorProfile = new ActorProfile
        {
            Kind = variant == PromptVariant.Narrative ? ActorProfileKind.Narrative : ActorProfileKind.Player,
            ActorName = variant == PromptVariant.Narrative ? "Narrator" : "Ken",
            ActorRole = variant == PromptVariant.Narrative ? "Narrator" : "Hero",
            PerspectiveMode = CharacterPerspectiveMode.FirstPersonInternalMonologue,
            PresentCharacterIds = [],
            AllCharacterIds = [],
        };

        return new PromptBuildContext
        {
            Session = session,
            ActorProfile = actorProfile,
            Variant = variant,
            Phase = "Committed",
            TurnIndex = 3,
            PositionInTurn = 1,
            TurnActorCount = 2,
            PromptText = "Continue naturally.",
            MaxPromptChars = 35000,
            WorldState = null,
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
                ProseStyleDirective = "Test prose style.",
                VoiceDirective = "Test voice.",
                ToneDirective = "Test tone.",
                FocusDirective = "Test focus.",
                HeatLevelDirective = "Test heat.",
            },
            WritingStyle = new ResolvedWritingStyleData
            {
                Example = "Style example",
                PhaseRuleOfThumb = "Phase RoT",
                StyleHint = "Hint",
                ImmersionDirective = "Stay in character.",
                ActionDirective = "Respond naturally.",
                WordTargetMin = 200,
                WordTargetMax = 400,
                NarrativeWordTargetMin = 300,
                NarrativeWordTargetMax = 500,
            },
            NarrativeTone = new ResolvedNarrativeToneData(),
            EncounterSummaries = [],
            RecentInteractions = [],
            CharacterDetails = null,
        };
    }

    private static ThemeMachineSessionSnapshot ReturnBeatSnapshot() => new()
    {
        MachineKey = "infidelity-brief-disappearance",
        ThemeId = "theme-1",
        DefinitionId = "definition-1",
        DefinitionVersion = 1,
        CurrentStateCode = "ReturnBeatRequired",
    };

    [Fact]
    public async Task WriteAsync_CharacterVariant_RendersReturnBeatMachineContinuity()
    {
        var slot = new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance);
        var context = CreateContext(PromptVariant.Character, ReturnBeatSnapshot());

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Theme Machine Continuity:", text, StringComparison.Ordinal);
        Assert.Contains("Current State: ReturnBeatRequired", text, StringComparison.Ordinal);
        Assert.Contains("Return beat is required", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_CharacterVariant_RendersReintegrationCooldownSignals()
    {
        var slot = new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance);
        var context = CreateContext(PromptVariant.Character, new ThemeMachineSessionSnapshot
        {
            MachineKey = "infidelity-brief-disappearance",
            ThemeId = "theme-1",
            DefinitionId = "definition-1",
            DefinitionVersion = 1,
            CurrentStateCode = "ReintegrationCooldown",
            TurnsInCurrentState = 3,
            ReturnBeatCompleted = false,
        });

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.Contains("Current State: ReintegrationCooldown", text, StringComparison.Ordinal);
        Assert.Contains("Cooldown turns in current state: 3", text, StringComparison.Ordinal);
        Assert.Contains("Return beat completed: no", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_CharacterVariant_NoMachineContinuity_WhenNoSnapshot()
    {
        var slot = new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance);
        var context = CreateContext(PromptVariant.Character, snapshot: null);

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.DoesNotContain("Theme Machine Continuity", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_NarrativeVariant_NoMachineContinuity_EvenWithSnapshot()
    {
        var slot = new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance);
        var context = CreateContext(PromptVariant.Narrative, ReturnBeatSnapshot());

        var text = await slot.WriteAsync(context, CancellationToken.None);

        Assert.DoesNotContain("Theme Machine Continuity", text, StringComparison.Ordinal);
    }
}
