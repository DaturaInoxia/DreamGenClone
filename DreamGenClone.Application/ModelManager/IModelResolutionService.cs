using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.ModelManager;

public interface IModelResolutionService
{
    Task<ResolvedModel> ResolveAsync(
        AppFunction function,
        string? sessionModelId = null,
        double? sessionTemperature = null,
        double? sessionTopP = null,
        int? sessionMaxTokens = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve the text model used for scene-image prompt drafting (RolePlaySceneImagePreprocessor).
    /// Fails fast when no function default is configured.
    /// </summary>
    Task<ResolvedModel> ResolveImagePromptModelAsync(
        string? sessionOverrideId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve the image model used for scene-image rendering (RolePlaySceneImage). Fails fast when
    /// the function default is missing, the model is not image-kind, the provider is not
    /// image-capable, or the provider content policy is Unknown.
    /// </summary>
    Task<ResolvedImageModel> ResolveImageModelAsync(
        string? sessionOverrideId = null,
        CancellationToken cancellationToken = default);
}
