using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// End-to-end tests for RolePlayPromptBuilder verifying Zone A ordering,
/// zone separation, and absence of legacy "You are continuing..." header.
/// </summary>
public sealed class PromptBuilderTests
{
    private static RolePlayPromptBuilder CreateBuilder(IEnumerable<IPromptSlot> slots)
    {
        var enforcer = new PromptBudgetEnforcer(NullLogger<PromptBudgetEnforcer>.Instance);
        var logger = NullLogger<RolePlayPromptBuilder>.Instance;
        return new RolePlayPromptBuilder(slots, enforcer, logger);
    }

    private static IReadOnlyList<RecentInteractionEntry> BuildTurnEntries(
        IReadOnlyList<RolePlayInteraction> interactions, int startTurn)
    {
        var entries = new List<RecentInteractionEntry>();
        for (int i = 0; i < interactions.Count; i++)
        {
            var turnNum = startTurn + (i / 2);
            entries.Add(new RecentInteractionEntry
            {
                Interaction = interactions[i],
                TurnNumber = turnNum,
                PositionInTurn = (i % 2) + 1,
                TurnActorCount = 2,
            });
        }
        return entries;
    }

    private static PromptBuildContext CreateCharacterContext(
        string phase = "BuildUp",
        string location = "The Cabin",
        int turnIndex = 3,
        int positionInTurn = 1,
        int turnActorCount = 2)
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
                CurrentPhase = NarrativePhase.BuildUp,
                CurrentSceneLocation = location,
            },
        };

        var characters = new List<ScenarioCharacter>
        {
            new("c1", "Becky", "wife"),
            new("c2", "Dean", "husband"),
        };

        var actorProfile = new ActorProfile
        {
            Kind = ActorProfileKind.Player,
            ActorName = "Ken",
            ActorRole = "Hero",
            PerspectiveMode = CharacterPerspectiveMode.FirstPersonInternalMonologue,
            PresentCharacterIds = characters.Select(c => c.Id).ToList(),
            AllCharacterIds = characters.Select(c => c.Id).ToList(),
        };

        return new PromptBuildContext
        {
            Session = session,
            ActorProfile = actorProfile,
            Variant = PromptVariant.Character,
            Phase = phase,
            TurnIndex = turnIndex,
            PositionInTurn = positionInTurn,
            TurnActorCount = turnActorCount,
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
                Characters = characters,
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
            PinnedInteractions = [], StagedInteractions = [],
            CharacterDetails = null,
        };
    }

    // ── T028: End-to-end Character-variant build ───────────────

    [Fact]
    public async Task BuildAsync_FailsFast_WhenMaxPromptCharsIsZero()
    {
        var slots = new List<IPromptSlot>
        {
            new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance),
        };
        var builder = CreateBuilder(slots);
        var context = CreateCharacterContext();
        context = context with { MaxPromptChars = 0 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(context, CancellationToken.None));

        Assert.Contains("MaxPromptChars", ex.Message);
        Assert.Contains("FR-004", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_PromptNotEmpty_WithAllSlots()
    {
        var slots = new List<IPromptSlot>
        {
            new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance),
            new ActorAssignmentSlot(NullLogger<ActorAssignmentSlot>.Instance),
            new TurnContextSlot(NullLogger<TurnContextSlot>.Instance),
            new SceneLocationLockSlot(NullLogger<SceneLocationLockSlot>.Instance),
            new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance),
        };

        var builder = CreateBuilder(slots);
        var context = CreateCharacterContext();

        var prompt = await builder.BuildAsync(context, CancellationToken.None);

        Assert.NotEmpty(prompt);
        Assert.True(prompt.Length > 0);
    }

    // ── T042: Deduplication — each content category appears exactly once (FR-027, SC-002) ──

    // ── T049: Narrative-variant end-to-end test (SC-004) ───────

    [Fact]
    public async Task BuildAsync_NarrativeVariant_NoPOVPersona_ZeroDialogueConstraint()
    {
        var slots = new List<IPromptSlot>
        {
            new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance),
            new ActorAssignmentSlot(NullLogger<ActorAssignmentSlot>.Instance),
            new TurnContextSlot(NullLogger<TurnContextSlot>.Instance),
            new SceneLocationLockSlot(NullLogger<SceneLocationLockSlot>.Instance),
            new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance),
            new ThemeContractSlot(NullLogger<ThemeContractSlot>.Instance),
            new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance),
        };

        var builder = CreateBuilder(slots);
        var context = CreateCharacterContext();
        context = context with
        {
            Variant = PromptVariant.Narrative,
            ActorProfile = context.ActorProfile with
            {
                Kind = ActorProfileKind.Narrative,
                ActorName = "omniscient narrator",
                ActorRole = "narrator",
            },
        };

        var prompt = await builder.BuildAsync(context, CancellationToken.None);

        // SC-004: No POV Persona text.
        Assert.DoesNotContain("POV Persona", prompt);

        // Zero-dialogue constraint present.
        Assert.Contains("Zero dialogue", prompt);

        // Physical detail checklist present.
        Assert.Contains("Physical Detail Checklist", prompt);

        // Omniscient narrator assignment present.
        Assert.Contains("omniscient narrator", prompt);

        // No legacy header.
        Assert.DoesNotContain("You are continuing", prompt);
    }

    // ── T062: Budget enforcement E2E — cap holds ────────────────

    [Fact]
    public async Task BuildAsync_BudgetEnforcement_CapHolds()
    {
        // Simulate a heavy context that stays under budget.
        var slots = new List<IPromptSlot>
        {
            new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance),
            new ActorAssignmentSlot(NullLogger<ActorAssignmentSlot>.Instance),
            new TurnContextSlot(NullLogger<TurnContextSlot>.Instance),
            new SceneLocationLockSlot(NullLogger<SceneLocationLockSlot>.Instance),
            new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance),
        };

        var builder = CreateBuilder(slots);
        var context = CreateCharacterContext();
        // Generous budget — should pass without trimming.
        context = context with { MaxPromptChars = 2000 };

        var prompt = await builder.BuildAsync(context, CancellationToken.None);

        Assert.NotEmpty(prompt);
        Assert.True(prompt.Length <= 2000,
            $"Prompt length {prompt.Length} exceeds budget of 2000");
    }

    [Fact]
    public async Task BuildAsync_BudgetEnforcement_FailsFast_OnMissingMaxPromptChars()
    {
        var slots = new List<IPromptSlot>
        {
            new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance),
        };
        var builder = CreateBuilder(slots);
        var context = CreateCharacterContext();
        context = context with { MaxPromptChars = 0 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(context, CancellationToken.None));

        Assert.Contains("MaxPromptChars", ex.Message);
        Assert.Contains("FR-004", ex.Message);
    }

    private static int CountOccurrences(string text, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    // ── T074: Tiered-history end-to-end (15+ turn session) ──────

    // ── T082: WorldStateSlot conditional omission ──────────────

    [Fact]
    public async Task WorldStateSlot_FiresWhenWorldStateIsPopulated()
    {
        var slots = new List<IPromptSlot>
        {
            new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance),
            new WorldStateSlot(),
            new ActorAssignmentSlot(NullLogger<ActorAssignmentSlot>.Instance),
            new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance),
        };

        var builder = CreateBuilder(slots);
        var context = CreateCharacterContext();
        context = context with
        {
            WorldState = new WorldStateData
            {
                DayNumber = 1,
                WeatherCondition = "Sunny",
                TemperatureCelsius = 25,
            },
        };

        var prompt = await builder.BuildAsync(context, CancellationToken.None);

        Assert.Contains("World State:", prompt);
        Assert.Contains("Day 1", prompt);
        Assert.Contains("Sunny", prompt);
    }
}

