namespace DreamGenClone.Web.Application.Scenarios;

public interface IScenarioAdaptationService
{
    Task<ScenarioPreviewResult> PreviewScenarioAsync(
        string parsedStoryId,
        CancellationToken cancellationToken = default);

    Task<AdaptStoryResult> BuildScenarioFromPreviewAsync(
        ScenarioPreviewResult preview,
        List<CharacterSubstitution> characterSubstitutions,
        string? sourceStoryId,
        CancellationToken cancellationToken = default);

    Task<AdaptStoryResult> AdaptStoryToScenarioAsync(
        AdaptStoryRequest request,
        CancellationToken cancellationToken = default);
}
