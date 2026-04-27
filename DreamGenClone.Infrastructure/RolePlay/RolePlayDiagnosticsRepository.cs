using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RolePlayDiagnosticsRepository : IRolePlayDiagnosticsRepository
{
    private readonly IRolePlayStateRepository _stateRepository;

    public RolePlayDiagnosticsRepository(IRolePlayStateRepository stateRepository)
    {
        _stateRepository = stateRepository;
    }

    public Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
        => _stateRepository.LoadAdaptiveStateAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
        => _stateRepository.LoadTurnsAsync(sessionId, take, cancellationToken);

    public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
        => _stateRepository.LoadCandidateEvaluationsAsync(sessionId, take, cancellationToken);

    public Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
        => _stateRepository.LoadTransitionEventsAsync(sessionId, take, cancellationToken);

    public Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
        => _stateRepository.LoadDecisionPointsAsync(sessionId, take, cancellationToken);

    public Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default)
        => _stateRepository.LoadUnsupportedSessionErrorsAsync(sessionId, take, cancellationToken);
}
