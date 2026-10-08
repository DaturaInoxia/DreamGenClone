using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Storage;

/// <summary>
/// Local-disk storage for composed scene video files (B-152). Mirrors <see cref="SceneImageStorageService"/> but
/// writes under <c>PersistenceOptions.SceneVideoRoot</c>, and exposes the absolute path because the mandatory
/// loudness normalization step shells out to ffmpeg, which needs a real file rather than a stream.
/// </summary>
public sealed class SceneVideoStorageService : ISceneVideoStorageService
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<SceneVideoStorageService> _logger;

    public SceneVideoStorageService(IOptions<PersistenceOptions> options, ILogger<SceneVideoStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        string recordId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var safeRecord = SanitizeSegment(recordId);
        var safeName = Path.GetFileName(fileName);
        var targetDirectory = Path.Combine(Path.GetFullPath(_options.SceneVideoRoot), safeRecord);
        Directory.CreateDirectory(targetDirectory);

        var fullPath = Path.Combine(targetDirectory, safeName);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Scene video stored at {Path}", fullPath);

        return $"{safeRecord}/{safeName}";
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            ResolveAbsolutePath(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public string ResolveAbsolutePath(string relativePath) =>
        Path.Combine(Path.GetFullPath(_options.SceneVideoRoot), relativePath);

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = ResolveAbsolutePath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Scene video deleted from {Path}", fullPath);
        }

        return Task.CompletedTask;
    }

    private static string SanitizeSegment(string value)
    {
        var safe = string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c == '-'));
        return string.IsNullOrEmpty(safe) ? "unknown" : safe;
    }
}
