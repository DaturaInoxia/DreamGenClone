namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Local-disk storage for generated scene image files. Mirrors <c>ITemplateImageStorageService</c>
/// but keeps files under <c>PersistenceOptions.SceneImageRoot</c> (git-ignored).
/// </summary>
public interface ISceneImageStorageService
{
    /// <summary>Save image bytes. Returns the relative path "{sessionId}/{imageId}.png".</summary>
    Task<string> SaveAsync(
        string sessionId, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Open a stored image for reading.</summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Delete a stored image. Idempotent (no-op if absent).</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
