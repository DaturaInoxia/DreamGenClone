using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Infrastructure.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Verifies that the static prompt changes introduced in B-006 (explicit scene writing directives)
/// produce the correct output: framing guards, fallback guidance, and intensity descriptions.
/// </summary>
public sealed class SceneWritingDirectivePromptTests
{
    // --- Climax framing guards (T004) ---

    [Fact]
    public void BuildFramingGuards_Climax_ReturnsAtLeastFiveGuards()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "scene-dominance");
        // Pre-B006: 1 guard. Post-B006: 5 guards.
        Assert.True(guards.Count >= 5, $"Expected at least 5 Climax guards; got {guards.Count}");
    }

    [Fact]
    public void BuildFramingGuards_Climax_ContainsSustainedPacingDirective()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "scene-dominance");
        Assert.Contains(guards, g => g.Contains("Sustain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildFramingGuards_Climax_ContainsExplicitDetailDirective()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "scene-dominance");
        Assert.Contains(guards, g => g.Contains("explicit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildFramingGuards_Climax_ContainsUrgencyDirective()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "scene-dominance");
        Assert.Contains(guards, g => g.Contains("urgency", StringComparison.OrdinalIgnoreCase)
                                  || g.Contains("Urgency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildFramingGuards_BuildUp_DoesNotContainClimaxGuards()
    {
        // BuildUp guards must not include Climax-phase specific scene-writing directives
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("BuildUp", "scene-exploration");
        Assert.DoesNotContain(guards, g => g.Contains("Sustain the physical scene", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildFramingGuards_NoActiveScenario_ReturnsEmpty()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", null);
        Assert.Empty(guards);
    }

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
        var factory = new ScenarioGuidanceContextFactory();
        var context = await factory.CreateAsync(new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "Climax",
            ActiveScenarioId: "infidelity",
            VariantId: null,
            AverageDesire: 80,
            AverageRestraint: 30,
            AverageTension: 70,
            AverageConnection: 60,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            HusbandAwarenessProfileId: null,
            SuppressedScenarioIds: []));

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
        var factory = new ScenarioGuidanceContextFactory();
        var context = await factory.CreateAsync(new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "Climax",
            ActiveScenarioId: "voyeurism",
            VariantId: null,
            AverageDesire: 80,
            AverageRestraint: 30,
            AverageTension: 70,
            AverageConnection: 60,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            HusbandAwarenessProfileId: null,
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
        var factory = new ScenarioGuidanceContextFactory();
        foreach (var phase in new[] { "BuildUp", "Committed", "Approaching", "Reset" })
        {
            var context = await factory.CreateAsync(new ScenarioGuidanceInput(
                SessionId: "s1",
                CurrentPhase: phase,
                ActiveScenarioId: "dominance",
                VariantId: null,
                AverageDesire: 60,
                AverageRestraint: 40,
                AverageTension: 50,
                AverageConnection: 50,
                AverageDominance: 50,
                AverageLoyalty: 50,
                SelectedWillingnessProfileId: null,
                HusbandAwarenessProfileId: null,
                SuppressedScenarioIds: []));

            Assert.False(string.IsNullOrWhiteSpace(context.GuidanceText),
                $"Phase '{phase}' should still return guidance text");
        }
    }

    // --- Phase 7 tweaks: position spanning, male climax gate, word count (T024/T025) ---

    [Fact]
    public void BuildFramingGuards_Climax_ContainsEndClimaxGate()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "scene-dominance");
        Assert.Contains(guards, g => g.Contains("/endclimax", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildFramingGuards_Climax_ContainsPositionSpanningDirective()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "scene-dominance");
        // The new position-spanning guard must mention staying in the act across turns
        Assert.Contains(guards, g =>
            g.Contains("two turns", StringComparison.OrdinalIgnoreCase) ||
            g.Contains("multiple turns", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScenarioGuidanceContextFactory_ClimaxFallback_ContainsEndClimaxGate()
    {
        var factory = new ScenarioGuidanceContextFactory();
        var context = await factory.CreateAsync(new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: "Climax",
            ActiveScenarioId: "infidelity",
            VariantId: null,
            AverageDesire: 80,
            AverageRestraint: 30,
            AverageTension: 70,
            AverageConnection: 60,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            HusbandAwarenessProfileId: null,
            SuppressedScenarioIds: []));

        Assert.Contains("/endclimax", context.GuidanceText, StringComparison.OrdinalIgnoreCase);
    }

    // --- Phase 8 tweaks: same-scene multi-perspective turns (T031) ---

    [Fact]
    public void BuildFramingGuards_Climax_ContainsTurnSceneContract()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "scene-dominance");
        Assert.Contains(guards, g =>
            g.Contains("Turn Scene Contract", StringComparison.OrdinalIgnoreCase) ||
            g.Contains("same physical scene moment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildFramingGuards_Climax_ContainsTurnAdvancementGate()
    {
        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "scene-dominance");
        // Turn-hold: scene advancement is gated to the next Continue turn
        Assert.Contains(guards, g =>
            g.Contains("next Continue turn", StringComparison.OrdinalIgnoreCase) ||
            g.Contains("next continue", StringComparison.OrdinalIgnoreCase));
    }
}
