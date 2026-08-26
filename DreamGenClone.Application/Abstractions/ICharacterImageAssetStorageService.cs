namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Metadata computed for a reference asset at ingest time: safe relative path under the
/// scene-image root, byte length, SHA-256, media type, and image dimensions (when detectable).
/// </summary>
public sealed record StoredCharacterImageAsset(
    string RelativePath,
    long ByteLength,
    string Sha256,
    string MediaType,
    int? Width,
    int? Height);

/// <summary>
/// Local-disk storage for character identity reference assets. Mirrors
/// <see cref="ISceneImageStorageService"/> but writes under a dedicated <c>identity/</c> subtree and
/// computes the checksum, byte length, media type, and dimensions required by the identity data
/// model. Unsupported or non-image content is rejected before any database row is created.
/// </summary>
public interface ICharacterImageAssetStorageService
{
    /// <summary>
    /// Save a reference asset's bytes and compute ingest metadata. Returns the relative path
    /// <c>identity/{characterProfileId}/{fileName}</c>. Throws for non-image or unsupported content.
    /// </summary>
    Task<StoredCharacterImageAsset> SaveAsync(
        string characterProfileId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Open a stored reference asset for reading.</summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Delete a stored reference asset. Idempotent (no-op if absent).</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
