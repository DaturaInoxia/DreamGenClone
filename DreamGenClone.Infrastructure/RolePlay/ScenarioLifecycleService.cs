using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioLifecycleService : IScenarioLifecycleService
{
    private readonly ILogger<ScenarioLifecycleService> _logger;

    public ScenarioLifecycleService(ILogger<ScenarioLifecycleService> logger)
    {
        _logger = logger;
    }

    public Task<PhaseTransitionResult> EvaluateTransitionAsync(
        AdaptiveScenarioState state,
        LifecycleInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var (transitioned, targetPhase, triggerType, reasonCode) = ResolveTransition(state, inputs);
        if (!transitioned)
        {
            return Task.FromResult(new PhaseTransitionResult
            {
                Transitioned = false,
                TargetPhase = state.CurrentPhase,
                Reason = "No transition criteria were met."
            });
        }

        var transitionEvent = new NarrativePhaseTransitionEvent
        {
            TransitionId = Guid.NewGuid().ToString("N"),
            SessionId = state.SessionId,
            FromPhase = state.CurrentPhase,
            ToPhase = targetPhase,
            TriggerType = triggerType,
            ReasonCode = reasonCode,
            EvidencePayload = JsonSerializer.Serialize(new
            {
                inputs.InteractionsSinceCommitment,
                inputs.ActiveScenarioConfidence,
                inputs.ActiveScenarioFitScore,
                inputs.EvidenceSummary,
                inputs.ForceReset,
                inputs.ManualOverride
            }),
            OccurredUtc = DateTime.UtcNow
        };

        _logger.LogInformation(
            RolePlayV2LogEvents.PhaseTransitionApplied,
            transitionEvent.SessionId,
            transitionEvent.FromPhase,
            transitionEvent.ToPhase,
            transitionEvent.TriggerType,
            transitionEvent.ReasonCode);

        return Task.FromResult(new PhaseTransitionResult
        {
            Transitioned = true,
            TargetPhase = targetPhase,
            TransitionEvent = transitionEvent,
            Reason = reasonCode
        });
    }

    public Task<AdaptiveScenarioState> ExecuteResetAsync(
        AdaptiveScenarioState state,
        ResetReason reason,
        CancellationToken cancellationToken = default)
    {
        var resetState = new AdaptiveScenarioState
        {
            SessionId = state.SessionId,
            ActiveScenarioId = null,
            CurrentPhase = NarrativePhase.BuildUp,
            InteractionCountInPhase = 0,
            ConsecutiveLeadCount = 0,
            LastEvaluationUtc = DateTime.UtcNow,
            CycleIndex = state.CycleIndex + 1,
            ActiveFormulaVersion = state.ActiveFormulaVersion,
            CharacterSnapshots = [.. state.CharacterSnapshots]
        };

        _logger.LogInformation(
            "RolePlayV2 reset executed: SessionId={SessionId} NewCycleIndex={CycleIndex} Reason={Reason}",
            state.SessionId,
            resetState.CycleIndex,
            reason);

        return Task.FromResult(resetState);
    }

    private static (bool Transitioned, NarrativePhase TargetPhase, TransitionTriggerType TriggerType, string ReasonCode) ResolveTransition(
        AdaptiveScenarioState state,
        LifecycleInputs inputs)
    {
        if (inputs.ForceReset)
        {
            return (true, NarrativePhase.Reset, TransitionTriggerType.Reset, "FORCE_RESET");
        }

        return state.CurrentPhase switch
        {
            NarrativePhase.BuildUp when inputs.ActiveScenarioConfidence >= 0.55m
                => (true, NarrativePhase.Committed, TransitionTriggerType.Threshold, "BUILDUP_TO_COMMITTED"),

            NarrativePhase.Committed when inputs.InteractionsSinceCommitment >= 2 && inputs.ActiveScenarioConfidence >= 0.65m
                => (true, NarrativePhase.Approaching, TransitionTriggerType.InteractionCountGate, "COMMITTED_TO_APPROACHING"),

            NarrativePhase.Approaching when inputs.InteractionsSinceCommitment >= 4 && inputs.ActiveScenarioFitScore >= 72m
                => (true, NarrativePhase.Climax, TransitionTriggerType.Threshold, "APPROACHING_TO_CLIMAX"),

            NarrativePhase.Climax when inputs.InteractionsSinceCommitment >= 5 || inputs.ManualOverride
                => (true, NarrativePhase.Reset, inputs.ManualOverride ? TransitionTriggerType.Override : TransitionTriggerType.InteractionCountGate, "CLIMAX_TO_RESET"),

            NarrativePhase.Reset
                => (true, NarrativePhase.BuildUp, TransitionTriggerType.Reset, "RESET_TO_BUILDUP"),

            _ => (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION")
        };
    }
}
