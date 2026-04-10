namespace DreamGenClone.Web.Application.Assistants;

public enum ScenarioAssistantTarget
{
    General,
    PlotDescription,
    WorldDescription,
    StyleGuidelines,
    Locations,
    Objects
}

public sealed class ScenarioAssistantContext
{
    public string SessionId { get; init; } = string.Empty;
    public string ScenarioName { get; init; } = "Unnamed Scenario";
    public string ScenarioDescription { get; init; } = string.Empty;
    public string PlotDescription { get; init; } = string.Empty;
    public IReadOnlyList<string> PlotConflicts { get; init; } = [];
    public IReadOnlyList<string> PlotGoals { get; init; } = [];
    public string WorldDescription { get; init; } = string.Empty;
    public string TimeFrame { get; init; } = string.Empty;
    public IReadOnlyList<string> WorldRules { get; init; } = [];
    public IReadOnlyList<string> EnvironmentalDetails { get; init; } = [];
    public string Tone { get; init; } = string.Empty;
    public string WritingStyle { get; init; } = string.Empty;
    public string PointOfView { get; init; } = string.Empty;
    public IReadOnlyList<string> StyleGuidelines { get; init; } = [];
    public IReadOnlyList<string> CharacterSummaries { get; init; } = [];
    public IReadOnlyList<string> LocationSummaries { get; init; } = [];
    public IReadOnlyList<string> ObjectSummaries { get; init; } = [];
    public IReadOnlyList<string> OpeningSummaries { get; init; } = [];
    public IReadOnlyList<string> ExampleSummaries { get; init; } = [];
    public string SavedScenarioSnapshot { get; init; } = string.Empty;
    public ScenarioAssistantTarget Target { get; init; } = ScenarioAssistantTarget.General;
}

public interface IScenarioAssistantService
{
    Task<string> GenerateSuggestionAsync(
        ScenarioAssistantContext context,
        string userPrompt,
        CancellationToken cancellationToken = default);

    void ClearChat(string sessionId);
}
