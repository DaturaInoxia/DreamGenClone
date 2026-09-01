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
}