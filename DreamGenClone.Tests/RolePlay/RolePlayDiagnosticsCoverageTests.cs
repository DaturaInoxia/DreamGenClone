using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayDiagnosticsCoverageTests
{
    [Fact]
    public async Task CandidateScoreDiagnostics_AreIncluded()
    {
        var service = new RolePlayDiagnosticsService(new FakeDiagnosticsRepository());

        var snapshot = await service.GetSnapshotAsync("session-1", "corr-1");

        Assert.NotEmpty(snapshot.CandidateEvaluations);
    }

    [Fact]
    public async Task TransitionDiagnostics_AreIncluded()
    {
        var service = new RolePlayDiagnosticsService(new FakeDiagnosticsRepository());

        var snapshot = await service.GetSnapshotAsync("session-1", "corr-2");

        Assert.NotEmpty(snapshot.TransitionEvents);
    }

    [Fact]
    public async Task ConceptAndDecisionDiagnostics_AreIncluded()
    {
        var service = new RolePlayDiagnosticsService(new FakeDiagnosticsRepository());

        var snapshot = await service.GetSnapshotAsync("session-1", "corr-3");

        Assert.NotEmpty(snapshot.DecisionPoints);
        Assert.NotEmpty(snapshot.CompatibilityErrors);
    }

    private sealed class FakeDiagnosticsRepository : IRolePlayDiagnosticsRepository
    {
        public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScenarioCandidateEvaluation>>([new ScenarioCandidateEvaluation { SessionId = sessionId, ScenarioId = "s", EvaluationId = "e" }]);

        public Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NarrativePhaseTransitionEvent>>([new NarrativePhaseTransitionEvent { SessionId = sessionId, TransitionId = "t" }]);

        public Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DecisionPoint>>([new DecisionPoint { SessionId = sessionId, DecisionPointId = "d" }]);

        public Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UnsupportedSessionError>>([new UnsupportedSessionError { SessionId = sessionId, ErrorCode = "E" }]);
    }
}
