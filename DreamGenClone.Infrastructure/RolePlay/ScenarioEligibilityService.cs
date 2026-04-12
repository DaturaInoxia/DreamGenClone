using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Infrastructure.RolePlay;

internal static class ScenarioEligibilityService
{
    public static string ResolveWillingnessTier(AdaptiveScenarioState state)
    {
        var avgDesire = state.CharacterSnapshots.Count == 0
            ? 0
            : state.CharacterSnapshots.Average(x => x.Desire);

        return avgDesire switch
        {
            >= 75 => "High",
            >= 55 => "Medium",
            >= 35 => "Low",
            _ => "Blocked"
        };
    }

    public static bool IsEligible(AdaptiveScenarioState state)
    {
        if (state.CharacterSnapshots.Count == 0)
        {
            return false;
        }

        var avgRestraint = state.CharacterSnapshots.Average(x => x.Restraint);
        var avgTension = state.CharacterSnapshots.Average(x => x.Tension);
        return avgTension >= 35 && avgRestraint <= 80;
    }

    public static decimal ComputeFitScore(AdaptiveScenarioState state, ScenarioDefinition scenario)
    {
        if (state.CharacterSnapshots.Count == 0)
        {
            return 0m;
        }

        var avgDesire = (decimal)state.CharacterSnapshots.Average(x => x.Desire);
        var avgConnection = (decimal)state.CharacterSnapshots.Average(x => x.Connection);
        var avgTension = (decimal)state.CharacterSnapshots.Average(x => x.Tension);

        var score = (avgDesire * 0.45m) + (avgConnection * 0.25m) + (avgTension * 0.30m) + scenario.Priority;
        return decimal.Round(score, 3, MidpointRounding.AwayFromZero);
    }
}
