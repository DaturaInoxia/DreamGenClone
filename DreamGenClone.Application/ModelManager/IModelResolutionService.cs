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

    /// <summary>
    /// Resolve the identity-conditioned image model (RolePlaySceneImage + identity mechanism config).
    /// Fails fast when the identity mechanism, strength, or required artifacts are missing. Never
    /// substitutes a mechanism or artifact default.
    /// </summary>
    Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(
        string? sessionOverrideId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a specific registered image model by id (user-pinned render selection). Applies the
    /// same fail-fast validation as <see cref="ResolveImageModelAsync"/> (enabled, image-kind,
    /// provider enabled + image-capable + content policy set, scene-image family metadata).
    /// </summary>
    Task<ResolvedImageModel> ResolveImageModelByIdAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a specific registered identity-conditioned image model by id. Fails fast when the
    /// identity mechanism, strength, or required artifacts are missing on that model.
    /// </summary>
    Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List enabled image models for the Studio model selector. When <paramref name="identityCapableOnly"/>
    /// is true, only models with an identity mechanism configured are returned.
    /// </summary>
    Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(
        bool identityCapableOnly,
        CancellationToken cancellationToken = default);

}
