using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ThemeMachineEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_SelectsHighestPriorityEligibleTransition()
    {
        var evaluator = new ThemeMachineEvaluator(NullLogger<ThemeMachineEvaluator>.Instance);
        var now = DateTime.UtcNow;

        var context = new ThemeMachineEvaluationContext
        {
            SessionId = "session-1",
            ActiveScenarioId = "theme-1",
            ThemeId = "theme-1",
            Snapshot = new ThemeMachineSessionSnapshot
            {
                MachineKey = "infidelity-brief-disappearance",
                ThemeId = "theme-1",
                DefinitionId = "def-1",
                DefinitionVersion = 1,
                CurrentStateCode = "PublicBaseline",
                TurnsInCurrentState = 1,
                ReturnBeatCompleted = false,
                LastEvaluatedUtc = now
            },
            Transitions =
            [
                new RPThemeMachineTransition
                {
                    TransitionId = "t-low",
                    FromStateCode = "PublicBaseline",
                    ToStateCode = "EncounterInProgress",
                    Priority = 10,
                    TriggerType = "always",
                    GateConfigJson = "{}",
                    BlockReasonCode = "LowPriority"
                },
                new RPThemeMachineTransition
                {
                    TransitionId = "t-high",
                    FromStateCode = "PublicBaseline",
                    ToStateCode = "ReturnBeatRequired",
                    Priority = 20,
                    TriggerType = "always",
                    GateConfigJson = "{}",
                    BlockReasonCode = "HighPriority"
                }
            ]
        };

        var result = await evaluator.EvaluateAsync(new AdaptiveScenarioState { SessionId = "session-1" }, context);

        Assert.True(result.TransitionApplied);
        Assert.Equal("t-high", result.AppliedTransitionId);
        Assert.Equal("ReturnBeatRequired", result.UpdatedSnapshot.CurrentStateCode);
        Assert.Equal(0, result.UpdatedSnapshot.TurnsInCurrentState);
        Assert.Equal("t-high", result.UpdatedSnapshot.LastTransitionId);
        Assert.Equal("ThemeMachineTransitionApplied", result.UpdatedSnapshot.LastTransitionReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_CooldownTransitionBlockedUntilBothRequirementsMet()
    {
        var evaluator = new ThemeMachineEvaluator(NullLogger<ThemeMachineEvaluator>.Instance);
        var now = DateTime.UtcNow;

        var cooldownTransition = new RPThemeMachineTransition
        {
            TransitionId = "cooldown-transition",
            FromStateCode = "ReintegrationCooldown",
            ToStateCode = "NextDisappearanceEligible",
            Priority = 5,
            TriggerType = "cooldown-eligibility",
            GateConfigJson = JsonSerializer.Serialize(new
            {
                minimumInteractions = 3,
                requireReturnBeatCompleted = true,
                returnBeatCompletionSignals = new[] { "returned to the room" },
                returnBeatTransgressorRole = "Wife",
                returnBeatPartnerRole = "Husband"
            }),
            BlockReasonCode = "ReintegrationCooldownGateBlocked"
        };

        var blockedContext = new ThemeMachineEvaluationContext
        {
            SessionId = "session-1",
            ActiveScenarioId = "theme-1",
            ThemeId = "theme-1",
            Snapshot = new ThemeMachineSessionSnapshot
            {
                MachineKey = "infidelity-brief-disappearance",
                ThemeId = "theme-1",
                DefinitionId = "def-1",
                DefinitionVersion = 1,
                CurrentStateCode = "ReintegrationCooldown",
                TurnsInCurrentState = 2,
                ReturnBeatCompleted = false,
                LastEvaluatedUtc = now
            },
            Transitions = [cooldownTransition]
        };

        var blockedResult = await evaluator.EvaluateAsync(new AdaptiveScenarioState { SessionId = "session-1" }, blockedContext);

        Assert.False(blockedResult.TransitionApplied);
        Assert.Equal("ReintegrationCooldown", blockedResult.UpdatedSnapshot.CurrentStateCode);
        Assert.True(blockedResult.Directive.BlockDisappearanceCandidates);
        Assert.Contains("ReintegrationCooldownGateBlocked", blockedResult.Directive.ReasonCodes);

        var eligibleContext = new ThemeMachineEvaluationContext
        {
            SessionId = "session-1",
            ActiveScenarioId = "theme-1",
            ThemeId = "theme-1",
            Snapshot = new ThemeMachineSessionSnapshot
            {
                MachineKey = "infidelity-brief-disappearance",
                ThemeId = "theme-1",
                DefinitionId = "def-1",
                DefinitionVersion = 1,
                CurrentStateCode = "ReintegrationCooldown",
                TurnsInCurrentState = 3,
                ReturnBeatCompleted = true,
                LastEvaluatedUtc = now
            },
            Transitions = [cooldownTransition]
        };

        var eligibleResult = await evaluator.EvaluateAsync(new AdaptiveScenarioState { SessionId = "session-1" }, eligibleContext);

        Assert.True(eligibleResult.TransitionApplied);
        Assert.Equal("NextDisappearanceEligible", eligibleResult.UpdatedSnapshot.CurrentStateCode);
        Assert.False(eligibleResult.Directive.BlockDisappearanceCandidates);
    }

    [Fact]
    public async Task EvaluateAsync_CooldownTransitionThrows_WhenReturnBeatSignalsMissing()
    {
        var evaluator = new ThemeMachineEvaluator(NullLogger<ThemeMachineEvaluator>.Instance);
        var now = DateTime.UtcNow;

        var cooldownTransition = new RPThemeMachineTransition
        {
            TransitionId = "cooldown-transition",
            FromStateCode = "ReintegrationCooldown",
            ToStateCode = "NextDisappearanceEligible",
            Priority = 5,
            TriggerType = "cooldown-eligibility",
            GateConfigJson = JsonSerializer.Serialize(new
            {
                minimumInteractions = 3,
                requireReturnBeatCompleted = true
            }),
            BlockReasonCode = "ReintegrationCooldownGateBlocked"
        };

        var context = new ThemeMachineEvaluationContext
        {
            SessionId = "session-1",
            ActiveScenarioId = "theme-1",
            ThemeId = "theme-1",
            Snapshot = new ThemeMachineSessionSnapshot
            {
                MachineKey = "infidelity-brief-disappearance",
                ThemeId = "theme-1",
                DefinitionId = "def-1",
                DefinitionVersion = 1,
                CurrentStateCode = "ReintegrationCooldown",
                TurnsInCurrentState = 2,
                ReturnBeatCompleted = false,
                LastEvaluatedUtc = now
            },
            Transitions = [cooldownTransition]
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => evaluator.EvaluateAsync(new AdaptiveScenarioState { SessionId = "session-1" }, context));

        Assert.Contains("returnBeatCompletionSignals", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAsync_EnteringEncounterResetsReturnBeatCompletionFlag()
    {
        var evaluator = new ThemeMachineEvaluator(NullLogger<ThemeMachineEvaluator>.Instance);
        var now = DateTime.UtcNow;

        var context = new ThemeMachineEvaluationContext
        {
            SessionId = "session-1",
            ActiveScenarioId = "theme-1",
            ThemeId = "theme-1",
            Snapshot = new ThemeMachineSessionSnapshot
            {
                MachineKey = "infidelity-brief-disappearance",
                ThemeId = "theme-1",
                DefinitionId = "def-1",
                DefinitionVersion = 1,
                CurrentStateCode = "NextDisappearanceEligible",
                TurnsInCurrentState = 4,
                ReturnBeatCompleted = true,
                LastEvaluatedUtc = now
            },
            Transitions =
            [
                new RPThemeMachineTransition
                {
                    TransitionId = "t-next-encounter",
                    FromStateCode = "NextDisappearanceEligible",
                    ToStateCode = "EncounterInProgress",
                    Priority = 10,
                    TriggerType = "always",
                    GateConfigJson = "{}",
                    BlockReasonCode = "MachineGateBlocked"
                }
            ]
        };

        var result = await evaluator.EvaluateAsync(new AdaptiveScenarioState { SessionId = "session-1" }, context);

        Assert.True(result.TransitionApplied);
        Assert.Equal("EncounterInProgress", result.UpdatedSnapshot.CurrentStateCode);
        Assert.False(result.UpdatedSnapshot.ReturnBeatCompleted);
    }
}
