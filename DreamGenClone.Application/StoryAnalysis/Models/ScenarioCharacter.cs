namespace DreamGenClone.Application.StoryAnalysis.Models;

/// <summary>
/// Minimal character data needed for behavioral frame generation.
/// </summary>
public sealed record ScenarioCharacter(
    string Id,
    string Name,
    string Role,
    IReadOnlyList<string>? SeductionArchetypes = null);
