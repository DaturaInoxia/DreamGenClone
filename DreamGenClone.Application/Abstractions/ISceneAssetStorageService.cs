namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Metadata computed for a scene asset at ingest time: safe relative path under the scene-image
/// root, byte length, SHA-256, media type, and image dimensions (when detectable).
/// </summary>
public sealed record StoredSceneAsset(
    string RelativePath,
    long ByteLength,
    string Sha256,
    string MediaType,
    int? Width,
    int? Height);

/// <summary>
/// Local-disk storage for the app-wide scene asset library. Writes under a dedicated
/// <c>assets/</c> subtree of the scene-image root (git-ignored) and computes the checksum, byte
/// length, media type, and dimensions required by the asset data model. Unsupported or non-image
/// content is rejected before any database row is created.
/// </summary>
public interface ISceneAssetStorageService
{
    /// <summary>
    /// Save an asset's bytes and compute ingest metadata. Returns the relative path
    /// <c>assets/{fileName}</c>. Throws for non-image or unsupported content.
    /// </summary>
    Task<StoredSceneAsset> SaveAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Open a stored asset for reading.</summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Delete a stored asset. Idempotent (no-op if absent).</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
