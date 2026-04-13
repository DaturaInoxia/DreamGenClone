namespace DreamGenClone.Application.RolePlay;

public interface IScenarioGuidanceGenerator
{
    Task<ScenarioGuidanceOutput> GenerateGuidanceAsync(
        ScenarioGuidanceRequest request,
        CancellationToken cancellationToken = default);
}