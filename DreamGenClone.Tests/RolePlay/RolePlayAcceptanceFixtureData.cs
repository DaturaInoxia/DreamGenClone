using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public static class RolePlayAcceptanceFixtureData
{
    public static AdaptiveScenarioState BuildBoundaryState(int desire, int restraint, int tension)
    {
        return new AdaptiveScenarioState
        {
            SessionId = "fixture-session",
            ActiveFormulaVersion = "rpv2-default",
            CurrentPhase = NarrativePhase.BuildUp,
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-a",
                    Desire = desire,
                    Restraint = restraint,
                    Tension = tension,
                    Connection = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50
                }
            ]
        };
    }

    public static IReadOnlyList<ScenarioDefinition> BuildCompetingScenarioSignals() =>
    [
        new ScenarioDefinition("scenario-alpha", "Alpha", 3),
        new ScenarioDefinition("scenario-beta", "Beta", 2),
        new ScenarioDefinition("scenario-gamma", "Gamma", 1)
    ];
}

public static class RolePlayV2AcceptanceFixtureData
{
    public static AdaptiveScenarioState BuildBoundaryState(int desire, int restraint, int tension)
    {
        return RolePlayAcceptanceFixtureData.BuildBoundaryState(desire, restraint, tension);
    }

    public static IReadOnlyList<ScenarioDefinition> BuildCompetingScenarioSignals()
    {
        return RolePlayAcceptanceFixtureData.BuildCompetingScenarioSignals();
    }
}
