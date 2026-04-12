using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayPromptComposer
{
    public IReadOnlyList<DecisionTrigger> ResolveConceptInjectionTriggers(
        DecisionTrigger initialTrigger,
        bool phaseChanged,
        bool significantStatChange)
    {
        var triggers = new List<DecisionTrigger> { initialTrigger };

        if (phaseChanged && !triggers.Contains(DecisionTrigger.PhaseChanged))
        {
            triggers.Add(DecisionTrigger.PhaseChanged);
        }

        if (significantStatChange && !triggers.Contains(DecisionTrigger.SignificantStatChange))
        {
            triggers.Add(DecisionTrigger.SignificantStatChange);
        }

        return triggers;
    }

    public ConceptInjectionContext BuildConceptContext(
        IReadOnlyList<BehavioralConcept> concepts,
        DecisionTrigger trigger,
        int budgetCap = 8)
    {
        return new ConceptInjectionContext
        {
            Concepts = concepts,
            BudgetCap = budgetCap,
            Trigger = trigger.ToString()
        };
    }
}
