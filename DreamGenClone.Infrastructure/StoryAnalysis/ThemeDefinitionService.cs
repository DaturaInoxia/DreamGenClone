using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ThemeDefinitionService : IThemeDefinitionService
{
    private readonly IThemeDefinitionParser _parser;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<ThemeDefinitionService> _logger;

    public ThemeDefinitionService(
        IThemeDefinitionParser parser,
        IHostEnvironment hostEnvironment,
        ILogger<ThemeDefinitionService> logger)
    {
        _parser = parser;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ThemeDefinitionDocument>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var root = _hostEnvironment.ContentRootPath;
        var definitionsDirectory = Path.GetFullPath(Path.Combine(root, "..", "specs", "ThemeDefinitaions"));

        if (!Directory.Exists(definitionsDirectory))
        {
            _logger.LogWarning("Theme definitions directory not found: {Directory}", definitionsDirectory);
            return [];
        }

        var files = Directory
            .EnumerateFiles(definitionsDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return await LoadFromFilesAsync(files, cancellationToken);
    }

    public async Task<IReadOnlyList<ThemeDefinitionDocument>> LoadFromFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var documents = new List<ThemeDefinitionDocument>();

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Theme definition file does not exist: {FilePath}", filePath);
                continue;
            }

            var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
            var document = _parser.Parse(filePath, raw);
            documents.Add(document);
        }

        return documents
            .OrderBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
