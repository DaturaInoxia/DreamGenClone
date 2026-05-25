namespace DreamGenClone.Web.Application.RolePlay;

public interface ISemanticEventInferenceService
{
    Task<SemanticEventInferenceResult> InferAsync(
        SemanticEventInferenceRequest request,
        CancellationToken cancellationToken = default);
}
