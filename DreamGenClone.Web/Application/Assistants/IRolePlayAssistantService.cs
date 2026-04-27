namespace DreamGenClone.Web.Application.Assistants;

/// <summary>
/// Snapshot of the current role-play session state provided to the assistant for context-aware advice.
/// </summary>
public sealed class RolePlayAssistantContext
{
    public string SessionId { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public string? ScenarioSummary { get; init; }
    public IReadOnlyList<string> RecentInteractions { get; init; } = [];
    public string? BehaviorMode { get; init; }
    public string? CurrentNarrativePhase { get; init; }
    public string? ActiveScenarioId { get; init; }
    public string? PersonaName { get; init; }
    public string? PersonaDescription { get; init; }
    public string? PersonaRole { get; init; }
    public string? PersonaRelationSummary { get; init; }
    public IReadOnlyList<string> CharacterSummaries { get; init; } = [];
    public int ContextWindowSize { get; init; }
    public int PinnedInteractionCount { get; init; }
    public string? SessionModelId { get; init; }
    public double SessionTemperature { get; init; }
    public double SessionTopP { get; init; }
    public int SessionMaxTokens { get; init; }

    // Rich scenario fields for field-specific advice
    public string? ScenarioNarrativeTone { get; init; }
    public string? ScenarioProseStyle { get; init; }
    public string? ScenarioPointOfView { get; init; }
    public IReadOnlyList<string> ScenarioNarrativeGuidelines { get; init; } = [];
    public IReadOnlyList<string> ScenarioConflicts { get; init; } = [];
    public IReadOnlyList<string> ScenarioGoals { get; init; } = [];
    public string? ScenarioWorldDescription { get; init; }
    public IReadOnlyList<string> ScenarioWorldRules { get; init; } = [];
    public IReadOnlyList<string> FullCharacterDetails { get; init; } = [];

    // Adaptive/profile steering visibility for assistant guidance
    public string? SelectedThemeProfileId { get; init; }
    public string? SelectedNarrativeGateProfileId { get; init; }
    public string? SelectedIntensityProfileId { get; init; }
    public string? AdaptiveIntensityProfileId { get; init; }
    public string? ActiveIntensityProfile { get; init; }
    public string? ResolvedIntensityProfile { get; init; }
    public string? SelectedSteeringProfileId { get; init; }
    public string? IntensityFloorOverride { get; init; }
    public string? IntensityCeilingOverride { get; init; }
    public bool IsIntensityManuallyPinned { get; init; }
    public string? EffectiveStyleMode { get; init; }
    public string? StyleResolutionReason { get; init; }
    public string? AdaptiveTransitionReason { get; init; }
    public string? AdaptiveTransitionFromProfileId { get; init; }
    public string? AdaptiveTransitionToProfileId { get; init; }
    public DateTime? AdaptiveTransitionUtc { get; init; }
    public bool AdaptiveTransitionBlockedByManualPin { get; init; }
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

    Task<string> GenerateSuggestionStreamingAsync(
        RolePlayAssistantContext context,
        string userPrompt,
        Func<string, Task> onChunk,
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
