using System.Text;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests that CharacterStatStateTexts is correctly derived from CharacterRuntimeStats
/// and injected into prompts by ScenarioGuidanceContextFactory and RolePlayAssistantPrompts.
/// </summary>
public sealed class BehavioralFrameWithRuntimeStatsTests
{
    // ── ScenarioGuidanceContextFactory: stat state text from runtime stats ────────────────────

    [Fact]
    public async Task CreateAsync_WithNonNeutralRuntimeStats_PopulatesCharacterStatStateTexts()
    {
        // Arrange: Wife character with Desire = 85 (Band4, non-neutral) and Restraint = 50 (neutral)
        var wifeProfile = CharacterStatProfileV2Accessor.CreateDefault("c-wife");
        CharacterStatProfileV2Accessor.SetStat(wifeProfile, "Desire", 85);
        CharacterStatProfileV2Accessor.SetStat(wifeProfile, "Restraint", 50); // neutral → no text

        var characters = new List<ScenarioCharacter>
        {
            new("c-wife", "Sarah", "Wife")
        };

        var runtimeStats = new Dictionary<string, CharacterStatProfileV2>
        {
            ["c-wife"] = wifeProfile
        };

        var factory = new ScenarioGuidanceContextFactory(new NoOpBehavioralFrameGenerator());
        var input = new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "BuildUp",
            ActiveScenarioId: null,
            VariantId: null,
            AverageDesire: 85,
            AverageRestraint: 50,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            CharacterEncounterProfileIds: new Dictionary<string, string>(),
            Characters: characters,
            SuppressedScenarioIds: [],
            CharacterRuntimeStats: runtimeStats);

        // Act
        var context = await factory.CreateAsync(input);

