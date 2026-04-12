using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class PhaseLifecycleTransitionTests
{
    private readonly ScenarioLifecycleService _service = new(NullLogger<ScenarioLifecycleService>.Instance);

    [Fact]
    public async Task ValidLifecycleTransitionSequence_ProgressesInOrder()
    {
        var state = CreateState();

        var toCommitted = await _service.EvaluateTransitionAsync(state, new LifecycleInputs { ActiveScenarioConfidence = 0.7m });
        state.CurrentPhase = toCommitted.TargetPhase;

        var toApproaching = await _service.EvaluateTransitionAsync(state, new LifecycleInputs { InteractionsSinceCommitment = 2, ActiveScenarioConfidence = 0.8m });
        state.CurrentPhase = toApproaching.TargetPhase;

        var toClimax = await _service.EvaluateTransitionAsync(state, new LifecycleInputs { InteractionsSinceCommitment = 4, ActiveScenarioFitScore = 80m });
        state.CurrentPhase = toClimax.TargetPhase;

        var toReset = await _service.EvaluateTransitionAsync(state, new LifecycleInputs { InteractionsSinceCommitment = 5 });
        state.CurrentPhase = toReset.TargetPhase;

        var toBuildUp = await _service.EvaluateTransitionAsync(state, new LifecycleInputs());

        Assert.True(toCommitted.Transitioned);
        Assert.Equal(NarrativePhase.Committed, toCommitted.TargetPhase);
        Assert.Equal(NarrativePhase.Approaching, toApproaching.TargetPhase);
        Assert.Equal(NarrativePhase.Climax, toClimax.TargetPhase);
        Assert.Equal(NarrativePhase.Reset, toReset.TargetPhase);
        Assert.Equal(NarrativePhase.BuildUp, toBuildUp.TargetPhase);
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
    public async Task ResetToBuildUp_ExecuteResetPreservesContinuityRelevantState()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Reset;
        state.CycleIndex = 4;
        state.ActiveFormulaVersion = "custom-v2";
        var snapshot = state.CharacterSnapshots[0];

        var reset = await _service.ExecuteResetAsync(state, ResetReason.Completion);

        Assert.Equal(NarrativePhase.BuildUp, reset.CurrentPhase);
        Assert.Equal(5, reset.CycleIndex);
        Assert.Equal("custom-v2", reset.ActiveFormulaVersion);
        Assert.Single(reset.CharacterSnapshots);
        Assert.Equal(snapshot.CharacterId, reset.CharacterSnapshots[0].CharacterId);
    }

    private static AdaptiveScenarioState CreateState() => new()
    {
        SessionId = "session-1",
        CurrentPhase = NarrativePhase.BuildUp,
        ActiveFormulaVersion = "rpv2-default",
        CharacterSnapshots =
        [
            new CharacterStatProfileV2 { CharacterId = "char-a", Desire = 60, Restraint = 40, Tension = 50, Connection = 55, Dominance = 50, Loyalty = 50, SelfRespect = 50 }
        ]
    };
}
