namespace DreamGenClone.Application.RolePlay;

public interface IScenarioSelectionService
{
	Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>> EvaluateCandidatesAsync(
		DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
		IReadOnlyList<ScenarioDefinition> candidates,
		CancellationToken cancellationToken = default);

	Task<ScenarioCommitResult> TryCommitScenarioAsync(
		DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
		IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation> evaluations,
		CancellationToken cancellationToken = default);
}
