namespace DreamGenClone.Application.RolePlay;

public interface IConceptInjectionService
{
	Task<ConceptInjectionResult> BuildGuidanceAsync(
		DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
		ConceptInjectionContext context,
		CancellationToken cancellationToken = default);
}
