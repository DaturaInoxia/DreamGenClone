using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Front-step acquisition (B-121 Phase C): a per-build container of many front attempts, produced by
/// generate (editable seeded prompt) or upload, from which the user selects the winning front.
/// </summary>
public interface ICharacterIdentityFrontService
{
    Task<SceneAsset> EnsureFrontContainerAsync(
        string buildId, string containerName, CancellationToken cancellationToken = default);

    Task<string> ResolveFrontPromptAsync(
        string characterId, string? characterName, string description, CancellationToken cancellationToken = default);

    /// <summary>The template row actually in effect for this character (character override or global).</summary>
    Task<ImageWorkflowPromptTemplate> ResolveFrontTemplateAsync(
        string characterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a character-scoped override exists whose body differs from the global default.
    /// </summary>
    Task<bool> HasFrontPromptOverrideAsync(
        string characterId, CancellationToken cancellationToken = default);

    /// <summary>Persist the edited front prompt as a character-scoped override (never the global row).</summary>
    Task SaveFrontPromptAsync(
        string characterId, string prompt, CancellationToken cancellationToken = default);

    /// <summary>Reset this character's front prompt to the global default and return the filled default text.</summary>
    Task<string> ResetFrontPromptToDefaultAsync(
        string characterId, string? characterName, string description, CancellationToken cancellationToken = default);

    /// <summary>The persisted Front generation model for this character, or null when never chosen.</summary>
    Task<string?> ResolveFrontModelAsync(string characterId, CancellationToken cancellationToken = default);

    /// <summary>Persist the chosen Front generation model as a character-scoped setting (never global).</summary>
    Task SaveFrontModelAsync(string characterId, string modelId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAssetImage>> ListFrontAttemptsAsync(
        string buildId, CancellationToken cancellationToken = default);

    Task<SceneAssetImage> GenerateFrontAttemptAsync(
        string buildId,
        string containerName,
        string prompt,
        string modelId,
        string imageSize,
        CancellationToken cancellationToken = default);

    Task<SceneAssetImage> UploadFrontAttemptAsync(
        string buildId,
        string containerName,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuild> SelectFrontAsync(
        string buildId, string imageId, CancellationToken cancellationToken = default);
}
