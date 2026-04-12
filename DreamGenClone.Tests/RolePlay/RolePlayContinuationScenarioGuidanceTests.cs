using System.Text;
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
            AverageDesire: 70,
            AverageRestraint: 35,
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
}