        // Assert: Wife should have a stat state text entry (Desire is non-neutral)
        Assert.True(context.CharacterStatStateTexts.ContainsKey("Sarah (Wife)")
            || context.CharacterStatStateTexts.ContainsKey("c-wife"),
            "Expected stat state text entry for the Wife character");
    }

    [Fact]
    public async Task CreateAsync_WithAllNeutralRuntimeStats_StillInjectsStatStateTexts()
    {
        // Arrange: Wife character with all stats at neutral value 50.
        // Neutral values are now injected (no neutral-band skip).
        var wifeProfile = CharacterStatProfileV2Accessor.CreateDefault("c-wife");
        foreach (var stat in AdaptiveStatCatalog.CanonicalStatNames)
            CharacterStatProfileV2Accessor.SetStat(wifeProfile, stat, 50);

        var characters = new List<ScenarioCharacter>
        {
            new("c-wife", "Sarah", "Wife")
        };

        var runtimeStats = new Dictionary<string, CharacterStatProfileV2>
        {
            ["c-wife"] = wifeProfile
        };

        var factory = new ScenarioGuidanceContextFactory(new NoOpBehavioralFrameGenerator());
        var input = new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "BuildUp",
            ActiveScenarioId: null,
            VariantId: null,
            AverageDesire: 50,
            AverageRestraint: 50,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            CharacterEncounterProfileIds: new Dictionary<string, string>(),
            Characters: characters,
            SuppressedScenarioIds: [],
            CharacterRuntimeStats: runtimeStats);

        // Act
        var context = await factory.CreateAsync(input);

        // Assert: neutral stats are now injected
        Assert.True(context.CharacterStatStateTexts.ContainsKey("Sarah (Wife)")
            || context.CharacterStatStateTexts.ContainsKey("c-wife"),
            "Expected stat state text entry for the Wife character with all-neutral stats");
    }

    [Fact]
    public async Task CreateAsync_WithNullRuntimeStats_CharacterStatStateTextsIsEmpty()
    {
        var factory = new ScenarioGuidanceContextFactory(new NoOpBehavioralFrameGenerator());
        var input = new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "BuildUp",
            ActiveScenarioId: null,
            VariantId: null,
            AverageDesire: 50,
            AverageRestraint: 50,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            CharacterEncounterProfileIds: new Dictionary<string, string>(),
            Characters: [],
            SuppressedScenarioIds: []);

        var context = await factory.CreateAsync(input);

        Assert.Empty(context.CharacterStatStateTexts);
    }

    // ── AppendScenarioGuidance: HARD CONSTRAINT injected for stat state texts ─────────────────

    [Fact]
    public void AppendScenarioGuidance_WithStatStateTexts_InjectsHardConstraint()
    {
        // Arrange: a context with one behavioral frame and one stat state text for the same label
        var builder = new StringBuilder();
        var guidance = new ScenarioGuidanceContext(
            Phase: "Committed",
            ActiveScenarioId: "seduction",
            GuidanceText: "Maintain seduction arc",
            ExcludedScenarioIds: [],
            CharacterBehavioralFrames: new Dictionary<string, string>
            {
                ["Sarah (Wife)"] = "Sarah shows exploratory curiosity."
            },
            CharacterStatStateTexts: new Dictionary<string, string>
            {
                ["Sarah (Wife)"] = "Sarah's desire is at a peak, eager and uninhibited."
            });

        // Act
        RolePlayAssistantPrompts.AppendScenarioGuidance(builder, guidance, framingGuards: []);
        var text = builder.ToString();

        // Assert: behavioral frame HARD CONSTRAINT
        Assert.Contains("HARD CONSTRAINT — Sarah (Wife) behavioral frame (authoritative", text, StringComparison.Ordinal);
        Assert.Contains("Sarah shows exploratory curiosity.", text, StringComparison.Ordinal);

        // Assert: stat state text HARD CONSTRAINT
        Assert.Contains("HARD CONSTRAINT — Sarah (Wife) current state (authoritative", text, StringComparison.Ordinal);
        Assert.Contains("Sarah's desire is at a peak, eager and uninhibited.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendScenarioGuidance_WithNoStatStateText_DoesNotInjectStatStateConstraint()
    {
        var builder = new StringBuilder();
        var guidance = new ScenarioGuidanceContext(
            Phase: "Committed",
            ActiveScenarioId: null,
            GuidanceText: "Maintain arc",
            ExcludedScenarioIds: [],
            CharacterBehavioralFrames: new Dictionary<string, string>
            {
                ["Sarah (Wife)"] = "Sarah shows exploratory curiosity."
            },
            CharacterStatStateTexts: new Dictionary<string, string>() // empty
        );

        RolePlayAssistantPrompts.AppendScenarioGuidance(builder, guidance, framingGuards: []);
        var text = builder.ToString();

        // Behavioral frame injected
        Assert.Contains("HARD CONSTRAINT — Sarah (Wife) behavioral frame", text, StringComparison.Ordinal);

        // No stat state text injected
        Assert.DoesNotContain("current state (authoritative", text, StringComparison.Ordinal);
    }

    // ── T028: Multi-stat coherence — Wife Desire=82, Restraint=12, Loyalty=15 ─────────────────

    [Fact]
    public async Task CreateAsync_WifeMultipleNonNeutralStats_ProducesNonEmptyStatStateText()
    {
        // Arrange: Wife with Desire=82 (Band4), Restraint=12 (Band1), Loyalty=15 (Band1)
        var wifeProfile = CharacterStatProfileV2Accessor.CreateDefault("c-wife");
        CharacterStatProfileV2Accessor.SetStat(wifeProfile, "Desire", 82);
        CharacterStatProfileV2Accessor.SetStat(wifeProfile, "Restraint", 12);
        CharacterStatProfileV2Accessor.SetStat(wifeProfile, "Loyalty", 15);
        // Keep others neutral so they don't interfere
        CharacterStatProfileV2Accessor.SetStat(wifeProfile, "Dominance", 50);
        CharacterStatProfileV2Accessor.SetStat(wifeProfile, "SelfRespect", 50);

        var characters = new List<ScenarioCharacter>
        {
            new("c-wife", "Sarah", "Wife")
        };
        var runtimeStats = new Dictionary<string, CharacterStatProfileV2>
        {
            ["c-wife"] = wifeProfile
        };

        var factory = new ScenarioGuidanceContextFactory(new NoOpBehavioralFrameGenerator());
        var input = new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "Committed",
            ActiveScenarioId: null,
            VariantId: null,
            AverageDesire: 82,
            AverageRestraint: 12,
            AverageDominance: 50,
            AverageLoyalty: 15,
            SelectedWillingnessProfileId: null,
            CharacterEncounterProfileIds: new Dictionary<string, string>(),
            Characters: characters,
            SuppressedScenarioIds: [],
            CharacterRuntimeStats: runtimeStats);

        var context = await factory.CreateAsync(input);

        // At least one stat state text entry exists for the Wife character
        var hasEntry = context.CharacterStatStateTexts.Any(kvp =>
            kvp.Key.Contains("Sarah", StringComparison.OrdinalIgnoreCase)
            || kvp.Key.Contains("Wife", StringComparison.OrdinalIgnoreCase)
            || kvp.Key.Contains("c-wife", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasEntry, "Expected a non-empty stat state text for Wife character with non-neutral stats");

        // The stat state text itself is non-empty
        var statText = context.CharacterStatStateTexts.First(kvp =>
            kvp.Key.Contains("Sarah", StringComparison.OrdinalIgnoreCase)
            || kvp.Key.Contains("Wife", StringComparison.OrdinalIgnoreCase)
            || kvp.Key.Contains("c-wife", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.False(string.IsNullOrWhiteSpace(statText), "Stat state text should not be empty");
    }

    // ── T029: Boundary — single out-of-neutral, OtherMan drift is no-op ──────────────────────

    [Fact]
    public async Task CreateAsync_SingleNonNeutralStat_ProducesExactlyOneStatStateTextEntry()
    {
        // Arrange: Wife with only Desire=82 non-neutral, all others neutral
        var wifeProfile = CharacterStatProfileV2Accessor.CreateDefault("c-wife");
        foreach (var stat in AdaptiveStatCatalog.CanonicalStatNames)
            CharacterStatProfileV2Accessor.SetStat(wifeProfile, stat, 50);
        CharacterStatProfileV2Accessor.SetStat(wifeProfile, "Desire", 82); // only non-neutral

        var characters = new List<ScenarioCharacter>
        {
            new("c-wife", "Sarah", "Wife")
        };
        var runtimeStats = new Dictionary<string, CharacterStatProfileV2>
        {
            ["c-wife"] = wifeProfile
        };

        var factory = new ScenarioGuidanceContextFactory(new NoOpBehavioralFrameGenerator());
        var input = new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "BuildUp",
            ActiveScenarioId: null,
            VariantId: null,
            AverageDesire: 82,
            AverageRestraint: 50,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            CharacterEncounterProfileIds: new Dictionary<string, string>(),
            Characters: characters,
            SuppressedScenarioIds: [],
            CharacterRuntimeStats: runtimeStats);

        var context = await factory.CreateAsync(input);

        // One entry for the Wife character
        Assert.Single(context.CharacterStatStateTexts);
    }

    [Fact]
    public void StatTextCatalog_OtherManDominance10_ReturnsNonEmptyText()
    {
        // OtherMan Dominance=10 is Band1 → catalog should have text defined
        var text = CharacterStatTextCatalog.ResolveText("Dominance", "OtherMan", 10);
        Assert.False(string.IsNullOrWhiteSpace(text),
            "OtherMan Dominance Band1 should have stat state text defined in catalog");
    }

    [Fact]
    public void ApplyDelta_OtherManDominance10_NoRuntimeEncounterStatsDrift()
    {
        // OtherMan has no drift rules — RuntimeEncounterStats should not change
        var encounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"] = 50,
        };

        StatToDimensionMappings.ApplyDelta(encounterStats, "OtherMan", "Dominance", +10);

        // No change — OtherMan has empty rules
        Assert.Equal(50, encounterStats["Exhibitionism"]);
    }

    // ── Private stub ─────────────────────────────────────────────────────────────────────────

    private sealed class NoOpBehavioralFrameGenerator : DreamGenClone.Application.StoryAnalysis.Abstractions.IBehavioralFrameGenerator
    {
        public Task<IReadOnlyDictionary<string, string>> GenerateFramesAsync(
            IReadOnlyDictionary<string, string> characterEncounterProfileIds,
            IReadOnlyList<ScenarioCharacter> characters,
            IReadOnlyDictionary<string, CharacterStatProfileV2>? characterRuntimeStats = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }
}
