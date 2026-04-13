using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class UnsupportedSessionVersionTests
{
    [Fact]
    public async Task UnsupportedSchema_IsRejectedAndPersisted()
    {
        var repository = new InMemoryStateRepository();
        var service = new RolePlaySessionCompatibilityService(repository, NullLogger<RolePlaySessionCompatibilityService>.Instance);

        var payload = "{\"RolePlayV2SchemaVersion\":\"v1\"}";
        var error = await service.ValidateSessionPayloadAsync("session-1", payload);

        Assert.NotNull(error);
        Assert.Equal("RPV2_UNSUPPORTED_SCHEMA", error!.ErrorCode);
        Assert.Contains("RolePlayV2SchemaVersion", error.MissingCanonicalStats);
        Assert.Single(repository.Errors);
    }

    [Fact]
    public async Task CorruptPayload_DoesNotMutateStateAndPersistsError()
    {
        var repository = new InMemoryStateRepository();
        var service = new RolePlaySessionCompatibilityService(repository, NullLogger<RolePlaySessionCompatibilityService>.Instance);

        var error = await service.ValidateSessionPayloadAsync("session-2", "{not-json}");

        Assert.NotNull(error);
        Assert.Contains("InvalidJson", error!.MissingCanonicalStats);
        Assert.Single(repository.Errors);
        Assert.Empty(repository.States);
    }

    private sealed class InMemoryStateRepository : IRolePlayV2StateRepository
    {
        public List<AdaptiveScenarioState> States { get; } = [];
        public List<UnsupportedSessionError> Errors { get; } = [];

        public Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
        {
            States.Add(state);
            return Task.CompletedTask;
        }

        public Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<AdaptiveScenarioState?>(null);
        public Task SaveCandidateEvaluationsAsync(IReadOnlyList<ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScenarioCandidateEvaluation>>([]);
        public Task SaveTransitionEventAsync(NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NarrativePhaseTransitionEvent>>([]);
        public Task SaveCompletionMetadataAsync(ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDecisionPointAsync(DecisionPoint decisionPoint, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DecisionPoint>>([]);
        public Task<IReadOnlyList<DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DecisionOption>>([]);
        public Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveFormulaVersionReferenceAsync(string sessionId, FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveUnsupportedSessionErrorAsync(UnsupportedSessionError error, CancellationToken cancellationToken = default)
        {
            Errors.Add(error);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UnsupportedSessionError>>(Errors);
    }
}
