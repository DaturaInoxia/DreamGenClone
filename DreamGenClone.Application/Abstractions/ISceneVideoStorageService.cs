namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Local-disk storage for composed scene video files (B-152). Mirrors <see cref="ISceneImageStorageService"/> but
/// writes under <c>PersistenceOptions.SceneVideoRoot</c> (git-ignored, served at <c>/scene-videos</c>).
/// </summary>
public interface ISceneVideoStorageService
{
    /// <summary>Save clip bytes. Returns the relative path "{recordId}/{fileName}".</summary>
    Task<string> SaveAsync(
        string recordId, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Open a stored clip for reading.</summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Absolute path of a stored clip - used by the ffmpeg verification step, which needs a real file.</summary>
    string ResolveAbsolutePath(string relativePath);

    /// <summary>Delete a stored clip. Idempotent (no-op if absent).</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
