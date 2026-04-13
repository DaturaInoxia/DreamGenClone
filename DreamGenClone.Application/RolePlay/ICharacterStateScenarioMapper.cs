using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.RolePlay;

public interface ICharacterStateScenarioMapper
{
    Task<IReadOnlyDictionary<string, ScenarioFitResult>> EvaluateAllScenariosAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ThemeCatalogEntry> catalogEntries,
        CancellationToken cancellationToken = default);
}