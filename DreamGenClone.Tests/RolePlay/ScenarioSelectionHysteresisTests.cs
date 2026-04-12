using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ScenarioSelectionHysteresisTests
{
    private readonly ScenarioSelectionService _service = new(NullLogger<ScenarioSelectionService>.Instance);

    [Fact]
    public async Task TwoStageGatingAndRanking_ProducesEligibleOrderedCandidates()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(desire: 72, restraint: 40, tension: 55);
        var candidates = RolePlayV2AcceptanceFixtureData.BuildCompetingScenarioSignals();

        var evaluations = await _service.EvaluateCandidatesAsync(state, candidates);

        Assert.Equal(3, evaluations.Count);
        Assert.All(evaluations, item => Assert.True(item.StageBEligible));
        Assert.True(evaluations[0].FitScore >= evaluations[1].FitScore);
    }

    [Fact]
    public async Task NearTie_HysteresisRequiresConsecutiveLeadBeforeCommit()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(desire: 60, restraint: 45, tension: 50);
        state.ConsecutiveLeadCount = 0;
        var candidates =
            new[]
            {
                new DreamGenClone.Application.RolePlay.ScenarioDefinition("A", "A", 0),
                new DreamGenClone.Application.RolePlay.ScenarioDefinition("B", "B", 0)
            };

        var evaluations = await _service.EvaluateCandidatesAsync(state, candidates);
        var firstResult = await _service.TryCommitScenarioAsync(state, evaluations);

        state.ConsecutiveLeadCount = firstResult.UpdatedConsecutiveLeadCount;
        var secondResult = await _service.TryCommitScenarioAsync(state, evaluations);

        Assert.False(firstResult.Committed);
        Assert.True(secondResult.Committed);
    }

    [Fact]
    public async Task DeterministicTieBreakOrdering_IsStableAcrossRuns()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(desire: 65, restraint: 45, tension: 55);
        var candidates =
            new[]
            {
                new DreamGenClone.Application.RolePlay.ScenarioDefinition("B", "B", 1),
                new DreamGenClone.Application.RolePlay.ScenarioDefinition("A", "A", 1)
            };

        var first = await _service.EvaluateCandidatesAsync(state, candidates);
        var second = await _service.EvaluateCandidatesAsync(state, candidates);

        Assert.Equal(first.Select(x => x.ScenarioId), second.Select(x => x.ScenarioId));
    }
}
