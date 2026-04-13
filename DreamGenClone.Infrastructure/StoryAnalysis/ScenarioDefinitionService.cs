using System.Text.RegularExpressions;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed partial class ScenarioDefinitionService : IScenarioDefinitionService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<ScenarioDefinitionService> _logger;

    public ScenarioDefinitionService(ISqlitePersistence persistence, ILogger<ScenarioDefinitionService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public Task<ScenarioDefinitionEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadScenarioDefinitionAsync(id, cancellationToken);

    public async Task<IReadOnlyList<ScenarioDefinitionEntity>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
        => await _persistence.LoadAllScenarioDefinitionsAsync(includeDisabled, cancellationToken);

    public async Task SaveAsync(ScenarioDefinitionEntity definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!IsValidId(definition.Id))
        {
            throw new ArgumentException(
                $"Scenario definition ID '{definition.Id}' is invalid. Must match pattern ^[a-z0-9]+(-[a-z0-9]+)*$ and be at most 80 characters.",
                nameof(definition));
        }

        definition.Label = definition.Label?.Trim() ?? string.Empty;
        definition.Description = definition.Description?.Trim() ?? string.Empty;
        definition.Category = definition.Category?.Trim() ?? string.Empty;
        definition.VariantOf = definition.VariantOf?.Trim() ?? string.Empty;
        definition.Weight = Math.Clamp(definition.Weight, 1, 10);
        definition.ScenarioFitRules = definition.ScenarioFitRules?.Trim() ?? string.Empty;
        definition.PhaseGuidance = definition.PhaseGuidance?.Trim() ?? string.Empty;
        definition.Keywords = NormalizeList(definition.Keywords);
        definition.DirectionalKeywords = NormalizeList(definition.DirectionalKeywords);
        definition.StatAffinities = definition.StatAffinities
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value != 0)
            .ToDictionary(kv => kv.Key.Trim(), kv => Math.Clamp(kv.Value, -5, 5), StringComparer.OrdinalIgnoreCase);

        if (definition.IsEnabled && definition.Keywords.Count == 0)
        {
            throw new ArgumentException("Enabled scenario definitions must include at least one keyword.", nameof(definition));
        }

        await _persistence.SaveScenarioDefinitionAsync(definition, cancellationToken);
        _logger.LogInformation("Scenario definition saved: {DefinitionId}, Label={Label}, Enabled={IsEnabled}",
            definition.Id,
            definition.Label,
            definition.IsEnabled);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _persistence.DeleteScenarioDefinitionAsync(id, cancellationToken);
        _logger.LogInformation("Scenario definition delete attempted: {DefinitionId}", id);
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 80 && IdPattern().IsMatch(id);

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();
}
