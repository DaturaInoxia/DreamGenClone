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
                LocationNames = [],
                DefaultSteeringProfileId = null,
                DefaultIntensityProfileId = null,
                DefaultStartingLocationName = null,
            },
            Theme = new ResolvedThemeData(),
            Intensity = new ResolvedIntensityData(),
            WritingStyle = new ResolvedWritingStyleData
            {
                Description = "Style desc",
                Example = "Style example",
                ProfileDefaultRuleOfThumb = "Default RoT",
                PhaseRuleOfThumb = "Phase RoT",
                StyleHint = "Hint",
            },
            EncounterSummaries = [],
            RecentInteractions = [],
            CharacterDetails = null,
        };
    }

    // ── T028: End-to-end Character-variant build ───────────────

    [Fact]
    public async Task BuildAsync_CharacterVariant_ZoneAOrdering_NoLegacyHeader()
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

        // Zone A opens with scene grounding, not legacy header.
        Assert.DoesNotContain("You are continuing", prompt);
        Assert.DoesNotContain("interactive role-play scene", prompt);

        // Zone A content appears before Zone B content.
        var sceneAnchorIndex = prompt.IndexOf("Current scene:", StringComparison.Ordinal);
        var actorIndex = prompt.IndexOf("Continue as:", StringComparison.Ordinal);
        var turnContextIndex = prompt.IndexOf("Turn Context:", StringComparison.Ordinal);
        var locationLockIndex = prompt.IndexOf("HARD CONSTRAINT", StringComparison.Ordinal);
        var charDataIndex = prompt.IndexOf("POV Persona", StringComparison.Ordinal);

        Assert.True(sceneAnchorIndex >= 0, "Scene anchor missing");
        Assert.True(actorIndex >= 0, "Actor assignment missing");
        Assert.True(turnContextIndex >= 0, "Turn context missing");
        Assert.True(locationLockIndex >= 0, "Location lock missing");
        Assert.True(charDataIndex >= 0, "Character data missing");

        // Zone A slots appear before Zone B (CharacterData).
        Assert.True(sceneAnchorIndex < charDataIndex, "Scene anchor should be before character data");
        Assert.True(actorIndex < charDataIndex, "Actor assignment should be before character data");
        Assert.True(turnContextIndex < charDataIndex, "Turn context should be before character data");
        Assert.True(locationLockIndex < charDataIndex, "Location lock should be before character data");
    }

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

    [Fact]
    public async Task BuildAsync_EachContentCategoryAppearsExactlyOnce()
    {
        var theme = new RPTheme
        {
            Id = "t1",
            Label = "Temptation",
            Description = "Forbidden desire.",
            AIGenerationNotes = new List<RPThemeAIGuidanceNote>
            {
                new() { Section = RPThemeAIGuidanceSection.KeyScenarioElement, Text = "Eye contact matters." }
            },
        };

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
            Theme = new ResolvedThemeData
            {
                ActiveTheme = theme,
                PhaseGuidanceLines = new List<string> { "Build tension." },
            },
        };

        var prompt = await builder.BuildAsync(context, CancellationToken.None);

        // Each category must appear exactly once.
        var themeContractCount = CountOccurrences(prompt, "Theme Contract:");
        Assert.Equal(1, themeContractCount);

        var writingInstructionCount = CountOccurrences(prompt, "Writing Instruction:");
        Assert.Equal(1, writingInstructionCount);

        var turnContextCount = CountOccurrences(prompt, "Turn Context:");
        Assert.Equal(1, turnContextCount);

        // No legacy header.
        Assert.DoesNotContain("You are continuing", prompt);
    }

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
    public async Task BuildAsync_BudgetEnforcement_TrimsWhenTight()
    {
        // Budget tight enough that trimmable slots get trimmed but never-trim survive.
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
            MaxPromptChars = 1500,
            Theme = new ResolvedThemeData
            {
                ActiveTheme = new RPTheme { Id = "t1", Label = "Test", Description = "Testing." },
            },
        };

        var prompt = await builder.BuildAsync(context, CancellationToken.None);

        // Never-trim Zone A and Zone C content must survive.
        Assert.Contains("Current scene:", prompt);

        // Never-trim Zone C content must survive.
        Assert.Contains("Writing Instruction:", prompt);

        // Budget enforced.
        Assert.True(prompt.Length <= 1500,
            $"Prompt length {prompt.Length} exceeds budget of 1500");
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

    [Fact]
    public async Task BuildAsync_TieredHistory_ShowsCorrectTierBoundaries()
    {
        // Build 16 interactions spanning multiple "turns".
        var interactions = new List<RolePlayInteraction>();
        for (int i = 1; i <= 16; i++)
        {
            interactions.Add(new RolePlayInteraction
            {
                Id = $"ixn-{i}",
                ActorName = i % 2 == 0 ? "Becky" : "Dean",
                Content = $"Turn {i} interaction content with sufficient detail for testing tiered compression boundaries.",
                IsExcluded = false,
            });
        }

        var slots = new List<IPromptSlot>
        {
            new SceneAnchorSlot(NullLogger<SceneAnchorSlot>.Instance),
            new ActorAssignmentSlot(NullLogger<ActorAssignmentSlot>.Instance),
            new TurnContextSlot(NullLogger<TurnContextSlot>.Instance),
            new SceneLocationLockSlot(NullLogger<SceneLocationLockSlot>.Instance),
            new CharacterDataSlot(NullLogger<CharacterDataSlot>.Instance),
            new InteractionHistorySlot(NullLogger<InteractionHistorySlot>.Instance),
            new ThemeContractSlot(NullLogger<ThemeContractSlot>.Instance),
            new FinalInstructionSlot(NullLogger<FinalInstructionSlot>.Instance),
        };

        var builder = CreateBuilder(slots);
        var context = CreateCharacterContext(turnIndex: 16);
        var session = context.Session;
        session.HistoryFullDetailTurnBand = 3;
        session.HistoryNarrativeOnlyTurnBand = 3;
        session.ContextWindowTurns = 8;
        context = context with
        {
            Session = session,
            RecentInteractions = interactions,
            Theme = new ResolvedThemeData
            {
                ActiveTheme = new RPTheme { Id = "t1", Label = "Test", Description = "Testing." },
            },
        };

        var prompt = await builder.BuildAsync(context, CancellationToken.None);

        // Layer 1 (full detail): last 3 interactions should be in full.
        Assert.Contains("Turn 14", prompt);
        Assert.Contains("Turn 15", prompt);
        Assert.Contains("Turn 16", prompt);

        // Layer 2 (narrative-only): interactions 11-13 should be in compressed section.
        Assert.Contains("Earlier Interactions", prompt);

        // Zone A content must still be present (never trimmed).
        Assert.Contains("Current scene:", prompt);

        // No legacy header.
        Assert.DoesNotContain("You are continuing", prompt);
    }

    // ── T082: WorldStateSlot conditional omission ──────────────

    [Fact]
    public async Task WorldStateSlot_SilentlyOmittedWhenWorldStateIsNull()
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
        // WorldState defaults to null in CreateCharacterContext.

        var prompt = await builder.BuildAsync(context, CancellationToken.None);

        // Should not contain World State section.
        Assert.DoesNotContain("World State:", prompt);
        // Should still contain other Zone A content.
        Assert.Contains("Current scene:", prompt);
        Assert.Contains("Continue as:", prompt);
    }

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

