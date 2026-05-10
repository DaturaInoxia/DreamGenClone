using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IThemeMachineEvaluator
{
    Task<ThemeMachineEvaluationResult> EvaluateAsync(
        AdaptiveScenarioState adaptiveState,
        ThemeMachineEvaluationContext context,
        CancellationToken cancellationToken = default);
}