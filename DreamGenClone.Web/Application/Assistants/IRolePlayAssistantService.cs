namespace DreamGenClone.Web.Application.Assistants;

/// <summary>
/// Snapshot of the current role-play session state provided to the assistant for context-aware advice.
/// </summary>
public sealed class RolePlayAssistantContext
{
    public string SessionId { get; init; } = string.Empty;
    public string? ScenarioSummary { get; init; }
    public IReadOnlyList<string> RecentInteractions { get; init; } = [];
    public string? BehaviorMode { get; init; }
    public string? PersonaName { get; init; }
    public string? PersonaDescription { get; init; }
    public IReadOnlyList<string> CharacterSummaries { get; init; } = [];
    public int ContextWindowSize { get; init; }
    public int PinnedInteractionCount { get; init; }
    public string? SessionModelId { get; init; }
    public double SessionTemperature { get; init; }
    public double SessionTopP { get; init; }
    public int SessionMaxTokens { get; init; }

    // Rich scenario fields for field-specific advice
    public string? ScenarioTone { get; init; }
    public string? ScenarioWritingStyle { get; init; }
    public string? ScenarioPointOfView { get; init; }
    public IReadOnlyList<string> ScenarioStyleGuidelines { get; init; } = [];
    public IReadOnlyList<string> ScenarioConflicts { get; init; } = [];
    public IReadOnlyList<string> ScenarioGoals { get; init; } = [];
    public string? ScenarioWorldDescription { get; init; }
    public IReadOnlyList<string> ScenarioWorldRules { get; init; } = [];
    public IReadOnlyList<string> FullCharacterDetails { get; init; } = [];

    // Adaptive/profile steering visibility for assistant guidance
    public string? SelectedRankingProfileId { get; init; }
    public string? SelectedToneProfileId { get; init; }
    public string? StyleFloorOverride { get; init; }
    public string? StyleCeilingOverride { get; init; }
    public string? EffectiveStyleMode { get; init; }
    public string? StyleResolutionReason { get; init; }
    public IReadOnlyList<string> ProfileSteeringThemes { get; init; } = [];
}

public interface IRolePlayAssistantService
{
    /// <summary>
    /// Generates a role-play suggestion based on scenario, recent interactions, and user prompt.
    /// Conversation history is tracked per-session with automatic context truncation.
    /// </summary>
    Task<string> GenerateSuggestionAsync(
        string sessionId,
        string? scenarioSummary,
        IReadOnlyList<string> recentInteractions,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a context-aware role-play suggestion using rich session state and optional model override.
    /// </summary>
    Task<string> GenerateSuggestionAsync(
        RolePlayAssistantContext context,
        string userPrompt,
        string? assistantModelId = null,
        double? assistantTemperature = null,
        double? assistantTopP = null,
        int? assistantMaxTokens = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the assistant conversation history for a specific session.
    /// </summary>
    void ClearChat(string sessionId);
}
