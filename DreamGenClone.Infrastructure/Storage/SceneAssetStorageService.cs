using System.Security.Cryptography;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Storage;

/// <summary>
/// Local-disk storage for the app-wide scene asset library. Writes under
/// <c>{SceneImageRoot}/assets/{fileName}</c> and computes the SHA-256, byte length, media type, and
/// dimensions required at ingest. Unsupported or non-image content is rejected before any database
/// row can be created. Reuses the image probing used by <see cref="CharacterImageAssetStorageService"/>.
/// </summary>
public sealed class SceneAssetStorageService : ISceneAssetStorageService
{
    private const int MaxProbeBytes = 256 * 1024;

    private readonly PersistenceOptions _options;
    private readonly ILogger<SceneAssetStorageService> _logger;

    public SceneAssetStorageService(
        IOptions<PersistenceOptions> options,
        ILogger<SceneAssetStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoredSceneAsset> SaveAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("A file name is required to store a scene asset.");

        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InvalidOperationException("A valid file name is required to store a scene asset.");

        var targetDirectory = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), "assets");
        Directory.CreateDirectory(targetDirectory);

        var fullPath = Path.Combine(targetDirectory, safeName);
        var relativePath = $"assets/{safeName}";

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var prefix = new MemoryStream();
        var buffer = new byte[81920];
        long byteLength = 0;

        try
        {
            await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    byteLength += read;

                    if (prefix.Length < MaxProbeBytes)
                    {
                        var remaining = (int)Math.Min(read, MaxProbeBytes - prefix.Length);
                        prefix.Write(buffer, 0, remaining);
                    }
                }
            }

            var (mediaType, width, height) = CharacterImageAssetStorageService.ProbeImage(prefix.GetBuffer(), (int)prefix.Length);

            var sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToUpperInvariant();
            _logger.LogInformation("Scene asset stored at {Path} ({ByteLength} bytes, {MediaType})", fullPath, byteLength, mediaType);

            return new StoredSceneAsset(relativePath, byteLength, sha256, mediaType, width, height);
        }
        catch
        {
            // Do not leave a partial or rejected file on disk.
            try { File.Delete(fullPath); } catch { /* best effort */ }
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), relativePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Scene asset deleted from {Path}", fullPath);
        }

        return Task.CompletedTask;
    }
}
