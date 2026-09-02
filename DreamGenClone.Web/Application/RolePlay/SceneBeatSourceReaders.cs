using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ISceneBeatSessionReader
{
    Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed class SceneBeatSessionReader : ISceneBeatSessionReader
{
    private readonly IRolePlayEngineService _engineService;

    public SceneBeatSessionReader(IRolePlayEngineService engineService)
    {
        _engineService = engineService;
    }

    public Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => _engineService.GetSessionAsync(sessionId, cancellationToken);
}

public interface ISceneBeatScenarioReader
{
    Task<IReadOnlyList<Character>?> GetCharactersAsync(string scenarioId);

    /// <summary>
    /// Canonical location names available for the scenario, used to ground beat locations.
    /// Returns an empty list when locations are unavailable or not resolved.
    /// </summary>
    Task<IReadOnlyList<string>?> GetLocationsAsync(string scenarioId)
    {
        return Task.FromResult<IReadOnlyList<string>?>([]);
    }
}

public sealed class SceneBeatScenarioReader : ISceneBeatScenarioReader
{
    private readonly IScenarioService _scenarioService;

    public SceneBeatScenarioReader(IScenarioService scenarioService)
    {
        _scenarioService = scenarioService;
    }

    public async Task<IReadOnlyList<Character>?> GetCharactersAsync(string scenarioId)
        => (await _scenarioService.GetScenarioAsync(scenarioId))?.Characters;

    public async Task<IReadOnlyList<string>?> GetLocationsAsync(string scenarioId)
        => (await _scenarioService.GetScenarioAsync(scenarioId))?.Locations
            .Where(location => !string.IsNullOrWhiteSpace(location.Name))
            .Select(location => location.Name!.Trim())
            .ToList();
}