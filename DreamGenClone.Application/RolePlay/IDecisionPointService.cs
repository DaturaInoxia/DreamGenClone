namespace DreamGenClone.Application.RolePlay;

public interface IDecisionPointService
{
	Task<DreamGenClone.Domain.RolePlay.DecisionPoint?> TryCreateDecisionPointAsync(
		DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
		DecisionTrigger trigger,
		CancellationToken cancellationToken = default);

	Task<DecisionOutcome> ApplyDecisionAsync(
		DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
		DecisionSubmission submission,
		CancellationToken cancellationToken = default);
}
