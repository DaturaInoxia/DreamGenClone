using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class PhaseLifecycleTransitionTests
{
    private readonly ScenarioLifecycleService _service = new(NullLogger<ScenarioLifecycleService>.Instance);

    private static ScenarioLifecycleService CreateServiceWithProfile()
    {
        var profileService = new StubNarrativeGateProfileService();
        return new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance, profileService);
    }

    [Fact]
    public async Task ValidLifecycleTransitionSequence_ProgressesInOrder()
    {
        // All phase transitions require a configured profile — no hardcoded fallbacks.
        var service = CreateServiceWithProfile();
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Committed;

        var toApproaching = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            InteractionsSinceCommitment = 3,
            ActiveScenarioFitScore = 61m,
            ActiveScenarioConfidence = 0.8m
        });
        state.CurrentPhase = toApproaching.TargetPhase;

        var toClimax = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            InteractionsSinceCommitment = 1,
            ActiveScenarioFitScore = 85m,
            ActiveScenarioConfidence = 0.9m
        });
        state.CurrentPhase = toClimax.TargetPhase;

        var toReset = await service.EvaluateTransitionAsync(state, new LifecycleInputs { ClimaxCompletionRequested = true });
        state.CurrentPhase = toReset.TargetPhase;

        var toBuildUp = await service.EvaluateTransitionAsync(state, new LifecycleInputs());

        Assert.Equal(NarrativePhase.Approaching, toApproaching.TargetPhase);
        Assert.Equal(NarrativePhase.Climax, toClimax.TargetPhase);
        Assert.Equal(NarrativePhase.Reset, toReset.TargetPhase);
        Assert.Equal(NarrativePhase.BuildUp, toBuildUp.TargetPhase);
    }

    [Fact]
    public async Task BuildUp_DoesNotTransitionToCommitted_ViaLifecycle()
    {
        var service = CreateServiceWithProfile();
        var state = CreateState();

        var result = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            ActiveScenarioConfidence = 0.99m,
            ActiveScenarioFitScore = 95m,
            InteractionsSinceCommitment = 100
        });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.BuildUp, result.TargetPhase);
    }

    [Fact]
    public async Task Committed_DoesNotTransitionWithoutProfile()
    {
        // Without a profile, no fallback thresholds — transition is blocked.
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Committed;

        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            InteractionsSinceCommitment = 100,
            ActiveScenarioConfidence = 0.99m,
            ActiveScenarioFitScore = 95m
        });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.Committed, result.TargetPhase);
    }

    [Fact]
    public async Task Climax_DoesNotAutoResetWithoutCommandOrGateProfile()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Climax;

        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            InteractionsSinceCommitment = 50,
            ActiveScenarioFitScore = 95m
        });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.Climax, result.TargetPhase);
    }

    [Fact]
    public async Task IllegalTransitionRequest_IsRejected()
    {
        var state = CreateState();
        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs { InteractionsSinceCommitment = 5, ActiveScenarioFitScore = 90m });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.BuildUp, result.TargetPhase);
    }

    [Fact]
    public async Task ManualAdvanceTargetPhase_TransitionsImmediately()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Committed;

        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            ManualAdvanceTargetPhase = NarrativePhase.Approaching
        });

        Assert.True(result.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, result.TargetPhase);
        Assert.Equal("MANUAL_NEXT_PHASE", result.Reason);
    }

    [Fact]
    public async Task ManualAdvanceTargetPhase_DoesNotAllowBackwardTransition()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Approaching;

        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            ManualAdvanceTargetPhase = NarrativePhase.Committed
        });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, result.TargetPhase);
    }

    [Fact]
    public async Task ResetToBuildUp_ExecuteResetPreservesContinuityRelevantState()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Reset;
        state.CycleIndex = 4;
        state.ActiveFormulaVersion = "custom-v2";
        var snapshot = state.CharacterSnapshots[0];

        var reset = await _service.ExecuteResetAsync(state, ResetReason.Completion);
        var decayed = reset.CharacterSnapshots[0];

        Assert.Equal(NarrativePhase.BuildUp, reset.CurrentPhase);
        Assert.Equal(5, reset.CycleIndex);
        Assert.Equal("custom-v2", reset.ActiveFormulaVersion);
        Assert.Single(reset.CharacterSnapshots);
        Assert.Equal(snapshot.CharacterId, reset.CharacterSnapshots[0].CharacterId);
        Assert.True(decayed.Desire < snapshot.Desire);
        Assert.True(decayed.Tension <= snapshot.Tension);
        Assert.True(decayed.Dominance <= snapshot.Dominance);
        Assert.Equal(snapshot.Connection, decayed.Connection);
        Assert.Equal(snapshot.Loyalty, decayed.Loyalty);
        Assert.Equal(snapshot.SelfRespect, decayed.SelfRespect);
    }

    [Fact]
    public async Task ResetDecay_StrongerForHigherElevatedDesire()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "session-2",
            CurrentPhase = NarrativePhase.Reset,
            ActiveFormulaVersion = "rpv2-default",
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-high",
                    Desire = 95,
                    Restraint = 20,
                    Tension = 85,
                    Connection = 60,
                    Dominance = 80,
                    Loyalty = 55,
                    SelfRespect = 58
                },
                new CharacterStatProfileV2
                {
                    CharacterId = "char-mid",
                    Desire = 65,
                    Restraint = 80,
                    Tension = 60,
                    Connection = 60,
                    Dominance = 60,
                    Loyalty = 55,
                    SelfRespect = 58
                }
            ]
        };

        var reset = await _service.ExecuteResetAsync(state, ResetReason.Completion);
        var high = reset.CharacterSnapshots.Single(x => x.CharacterId == "char-high");
        var mid = reset.CharacterSnapshots.Single(x => x.CharacterId == "char-mid");

        Assert.True((95 - high.Desire) > (65 - mid.Desire));
        Assert.Equal(30, high.Restraint);
        Assert.Equal(70, mid.Restraint);
    }

    private static AdaptiveScenarioState CreateState() => new()
    {
        SessionId = "session-1",
        CurrentPhase = NarrativePhase.BuildUp,
        ActiveFormulaVersion = "rpv2-default",
        CharacterSnapshots =
        [
            new CharacterStatProfileV2 { CharacterId = "char-a", Desire = 80, Restraint = 30, Tension = 50, Connection = 55, Dominance = 50, Loyalty = 50, SelfRespect = 50 }
        ]
    };

    private sealed class StubNarrativeGateProfileService : INarrativeGateProfileService
    {
        private readonly NarrativeGateProfile _profile = new()
        {
            Id = "default-profile",
            Name = "Defaults",
            IsDefault = true,
            Rules =
            [
                new() { SortOrder = 1, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 60m },
                new() { SortOrder = 2, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.AverageDesire, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 65m },
                new() { SortOrder = 3, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.AverageRestraint, Comparator = NarrativeGateComparators.LessThanOrEqual, Threshold = 45m },
                new() { SortOrder = 4, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 3m },
                new() { SortOrder = 5, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 80m },
                new() { SortOrder = 6, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.AverageDesire, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 75m },
                new() { SortOrder = 7, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.AverageRestraint, Comparator = NarrativeGateComparators.LessThanOrEqual, Threshold = 35m },
                new() { SortOrder = 8, FromPhase = "Climax", ToPhase = "Reset", MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 12m }
            ]
        };

        public Task<NarrativeGateProfile> SaveAsync(NarrativeGateProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task<List<NarrativeGateProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<NarrativeGateProfile> { _profile });

        public Task<NarrativeGateProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<NarrativeGateProfile?>(_profile);

        public Task<NarrativeGateProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<NarrativeGateProfile?>(_profile);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
