using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Infrastructure.RolePlay;

internal static class ScenarioEligibilityService
{
    public static string ResolveWillingnessTier(AdaptiveScenarioState state, ScenarioEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var avgDesire = state.CharacterSnapshots.Count == 0
            ? 0
            : state.CharacterSnapshots.Average(x => x.Desire);

        var highMin = settings.StageAHighDesireMin;
        var medMin = settings.StageAMediumDesireMin;
        var lowMin = settings.StageALowDesireMin;

        if (avgDesire >= highMin) return "High";
        if (avgDesire >= medMin) return "Medium";
        if (avgDesire >= lowMin) return "Low";
        return "Blocked";
    }

    public static decimal ComputeFitScore(AdaptiveScenarioState state, ScenarioDefinition scenario, ScenarioEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (state.CharacterSnapshots.Count == 0)
        {
            return 0m;
        }

        var avgDesire = (decimal)state.CharacterSnapshots.Average(x => x.Desire);
        var avgConnection = (decimal)state.CharacterSnapshots.Average(x => x.Connection);
        var avgTension = (decimal)state.CharacterSnapshots.Average(x => x.Tension);

        var desireWeight = (decimal)settings.LegacyFitDesireWeight;
        var connectionWeight = (decimal)settings.LegacyFitConnectionWeight;
        var tensionWeight = (decimal)settings.LegacyFitTensionWeight;

        var score = (avgDesire * desireWeight) + (avgConnection * connectionWeight) + (avgTension * tensionWeight) + scenario.Priority;
        return decimal.Round(score, 3, MidpointRounding.AwayFromZero);
    }
}
