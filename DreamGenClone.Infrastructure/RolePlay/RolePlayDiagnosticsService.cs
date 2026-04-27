using DreamGenClone.Application.RolePlay;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RolePlayDiagnosticsService : IRolePlayDiagnosticsService
{
    private readonly IRolePlayDiagnosticsRepository _repository;

    public RolePlayDiagnosticsService(IRolePlayDiagnosticsRepository repository)
    {
        _repository = repository;
    }

    public async Task<RolePlayV2DiagnosticsSnapshot> GetSnapshotAsync(string sessionId, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        var adaptiveState = await _repository.LoadAdaptiveStateAsync(sessionId, cancellationToken);
        var turns = await _repository.LoadTurnsAsync(sessionId, 200, cancellationToken);
        var candidates = await _repository.LoadCandidateEvaluationsAsync(sessionId, 100, cancellationToken);
        var transitions = await _repository.LoadTransitionEventsAsync(sessionId, 100, cancellationToken);
        var decisions = await _repository.LoadDecisionPointsAsync(sessionId, 100, cancellationToken);
        var errors = await _repository.LoadUnsupportedSessionErrorsAsync(sessionId, 20, cancellationToken);

        return new RolePlayV2DiagnosticsSnapshot
        {
            SessionId = sessionId,
            AdaptiveState = adaptiveState,
            Turns = turns,
            CandidateEvaluations = candidates,
            TransitionEvents = transitions,
            DecisionPoints = decisions,
            CompatibilityErrors = errors,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N")
        };
    }
}
