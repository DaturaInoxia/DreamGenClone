using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Infrastructure.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;
using RpNarrativePhase = DreamGenClone.Domain.RolePlay.NarrativePhase;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Verifies that the static prompt changes introduced in B-006 (explicit scene writing directives)
/// produce the correct output: framing guards, fallback guidance, and intensity descriptions.
/// </summary>
public sealed class SceneWritingDirectivePromptTests
{
    // --- Intensity descriptions (T005) ---

    [Fact]
    public void GetDefaultDescription_Explicit_ContainsPacingAcrossMultipleTurns()
    {
        var description = IntensityLadder.GetDefaultDescription(IntensityLevel.Explicit);
        Assert.Contains("turn", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDefaultDescription_Hardcore_HasExplicitCaseNotFallthrough()
    {
        // Verify Hardcore has its own description distinct from the old _ fallthrough text
        var hardcoreDesc = IntensityLadder.GetDefaultDescription(IntensityLevel.Hardcore);
        var explicitDesc = IntensityLadder.GetDefaultDescription(IntensityLevel.Explicit);
        Assert.NotEqual(hardcoreDesc, explicitDesc);
        Assert.False(string.IsNullOrWhiteSpace(hardcoreDesc));
    }

    [Fact]
    public void GetDefaultDescription_Hardcore_ContainsMaximumIntensityLanguage()
    {
        var description = IntensityLadder.GetDefaultDescription(IntensityLevel.Hardcore);
        Assert.True(
            description.Contains("Maximum", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("full", StringComparison.OrdinalIgnoreCase),
            $"Hardcore description should describe maximum intensity; got: '{description}'");
    }

    [Fact]
    public void GetDefaultDescription_AllLevels_ReturnNonEmptyDistinctStrings()
    {
        var levels = Enum.GetValues<IntensityLevel>();
        var descriptions = levels.Select(IntensityLadder.GetDefaultDescription).ToList();
        Assert.All(descriptions, d => Assert.False(string.IsNullOrWhiteSpace(d)));
        Assert.Equal(descriptions.Count, descriptions.Distinct(StringComparer.Ordinal).Count());
    }

    // --- Climax fallback guidance (T006) ---

    [Fact]
    public async Task ScenarioGuidanceContextFactory_ClimaxFallback_ContainsPhysicalDetailGuidance()
    {
        // No LLM generator → fallback path is taken
        var factory = new ScenarioGuidanceContextFactory(NoOpFrameGenerator());
        var context = await factory.CreateAsync(new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "Climax",
            ActiveScenarioId: "infidelity",
            VariantId: null,
            AverageDesire: 80,
            AverageRestraint: 30,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            CharacterEncounterProfileIds: new Dictionary<string, string>(),
            Characters: [],
            SuppressedScenarioIds: []));;

        Assert.Equal("Climax", context.Phase);
        // Post-B006: guidance must be multi-sentence and mention physical detail and pacing
        Assert.True(context.GuidanceText.Length > 80,
            $"Climax fallback guidance should be multi-sentence; got: '{context.GuidanceText}'");
        Assert.True(
            context.GuidanceText.Contains("physical", StringComparison.OrdinalIgnoreCase) ||
            context.GuidanceText.Contains("detail", StringComparison.OrdinalIgnoreCase),
            $"Climax fallback guidance must reference physical detail; got: '{context.GuidanceText}'");
    }

    [Fact]
    public async Task ScenarioGuidanceContextFactory_ClimaxFallback_ContainsUrgencyGuidance()
    {
        var factory = new ScenarioGuidanceContextFactory(NoOpFrameGenerator());
        var context = await factory.CreateAsync(new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "Climax",
            ActiveScenarioId: "voyeurism",
            VariantId: null,
            AverageDesire: 80,
            AverageRestraint: 30,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            CharacterEncounterProfileIds: new Dictionary<string, string>(),
            Characters: [],
            SuppressedScenarioIds: []));

        Assert.True(
            context.GuidanceText.Contains("urgency", StringComparison.OrdinalIgnoreCase) ||
            context.GuidanceText.Contains("pacing", StringComparison.OrdinalIgnoreCase) ||
            context.GuidanceText.Contains("turn", StringComparison.OrdinalIgnoreCase),
            $"Climax guidance should address pacing/urgency; got: '{context.GuidanceText}'");
    }

    [Fact]
    public async Task ScenarioGuidanceContextFactory_NonClimaxPhases_StillReturnGuidance()
    {
        var factory = new ScenarioGuidanceContextFactory(NoOpFrameGenerator());
        foreach (var phase in new[] { "BuildUp", "Committed", "Approaching", "Reset" })
        {
            var context = await factory.CreateAsync(new ScenarioGuidanceInput(
                SessionId: "s1",
                CurrentPhase: phase,
                ActiveScenarioId: "dominance",
                VariantId: null,
                AverageDesire: 60,
                AverageRestraint: 40,
                AverageDominance: 50,
                AverageLoyalty: 50,
                SelectedWillingnessProfileId: null,
                CharacterEncounterProfileIds: new Dictionary<string, string>(),
                Characters: [],
                SuppressedScenarioIds: []));

            Assert.False(string.IsNullOrWhiteSpace(context.GuidanceText),
                $"Phase '{phase}' should still return guidance text");
        }
    }

    // --- Phase 7 tweaks: Climax fallback guidance ---

    [Fact]
    public async Task ScenarioGuidanceContextFactory_ClimaxFallback_ContainsEndClimaxGate()
    {
        var factory = new ScenarioGuidanceContextFactory(NoOpFrameGenerator());
        var context = await factory.CreateAsync(new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "Climax",
            ActiveScenarioId: "infidelity",
            VariantId: null,
            AverageDesire: 80,
            AverageRestraint: 30,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            CharacterEncounterProfileIds: new Dictionary<string, string>(),
            Characters: [],
            SuppressedScenarioIds: []));

        Assert.Contains("/endclimax", context.GuidanceText, StringComparison.OrdinalIgnoreCase);
    }

    private static IBehavioralFrameGenerator NoOpFrameGenerator() => new NoOpBehavioralFrameGenerator();

    private sealed class NoOpBehavioralFrameGenerator : IBehavioralFrameGenerator
    {
        public Task<IReadOnlyDictionary<string, string>> GenerateFramesAsync(
            IReadOnlyDictionary<string, string> characterEncounterProfileIds,
            IReadOnlyList<ScenarioCharacter> characters,
            IReadOnlyDictionary<string, CharacterStatProfileV2>? characterRuntimeStats = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }
}
