namespace DreamGenClone.Application.RolePlay;

public interface IScenarioLifecycleService
{
	Task<PhaseTransitionResult> EvaluateTransitionAsync(
		DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
		LifecycleInputs inputs,
		CancellationToken cancellationToken = default);

	Task<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState> ExecuteResetAsync(
		DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
		ResetReason reason,
		CancellationToken cancellationToken = default);
}
