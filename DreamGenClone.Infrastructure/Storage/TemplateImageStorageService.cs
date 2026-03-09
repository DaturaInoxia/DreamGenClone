using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Storage;

public sealed class TemplateImageStorageService : ITemplateImageStorageService
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<TemplateImageStorageService> _logger;

    public TemplateImageStorageService(IOptions<PersistenceOptions> options, ILogger<TemplateImageStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(fileName);
        var targetDirectory = Path.GetFullPath(_options.TemplateImageRoot);
        Directory.CreateDirectory(targetDirectory);

        var generatedName = $"{Guid.NewGuid():N}-{safeName}";
        var fullPath = Path.Combine(targetDirectory, generatedName);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Template image stored at {Path}", fullPath);

        return generatedName;
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.Combine(Path.GetFullPath(_options.TemplateImageRoot), relativePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        _logger.LogInformation("Template image opened from {Path}", fullPath);

        return Task.FromResult(stream);
    }
}
