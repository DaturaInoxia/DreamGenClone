using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Orchestration surface for the app-wide asset library (Asset Studio). Creates assets by prompt or
/// upload, enqueues Qwen edits and the special profile-pack function, and provides list/view/
/// download/delete operations. The UI talks to this service, never to the repository or storage.
/// </summary>
public interface ISceneAssetService
{
    Task<SceneAsset> CreateFromPromptAsync(
        string name, string prompt, string? size = null, CancellationToken cancellationToken = default);

    Task<SceneAsset> CreateFromUploadAsync(
        string name, string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<SceneAsset> EnqueueEditAsync(
        string sourceAssetId, string name, string editPrompt, CancellationToken cancellationToken = default);

    Task EnqueueProfilePackAsync(SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default);

    Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(
        string identityPackId, CancellationToken cancellationToken = default);

    /// <summary>Open a complete asset's stored bytes for viewing/downloading.</summary>
    Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(
        string assetId, CancellationToken cancellationToken = default);

    Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default);
}
