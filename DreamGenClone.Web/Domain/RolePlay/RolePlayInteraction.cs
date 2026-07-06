namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class RolePlayInteraction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public InteractionType InteractionType { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Snapshot of the session's <see cref="DreamGenClone.Domain.RolePlay.NarrativePhase"/>
    /// at the moment this interaction was created. Null for interactions persisted before
    /// this field was introduced. Used by the Interaction Info UI to show which phase the
    /// interaction belonged to when it was generated.
    /// </summary>
    public DreamGenClone.Domain.RolePlay.NarrativePhase? NarrativePhaseAtCreation { get; set; }

    public bool IsExcluded { get; set; }

    public bool IsHidden { get; set; }

    public bool IsPinned { get; set; }

    public string? ParentInteractionId { get; set; }

    public int AlternativeIndex { get; set; }

    public int ActiveAlternativeIndex { get; set; }

    /// <summary>Model identifier used to generate this interaction (null for user-authored).</summary>
    public string? GeneratedByModelId { get; set; }

    /// <summary>Display name of the model used for generation.</summary>
    public string? GeneratedByModelName { get; set; }

    /// <summary>The command that created this interaction (e.g. Retry, MakeLonger, AskToRewrite).</summary>
    public string? GeneratedByCommand { get; set; }

    /// <summary>Provider name of the model used for generation.</summary>
    public string? GeneratedByProvider { get; set; }

    /// <summary>Temperature used during generation.</summary>
    public double? GeneratedTemperature { get; set; }

    /// <summary>Top-P used during generation.</summary>
    public double? GeneratedTopP { get; set; }

    /// <summary>Max tokens setting used during generation.</summary>
    public int? GeneratedMaxTokens { get; set; }

    /// <summary>
    /// Set to true when this interaction's content triggered the sync heuristic
    /// (HasSexualActivityContent) — meaning the character was flagged as being in
    /// a sexual/erotic scene at the time this interaction was generated.
    /// Null for interactions that were not evaluated (legacy data).
    /// </summary>
    public bool? WasInSexScene { get; set; }

    /// <summary>
    /// Set to true when this interaction triggered an encounter-completed boundary
    /// detection that advanced the encounter counter and set a time-skip phase.
    /// Null for interactions that were not evaluated or did not trigger detection.
    /// </summary>
    public bool? WasEncounterBoundaryDetected { get; set; }

    /// <summary>
    /// Model's reasoning/chain-of-thought output (e.g. DeepSeek reasoning_content,
    /// OpenAI o-series, Anthropic thinking). Null when the model does not provide reasoning
    /// or when the interaction was user-authored.
    /// </summary>
    public string? ReasoningContent { get; set; }
}
