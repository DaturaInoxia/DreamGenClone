using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
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
        Assert.Contains("custom", point.OptionIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildUpPhase_UsesHiddenTransparency()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(45, 72, 40);
        state.ActiveScenarioId = "scenario-1";
        state.InteractionCountInPhase = 3;
        state.CurrentPhase = NarrativePhase.BuildUp;

        var point = await _service.TryCreateDecisionPointAsync(state, DecisionTrigger.InteractionStart);

        Assert.NotNull(point);
        Assert.Equal(TransparencyMode.Hidden, point!.TransparencyMode);
    }

    [Fact]
    public async Task HighDesireContext_ChangesOptionSet()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(78, 35, 75);
        state.ActiveScenarioId = "scenario-1";
        state.InteractionCountInPhase = 3;

        var point = await _service.TryCreateDecisionPointAsync(state, DecisionTrigger.SignificantStatChange);

        Assert.NotNull(point);
        Assert.Contains("test-boundary", point!.OptionIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("observe", point.OptionIds, StringComparer.OrdinalIgnoreCase);
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

    [Fact]
    public async Task TargetActor_OnlyMutatesTargetSnapshot()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "fixture-session",
            ActiveScenarioId = "scenario-1",
            CurrentPhase = NarrativePhase.Committed,
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "wife",
                    Desire = 50,
                    Restraint = 50,
                    Tension = 50,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                },
                new CharacterStatProfileV2
                {
                    CharacterId = "husband",
                    Desire = 50,
                    Restraint = 50,
                    Tension = 50,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                }
            ]
        };

        var outcome = await _service.ApplyDecisionAsync(
            state,
            new DecisionSubmission
            {
                DecisionPointId = "dp-3",
                OptionId = "lean-in",
                ActorName = "wife",
                TargetActorId = "wife"
            },
            targetActorId: "wife");

        Assert.True(outcome.Applied);
        Assert.Equal("wife", outcome.TargetActorId);
        Assert.True(state.CharacterSnapshots[0].Desire > 50);
        Assert.Equal(50, state.CharacterSnapshots[1].Desire);
    }
}
