using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ScenarioSelectionBuildUpGateProfileTests
{
    [Fact]
    public async Task TryCommitScenarioAsync_NoBuildUpProfileRules_KeepsCurrentFallbackBehavior()
    {
        var service = new ScenarioSelectionService(
            NullLogger<ScenarioSelectionService>.Instance,
            narrativeGateProfileService: new StubNarrativeGateProfileService());

        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(desire: 55, restraint: 45, tension: 50);
        state.CurrentPhase = NarrativePhase.BuildUp;
        state.InteractionCountInPhase = 2;

        var evaluations = new[]
        {
            new ScenarioCandidateEvaluation
            {
                SessionId = state.SessionId,
                ScenarioId = "only",
                StageBEligible = true,
                FitScore = 55m,
                Confidence = 0.55m,
                TieBreakKey = "001:only"
            }
        };

        var result = await service.TryCommitScenarioAsync(state, evaluations);

        Assert.True(result.Committed);
        Assert.Equal("only", result.ScenarioId);
    }

    [Fact]
    public async Task TryCommitScenarioAsync_BuildUpProfileRule_CanBlockCommit()
    {
        var profile = new NarrativeGateProfile
        {
            Id = "gate-profile-1",
            Name = "Strict BuildUp",
            Rules =
            [
                new NarrativeGateRule
                {
                    SortOrder = 1,
                    FromPhase = "BuildUp",
                    ToPhase = "Committed",
                    MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore,
                    Comparator = NarrativeGateComparators.GreaterThanOrEqual,
                    Threshold = 80m
                }
            ]
        };

        var service = new ScenarioSelectionService(
            NullLogger<ScenarioSelectionService>.Instance,
            narrativeGateProfileService: new StubNarrativeGateProfileService(profile));

        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(desire: 70, restraint: 35, tension: 60);
        state.CurrentPhase = NarrativePhase.BuildUp;
        state.InteractionCountInPhase = 3;
        state.SelectedNarrativeGateProfileId = profile.Id;

        var evaluations = new[]
        {
            new ScenarioCandidateEvaluation
            {
                SessionId = state.SessionId,
                ScenarioId = "only",
                StageBEligible = true,
                FitScore = 70m,
                Confidence = 0.70m,
                TieBreakKey = "001:only"
            }
        };

        var result = await service.TryCommitScenarioAsync(state, evaluations);

        Assert.False(result.Committed);
        Assert.Contains("BuildUp profile gate blocked commit", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubNarrativeGateProfileService : INarrativeGateProfileService
    {
        private readonly Dictionary<string, NarrativeGateProfile> _profiles;

        public StubNarrativeGateProfileService(params NarrativeGateProfile[] profiles)
        {
            _profiles = profiles.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        }

        public Task<NarrativeGateProfile> SaveAsync(NarrativeGateProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task<List<NarrativeGateProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.Values.ToList());

        public Task<NarrativeGateProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.TryGetValue(id, out var profile) ? profile : null);

        public Task<NarrativeGateProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.Values.FirstOrDefault());

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.Remove(id));
    }
}
