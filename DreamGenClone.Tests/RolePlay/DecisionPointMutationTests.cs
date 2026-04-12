using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class DecisionPointMutationTests
{
    private readonly DecisionPointService _service = new(NullLogger<DecisionPointService>.Instance);

    [Fact]
    public async Task TriggeredDecisionPoint_GeneratesOptions()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(70, 40, 60);
        state.ActiveScenarioId = "scenario-1";
        state.InteractionCountInPhase = 3;

        var point = await _service.TryCreateDecisionPointAsync(state, DecisionTrigger.InteractionStart);

        Assert.NotNull(point);
        Assert.NotEmpty(point!.OptionIds);
    }

    [Fact]
    public async Task ApplyDecision_PersistsStatDeltaMutations()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(50, 50, 50);
        state.ActiveScenarioId = "scenario-1";

        var outcome = await _service.ApplyDecisionAsync(state, new DecisionSubmission
        {
            DecisionPointId = "dp-1",
            OptionId = "lean-in",
            ActorName = "Tester"
        });

        Assert.True(outcome.Applied);
        Assert.True(state.CharacterSnapshots[0].Desire > 50);
        Assert.True(state.CharacterSnapshots[0].Restraint < 50);
    }

    [Fact]
    public async Task CustomResponseFallback_ParsesDeltaMap()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(50, 50, 50);
        state.ActiveScenarioId = "scenario-1";

        var outcome = await _service.ApplyDecisionAsync(state, new DecisionSubmission
        {
            DecisionPointId = "dp-2",
            OptionId = "custom",
            CustomResponseText = "Connection:4,SelfRespect:2",
            ActorName = "Tester"
        });

        Assert.Equal(4, outcome.AppliedStatDeltas["Connection"]);
        Assert.Equal(2, outcome.AppliedStatDeltas["SelfRespect"]);
    }
}
