using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests for CR-006 P2 — full-turn context resolution
/// (<see cref="SceneImageTurnResolver"/>).
/// </summary>
public sealed class SceneImageTurnResolverTests
{
    private sealed class FakeStateRepository : IRolePlayStateRepository
    {
        public IReadOnlyList<RolePlayTurn> Turns { get; set; } = [];

        public Task<RolePlayTurn> StartTurnAsync(string sessionId, string turnKind, string triggerSource, string? initiatedByActorName, string? inputInteractionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task CompleteTurnAsync(string sessionId, string turnId, IReadOnlyList<string> outputInteractionIds, bool succeeded, string? failureReason = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult(Turns);
        public Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveAdaptiveStateSemanticFieldsAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveAdaptiveStateLocationFieldsAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveCandidateEvaluationsAsync(IReadOnlyList<ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveTransitionEventAsync(NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveCompletionMetadataAsync(ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveDecisionPointAsync(DecisionPoint decisionPoint, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveFormulaVersionReferenceAsync(string sessionId, FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveUnsupportedSessionErrorAsync(UnsupportedSessionError error, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveThemeMachineDiagnosticEventsAsync(IReadOnlyList<ThemeMachineDiagnosticEvent> events, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SaveEncounterSummaryAsync(EncounterSummaryRecord record, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task UpdateEncounterSummaryLlmAsync(string summaryId, string llmSummary, DateTime llmEnhancedUtc, string? enrichmentPrompt = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<EncounterSummaryRecord>> LoadEncounterSummariesForSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static RolePlaySession MakeSession(params RolePlayInteraction[] interactions) => new()
    {
        Id = "s1",
        Title = "Test",
        Interactions = interactions.ToList()
    };

    private static RolePlayInteraction MakeInteraction(string id, string actor, string content)
        => new() { Id = id, ActorName = actor, Content = content };

    [Fact]
    public async Task ResolveAsync_TurnFound_ReturnsAllSiblingInteractions()
    {
        var becky = MakeInteraction("i1", "Becky", "She stepped closer.");
        var dean = MakeInteraction("i2", "Dean", "He reached for her.");
        var narrative = MakeInteraction("i3", "Narrative", "The rain beat against the window.");
        var session = MakeSession(becky, dean, narrative);

        var repo = new FakeStateRepository
        {
            Turns = new List<RolePlayTurn>
            {
                new()
                {
                    TurnId = "t1",
                    SessionId = "s1",
                    TurnIndex = 1,
                    TurnKind = "Continue",
                    TriggerSource = "user",
                    InputInteractionId = "i0",
                    OutputInteractionIds = ["i1", "i2", "i3"]
                }
            }
        };
        var resolver = new SceneImageTurnResolver(repo);

        var result = await resolver.ResolveAsync(session, "i2");

        Assert.NotNull(result.Turn);
        Assert.Equal("t1", result.Turn!.TurnId);
        Assert.Equal(3, result.Interactions.Count);
        Assert.Contains(result.Interactions, x => x.Id == "i1");
        Assert.Contains(result.Interactions, x => x.Id == "i2");
        Assert.Contains(result.Interactions, x => x.Id == "i3");
        // Narrative interaction is identified.
        Assert.NotNull(result.NarrativeInteraction);
        Assert.Equal("i3", result.NarrativeInteraction!.Id);
        // Selected interaction preserved.
        Assert.Equal("i2", result.SelectedInteraction.Id);
    }

    [Fact]
    public async Task ResolveAsync_NoTurnRow_LegacyFallbackWindow()
    {
        var i1 = MakeInteraction("i1", "Becky", "one");
        var i2 = MakeInteraction("i2", "Dean", "two");
        var i3 = MakeInteraction("i3", "Narrative", "three");
        var i4 = MakeInteraction("i4", "Becky", "four");
        var session = MakeSession(i1, i2, i3, i4);

        var repo = new FakeStateRepository { Turns = [] };
        var resolver = new SceneImageTurnResolver(repo);

        var result = await resolver.ResolveAsync(session, "i2");

        Assert.Null(result.Turn);
        // Window around the selected interaction.
        Assert.Contains(result.Interactions, x => x.Id == "i1");
        Assert.Contains(result.Interactions, x => x.Id == "i2");
        Assert.Contains(result.Interactions, x => x.Id == "i3");
        Assert.Equal("i2", result.SelectedInteraction.Id);
    }

    [Fact]
    public async Task ResolveAsync_InteractionNotFound_Throws()
    {
        var session = MakeSession(MakeInteraction("i1", "Becky", "one"));
        var repo = new FakeStateRepository { Turns = [] };
        var resolver = new SceneImageTurnResolver(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(session, "missing"));
    }
}