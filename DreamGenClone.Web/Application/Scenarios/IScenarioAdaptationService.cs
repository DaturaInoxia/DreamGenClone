namespace DreamGenClone.Web.Application.Scenarios;

public interface IScenarioAdaptationService
{
    Task<AdaptStoryResult> AdaptStoryToScenarioAsync(
        AdaptStoryRequest request,
        CancellationToken cancellationToken = default);
}
