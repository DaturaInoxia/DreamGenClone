using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ScenarioLifecycleGateProfileTests
{
    [Fact]
    public async Task CommittedToApproaching_RequiresAllConfiguredGates()
    {
        var profileService = new StubNarrativeGateProfileService();
        var service = new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance, profileService);
        var state = CreateState(NarrativePhase.Committed, desire: 70, restraint: 40);

        var pass = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            InteractionsSinceCommitment = 3,
            ActiveScenarioFitScore = 61m
        });

        Assert.True(pass.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, pass.TargetPhase);

        var fail = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            InteractionsSinceCommitment = 2,
            ActiveScenarioFitScore = 61m
        });

        Assert.False(fail.Transitioned);
        Assert.Equal(NarrativePhase.Committed, fail.TargetPhase);
    }

    [Fact]
    public async Task ApproachingToClimax_UsesConfiguredDesireAndRestraintThresholds()
    {
        var profileService = new StubNarrativeGateProfileService();
        var service = new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance, profileService);

        var failingState = CreateState(NarrativePhase.Approaching, desire: 74, restraint: 30);
        var failing = await service.EvaluateTransitionAsync(failingState, new LifecycleInputs
        {
            InteractionsSinceCommitment = 99,
            ActiveScenarioFitScore = 85m
        });

        Assert.False(failing.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, failing.TargetPhase);

        var passingState = CreateState(NarrativePhase.Approaching, desire: 76, restraint: 35);
        var passing = await service.EvaluateTransitionAsync(passingState, new LifecycleInputs
        {
            InteractionsSinceCommitment = 1,
            ActiveScenarioFitScore = 85m
        });

        Assert.True(passing.Transitioned);
        Assert.Equal(NarrativePhase.Climax, passing.TargetPhase);
    }

    [Fact]
    public async Task ClimaxToReset_UsesConfiguredInteractionThreshold()
    {
        var profileService = new StubNarrativeGateProfileService();
        var service = new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance, profileService);

        var state = CreateState(NarrativePhase.Climax, desire: 80, restraint: 20);
        var blocked = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            InteractionsSinceCommitment = 11
        });

        Assert.False(blocked.Transitioned);
        Assert.Equal(NarrativePhase.Climax, blocked.TargetPhase);

        var passed = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            InteractionsSinceCommitment = 12
        });

        Assert.True(passed.Transitioned);
        Assert.Equal(NarrativePhase.Reset, passed.TargetPhase);
    }

    private static AdaptiveScenarioState CreateState(NarrativePhase phase, int desire, int restraint)
    {
        return new AdaptiveScenarioState
        {
            SessionId = "session-gates",
            CurrentPhase = phase,
            ActiveFormulaVersion = "rpv2-default",
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-1",
                    Desire = desire,
                    Restraint = restraint,
                    Tension = 50,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                },
                new CharacterStatProfileV2
                {
                    CharacterId = "char-2",
                    Desire = desire,
                    Restraint = restraint,
                    Tension = 55,
                    Connection = 52,
                    Dominance = 49,
                    Loyalty = 50,
                    SelfRespect = 51
                }
            ]
        };
    }

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
