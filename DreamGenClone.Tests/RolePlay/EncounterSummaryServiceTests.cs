using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class EncounterSummaryServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static EncounterSummaryService CreateService() =>
        new(new NullRepository(), NullLogger<EncounterSummaryService>.Instance);

    private static CharacterStatProfileV2 MakeSnapshot(string charId = "char-a") =>
        new()
        {
            CharacterId = charId,
            Desire      = 60,
            Restraint   = 30,
            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 50, ["Connection"] = 40 }
        };

    private static AdaptiveScenarioState MakeState(string sessionId = "sess-1", int cycleIndex = 0) =>
        new()
        {
            SessionId           = sessionId,
            CycleIndex          = cycleIndex,
            CurrentPhase        = NarrativePhase.Approaching,
            TurnCountInPhase = 3,
            CurrentSceneLocation = "Bedroom",
            PrimaryThemeId      = "theme-abc",
            CurrentBeatCode     = "beat-42",
            CharacterSnapshots  = [MakeSnapshot("char-a"), MakeSnapshot("char-b")]
        };

    private static NarrativePhaseTransitionEvent MilestoneEvent(string sessionId = "sess-1") =>
        new()
        {
            SessionId   = sessionId,
            FromPhase   = NarrativePhase.Committed,
            ToPhase     = NarrativePhase.Approaching,
            OccurredUtc = DateTime.UtcNow
        };

    private static NarrativePhaseTransitionEvent ArcCompletionEvent(string sessionId = "sess-1") =>
        new()
        {
            SessionId   = sessionId,
            FromPhase   = NarrativePhase.Climax,
            ToPhase     = NarrativePhase.Reset,
            OccurredUtc = DateTime.UtcNow
        };

    // ── T016 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateTemplate_PhaseMilestone_ContainsAllFields()
    {
        var svc   = CreateService();
        var state = MakeState();
        var evt   = MilestoneEvent();

        var records = await svc.GenerateTemplatesAsync(evt, state);

        // One record per character snapshot
        Assert.Equal(2, records.Count);

        var r = records[0];
        Assert.Equal(EncounterSummaryType.PhaseMilestone, r.SummaryType);
        Assert.Equal(state.CycleIndex,  r.CycleIndex);
        Assert.Equal(evt.FromPhase,     r.FromPhase);
        Assert.Equal(evt.ToPhase,       r.ToPhase);
        Assert.Equal(state.TurnCountInPhase, r.TurnCountInPhase);
        Assert.Equal(state.CurrentSceneLocation,    r.SceneLocation);
        Assert.Equal(state.PrimaryThemeId,          r.ActiveThemeId);
        Assert.NotEmpty(r.TemplateSummary);
        Assert.Null(r.LlmSummary);

        // Template should mention phase names and character id
        Assert.Contains("Committed",  r.TemplateSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Approaching",r.TemplateSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("char-a",     r.TemplateSummary, StringComparison.OrdinalIgnoreCase);
        // Stat values should be present
        Assert.Contains("60",         r.TemplateSummary); // Desire
        Assert.Contains("30",         r.TemplateSummary); // Restraint
    }

    [Fact]
    public async Task GenerateTemplate_EmptyCharacterSnapshots_ProducesMinimalSummaryWithoutThrowing()
    {
        var svc = CreateService();
        var state = new AdaptiveScenarioState
        {
            SessionId          = "sess-empty",
            CharacterSnapshots = [] // empty
        };
        var evt = MilestoneEvent("sess-empty");

        var records = await svc.GenerateTemplatesAsync(evt, state);

        Assert.Empty(records);
    }

    // ── T020 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateTemplate_ArcCompletion_ContainsBeatCodeAndStats()
    {
        var svc   = CreateService();
        var state = MakeState(cycleIndex: 1);
        var evt   = ArcCompletionEvent();

        var records = await svc.GenerateTemplatesAsync(evt, state);

        Assert.Equal(2, records.Count);

        var r = records[0];
        Assert.Equal(EncounterSummaryType.ArcCompletion, r.SummaryType);
        Assert.Equal(NarrativePhase.Reset, r.ToPhase);

        // Template must contain the beat code, stat values, and arc number
        Assert.Contains("beat-42", r.TemplateSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("60",      r.TemplateSummary); // Desire
        Assert.Contains("2",       r.TemplateSummary); // Arc 2 (CycleIndex 1 → "arc 2")
    }

    // ── null repository stub ─────────────────────────────────────────────

    private sealed class NullRepository : IRolePlayStateRepository
    {
        public Task SaveEncounterSummaryAsync(EncounterSummaryRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateEncounterSummaryLlmAsync(string id, string llm, DateTime utc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EncounterSummaryRecord>> LoadEncounterSummariesForSessionAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>([]);

        // ── all other interface members ───────────────────────────────────
        public Task<DreamGenClone.Domain.RolePlay.RolePlayTurn> StartTurnAsync(string sessionId, string turnKind, string triggerSource, string? initiatedByActorName, string? inputInteractionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task CompleteTurnAsync(string sessionId, string turnId, IReadOnlyList<string> outputInteractionIds, bool succeeded, string? failureReason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.RolePlayTurn>>([]);
        public Task SaveAdaptiveStateAsync(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState?>(null);
        public Task SaveCandidateEvaluationsAsync(IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>>([]);
        public Task SaveTransitionEventAsync(DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent>>([]);
        public Task SaveCompletionMetadataAsync(DreamGenClone.Domain.RolePlay.ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDecisionPointAsync(DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint, IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption> options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionPoint>>([]);
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption>>([]);
        public Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveFormulaVersionReferenceAsync(string sessionId, DreamGenClone.Domain.RolePlay.FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveUnsupportedSessionErrorAsync(DreamGenClone.Domain.RolePlay.UnsupportedSessionError error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.UnsupportedSessionError>>([]);
        public Task SaveThemeMachineDiagnosticEventsAsync(IReadOnlyList<DreamGenClone.Domain.RolePlay.ThemeMachineDiagnosticEvent> events, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.ThemeMachineDiagnosticEvent>>([]);
    }
}
