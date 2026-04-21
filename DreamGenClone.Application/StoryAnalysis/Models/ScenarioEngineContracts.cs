namespace DreamGenClone.Application.StoryAnalysis.Models;

public sealed record ScenarioGuidanceInput(
    string SessionId,
    string CurrentPhase,
    string? ActiveScenarioId,
    string? VariantId,
    double AverageDesire,
    double AverageRestraint,
    double AverageTension,
    double AverageConnection,
    double AverageDominance,
    double AverageLoyalty,
    string? SelectedWillingnessProfileId,
    string? HusbandAwarenessProfileId,
    IReadOnlyList<string> SuppressedScenarioIds);

public sealed record ScenarioGuidanceContext(
    string Phase,
    string? ActiveScenarioId,
    string GuidanceText,
    IReadOnlyList<string> ExcludedScenarioIds);
