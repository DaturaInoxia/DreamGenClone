using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Storage;

/// <summary>
/// Local-disk storage for generated scene image files. Mirrors <c>TemplateImageStorageService</c>
/// but writes under <c>PersistenceOptions.SceneImageRoot</c> (git-ignored, alongside the dev DB).
/// </summary>
public sealed class SceneImageStorageService : ISceneImageStorageService
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<SceneImageStorageService> _logger;

    public SceneImageStorageService(IOptions<PersistenceOptions> options, ILogger<SceneImageStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        string sessionId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var safeSession = SanitizeSegment(sessionId);
        var safeName = Path.GetFileName(fileName);
        var targetDirectory = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), safeSession);
        Directory.CreateDirectory(targetDirectory);

        var fullPath = Path.Combine(targetDirectory, safeName);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Scene image stored at {Path}", fullPath);

        return $"{safeSession}/{safeName}";
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), relativePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        _logger.LogDebug("Scene image opened from {Path}", fullPath);

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Scene image deleted from {Path}", fullPath);
        }

        return Task.CompletedTask;
    }

    private static string SanitizeSegment(string value)
    {
        // Keep alphanumerics and hyphens (GUID segments); strip everything else (dots, slashes,
        // path separators) to prevent path traversal while keeping directory names faithful.
        var safe = string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c == '-'));
        return string.IsNullOrEmpty(safe) ? "unknown" : safe;
    }
}
