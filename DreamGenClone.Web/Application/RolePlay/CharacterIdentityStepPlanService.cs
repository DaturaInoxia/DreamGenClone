using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves the persisted step plan for a target kind, validating it as it is read. The plan is data, so the
/// read is the only place that can catch a row naming a handler the code does not implement or a step that
/// appears twice; both fail fast here rather than letting a build walk a step nothing can run.
/// </summary>
public sealed class CharacterIdentityStepPlanService : ICharacterIdentityStepPlanService
{
    private readonly ICharacterIdentityBuildRepository _repository;

    public CharacterIdentityStepPlanService(ICharacterIdentityBuildRepository repository)
    {
        _repository = repository;
    }

    public async Task<CharacterIdentityStepPlan> GetPlanAsync(
        CharacterIdentityTargetKind kind, CancellationToken cancellationToken = default)
    {
        var definitions = await _repository.ListStepPlanAsync(kind, cancellationToken);
        if (definitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"No character identity step plan is seeded for target kind '{kind}'. The plan defines the steps, "
                + "their order, the handler that runs each one and the prompt template that handler resolves, so a "
                + "build cannot start without it.");
        }

        var ordered = definitions.OrderBy(definition => definition.Order).ToList();

        // The table's key already makes a step unique within a kind (PK Kind+Step), so the order is the only
        // thing that can be ambiguous — and an ambiguous order would let two boots disagree about the pipeline.
        var ambiguous = ordered
            .GroupBy(definition => definition.Order)
            .FirstOrDefault(group => group.Count() > 1);
        if (ambiguous is not null)
        {
            throw new InvalidOperationException(
                $"The step plan for target kind '{kind}' gives {ambiguous.Count()} steps the same order "
                + $"({ambiguous.Key}): {string.Join(", ", ambiguous.Select(definition => definition.Step))}. "
                + "The pipeline order would be ambiguous.");
        }

        foreach (var definition in ordered)
        {
            CharacterIdentityBuildHandlers.RequireKnown(definition.HandlerKey, kind, definition.Step);
        }

        return new CharacterIdentityStepPlan(kind, ordered);
    }
}
