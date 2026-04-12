using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IThemeDefinitionService
{
    Task<IReadOnlyList<ThemeDefinitionDocument>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThemeDefinitionDocument>> LoadFromFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
