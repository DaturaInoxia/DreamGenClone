using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Infrastructure.RolePlay;

internal static class ScenarioEligibilityService
{
    public static string ResolveWillingnessTier(AdaptiveScenarioState state, ScenarioEngineSettings? settings = null)
    {
        var avgDesire = state.CharacterSnapshots.Count == 0
            ? 0
            : state.CharacterSnapshots.Average(x => x.Desire);

        var highMin = settings?.StageAHighDesireMin ?? 75;
        var medMin = settings?.StageAMediumDesireMin ?? 55;
        var lowMin = settings?.StageALowDesireMin ?? 35;

        if (avgDesire >= highMin) return "High";
        if (avgDesire >= medMin) return "Medium";
        if (avgDesire >= lowMin) return "Low";
        return "Blocked";
    }

    public static bool IsEligible(AdaptiveScenarioState state, ScenarioEngineSettings? settings = null)
    {
        if (state.CharacterSnapshots.Count == 0)
        {
            return false;
        }

        var avgRestraint = state.CharacterSnapshots.Average(x => x.Restraint);
        var avgTension = state.CharacterSnapshots.Average(x => x.Tension);

        var minTension = settings?.StageBMinTension ?? 35;
        var maxRestraint = settings?.StageBMaxRestraint ?? 80;

        return avgTension >= minTension && avgRestraint <= maxRestraint;
    }

    public static decimal ComputeFitScore(AdaptiveScenarioState state, ScenarioDefinition scenario, ScenarioEngineSettings? settings = null)
    {
        if (state.CharacterSnapshots.Count == 0)
        {
            return 0m;
        }

        var avgDesire = (decimal)state.CharacterSnapshots.Average(x => x.Desire);
        var avgConnection = (decimal)state.CharacterSnapshots.Average(x => x.Connection);
        var avgTension = (decimal)state.CharacterSnapshots.Average(x => x.Tension);

        var desireWeight = (decimal)(settings?.LegacyFitDesireWeight ?? 0.45);
        var connectionWeight = (decimal)(settings?.LegacyFitConnectionWeight ?? 0.25);
        var tensionWeight = (decimal)(settings?.LegacyFitTensionWeight ?? 0.30);

        var score = (avgDesire * desireWeight) + (avgConnection * connectionWeight) + (avgTension * tensionWeight) + scenario.Priority;
        return decimal.Round(score, 3, MidpointRounding.AwayFromZero);
    }
}
