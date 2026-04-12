using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ConceptInjectionService : IConceptInjectionService
{
    private readonly ILogger<ConceptInjectionService> _logger;

    public ConceptInjectionService(ILogger<ConceptInjectionService> logger)
    {
        _logger = logger;
    }

    public Task<ConceptInjectionResult> BuildGuidanceAsync(
        AdaptiveScenarioState state,
        ConceptInjectionContext context,
        CancellationToken cancellationToken = default)
    {
        var budget = Math.Max(1, context.BudgetCap);
        var enabled = context.Concepts
            .Where(c => c.IsEnabled)
            .GroupBy(c => c.ConceptId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.Priority).ThenBy(x => x.ConceptId, StringComparer.Ordinal).First())
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.ConceptId, StringComparer.Ordinal)
            .ToList();

        var scenarioReserved = enabled.Where(x => string.Equals(x.Category, "Scenario", StringComparison.OrdinalIgnoreCase))
            .Take(Math.Max(0, context.ReservedScenarioQuota))
            .ToList();
        var willingnessReserved = enabled.Where(x => string.Equals(x.Category, "Willingness", StringComparison.OrdinalIgnoreCase))
            .Take(Math.Max(0, context.ReservedWillingnessQuota))
            .ToList();

        var selected = new List<BehavioralConcept>();
        selected.AddRange(scenarioReserved);
        foreach (var concept in willingnessReserved)
        {
            if (selected.All(x => !string.Equals(x.ConceptId, concept.ConceptId, StringComparison.Ordinal)))
            {
                selected.Add(concept);
            }
        }

        var overflow = enabled.Where(c => selected.All(x => !string.Equals(x.ConceptId, c.ConceptId, StringComparison.Ordinal)));
        foreach (var concept in overflow)
        {
            if (selected.Count >= budget)
            {
                break;
            }

            selected.Add(concept);
        }

        var ordered = selected
            .Take(budget)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.ConceptId, StringComparer.Ordinal)
            .ToList();

        _logger.LogInformation(
            RolePlayV2LogEvents.ConceptInjectionBuilt,
            state.SessionId,
            ordered.Count,
            ordered.Count,
            budget);

        return Task.FromResult(new ConceptInjectionResult
        {
            SelectedConcepts = ordered,
            BudgetCap = budget,
            BudgetUsed = ordered.Count,
            Rationale = $"Trigger={context.Trigger}; reserved+overflow selection produced {ordered.Count} concepts."
        });
    }
}
