using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RolePlayDiagnosticsRepository : IRolePlayDiagnosticsRepository
{
    private readonly IRolePlayV2StateRepository _stateRepository;

    public RolePlayDiagnosticsRepository(IRolePlayV2StateRepository stateRepository)
    {
        _stateRepository = stateRepository;
    }

    public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
        => _stateRepository.LoadCandidateEvaluationsAsync(sessionId, take, cancellationToken);

    public Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
        => _stateRepository.LoadTransitionEventsAsync(sessionId, take, cancellationToken);

    public Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
        => _stateRepository.LoadDecisionPointsAsync(sessionId, take, cancellationToken);

    public Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default)
        => _stateRepository.LoadUnsupportedSessionErrorsAsync(sessionId, take, cancellationToken);
}
