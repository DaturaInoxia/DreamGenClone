using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IScenarioGuidanceContextFactory
{
    Task<ScenarioGuidanceContext> CreateAsync(
        ScenarioGuidanceInput input,
        CancellationToken cancellationToken = default);
}
