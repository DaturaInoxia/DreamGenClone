using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Infrastructure.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class ScenarioResetCycleTests
{
    [Fact]
    public async Task ApplyResetAsync_ClearsActiveScenario_AndReducesIntensitySignals()
    {
        var manager = new NarrativePhaseManager();

        var result = await manager.ApplyResetAsync(
            new AdaptiveScenarioSnapshot("dominance", "Climax", 14, 85, 20, 92),
            new ResetTrigger("Climax completed", false, null));

        Assert.Null(result.ActiveScenarioId);
        Assert.Equal("BuildUp", result.CurrentNarrativePhase);
        Assert.True(result.AverageDesire < 85);
        Assert.True(result.ActiveScenarioScore < 92);
    }
}
