using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ConceptInjectionDeterminismTests
{
    private readonly ConceptInjectionService _service = new(NullLogger<ConceptInjectionService>.Instance);

    [Fact]
    public async Task DeterministicSelection_IsStableForIdenticalInput()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(70, 40, 60);
        var context = BuildContext();

        var first = await _service.BuildGuidanceAsync(state, context);
        var second = await _service.BuildGuidanceAsync(state, context);

        Assert.Equal(first.SelectedConcepts.Select(x => x.ConceptId), second.SelectedConcepts.Select(x => x.ConceptId));
    }

    [Fact]
    public async Task ConflictResolution_PrefersHighestPriorityPerConceptId()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(70, 40, 60);
        var context = BuildContext();

        var result = await _service.BuildGuidanceAsync(state, context);

        Assert.Single(result.SelectedConcepts.Where(x => x.ConceptId == "concept-1"));
        Assert.Equal(10, result.SelectedConcepts.First(x => x.ConceptId == "concept-1").Priority);
    }

    [Fact]
    public async Task ReservedQuotaAndOverflow_RespectsBudgetCap()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(70, 40, 60);
        var context = new ConceptInjectionContext
        {
            BudgetCap = 3,
            ReservedScenarioQuota = 1,
            ReservedWillingnessQuota = 1,
            Concepts = BuildContext().Concepts
        };

        var result = await _service.BuildGuidanceAsync(state, context);

        Assert.Equal(3, result.BudgetUsed);
        Assert.Equal(3, result.SelectedConcepts.Count);
    }

    private static ConceptInjectionContext BuildContext() => new()
    {
        BudgetCap = 5,
        ReservedScenarioQuota = 1,
        ReservedWillingnessQuota = 1,
        Concepts =
        [
            new BehavioralConcept { ConceptId = "concept-1", Category = "Scenario", Priority = 10, GuidanceText = "A", TriggerConditions = "{}", IsEnabled = true },
            new BehavioralConcept { ConceptId = "concept-1", Category = "Scenario", Priority = 2, GuidanceText = "B", TriggerConditions = "{}", IsEnabled = true },
            new BehavioralConcept { ConceptId = "concept-2", Category = "Willingness", Priority = 9, GuidanceText = "C", TriggerConditions = "{}", IsEnabled = true },
            new BehavioralConcept { ConceptId = "concept-3", Category = "Other", Priority = 8, GuidanceText = "D", TriggerConditions = "{}", IsEnabled = true },
            new BehavioralConcept { ConceptId = "concept-4", Category = "Other", Priority = 7, GuidanceText = "E", TriggerConditions = "{}", IsEnabled = true }
        ]
    };
}
