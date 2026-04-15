using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Infrastructure.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayContinuationScenarioGuidanceTests
{
    [Theory]
    [InlineData("BuildUp")]
    [InlineData("Committed")]
    [InlineData("Approaching")]
    [InlineData("Climax")]
    [InlineData("Reset")]
    public async Task CreateAsync_BuildsGuidance_ForEachPhase(string phase)
    {
        var factory = new ScenarioGuidanceContextFactory();

        var context = await factory.CreateAsync(new ScenarioGuidanceInput(
            SessionId: "s1",
            CurrentPhase: phase,
            ActiveScenarioId: "dominance",
            VariantId: null,
            AverageDesire: 70,
            AverageRestraint: 35,
            AverageTension: 50,
            AverageConnection: 50,
            AverageDominance: 50,
            AverageLoyalty: 50,
            SelectedWillingnessProfileId: null,
            HusbandAwarenessProfileId: null,
            SuppressedScenarioIds: ["infidelity"]));

        Assert.Equal(phase, context.Phase);
        Assert.False(string.IsNullOrWhiteSpace(context.GuidanceText));
    }

    [Fact]
    public void AppendScenarioGuidance_IncludesExclusionAndGuards()
    {
        var builder = new StringBuilder();
        var guidance = new ScenarioGuidanceContext(
            Phase: "Climax",
            ActiveScenarioId: "dominance",
            GuidanceText: "Deliver culmination",
            ExcludedScenarioIds: ["infidelity", "voyeurism"]);

        var guards = RolePlayAssistantPrompts.BuildFramingGuards("Climax", "dominance");
        RolePlayAssistantPrompts.AppendScenarioGuidance(builder, guidance, guards);

        var text = builder.ToString();
        Assert.Contains("Active Scenario: dominance", text, StringComparison.Ordinal);
        Assert.Contains("Exclude contradictory framing for: infidelity, voyeurism", text, StringComparison.Ordinal);
        Assert.Contains("Do not pivot to a competing scenario", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendThemeAIGuidance_AddsSoftWeightedHints()
    {
        var builder = new StringBuilder();
        var theme = new RPTheme
        {
            Id = "infidelity-public-facade",
            AIGenerationNotes =
            [
                new RPThemeAIGuidanceNote { Section = RPThemeAIGuidanceSection.KeyScenarioElement, Text = "Keep departures brief and plausible.", SortOrder = 0 },
                new RPThemeAIGuidanceNote { Section = RPThemeAIGuidanceSection.InteractionDynamics, Text = "Escalate excuse complexity over time.", SortOrder = 1 },
                new RPThemeAIGuidanceNote { Section = RPThemeAIGuidanceSection.Avoidance, Text = "Avoid direct witness by partner.", SortOrder = 2 },
                new RPThemeAIGuidanceNote { Section = RPThemeAIGuidanceSection.FitFormula, Text = "Fit Score = (Tension x 0.25) + (Restraint x 0.25)", SortOrder = 3 }
            ]
        };

        RolePlayAssistantPrompts.AppendThemeAIGuidance(builder, theme, phase: "Committed", influencePercent: 40, maxNotes: 6);

        var text = builder.ToString();
        Assert.Contains("Theme AI Guidance (soft hints, influence=40%):", text, StringComparison.Ordinal);
        Assert.Contains("Escalate excuse complexity over time.", text, StringComparison.Ordinal);
        Assert.Contains("Apply these as soft guidance only", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fit Score =", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendThemeAIGuidance_WithMissingTheme_DoesNothing()
    {
        var builder = new StringBuilder();
        RolePlayAssistantPrompts.AppendThemeAIGuidance(builder, activeTheme: null, phase: "BuildUp", influencePercent: 35, maxNotes: 6);
        Assert.Equal(string.Empty, builder.ToString());
    }
}
