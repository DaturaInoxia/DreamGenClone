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

    /// <summary>
    /// True when this interaction is part of a staged scene-directions batch (B-076).
    /// Staged rows are injected via StagedDirectionsSlot on the next … continuation,
    /// then graduate (flag flips to false) so their content shows up in InteractionHistory
    /// as past context for subsequent turns. Pre-consumption staged rows are real
    /// RolePlayInteraction rows — all per-interaction commands (Retry, Expand, Pin, etc.)
    /// apply to them like any other row.
    /// </summary>
    public bool IsStagedDirection { get; set; }

    public string? ParentInteractionId { get; set; }

    public int AlternativeIndex { get; set; }

    public int ActiveAlternativeIndex { get; set; }

    /// <summary>Model identifier used to generate this interaction (null for user-authored).</summary>
    public string? GeneratedByModelId { get; set; }

    /// <summary>Display name of the model used for generation.</summary>
    public string? GeneratedByModelName { get; set; }

    /// <summary>The command that created this interaction (e.g. Retry, MakeLonger, AskToRewrite).</summary>
    public string? GeneratedByCommand { get; set; }

    /// <summary>
    /// The prompt variant (Character vs Narrative) used to generate this interaction.
    /// Null for interactions persisted before B-088; fall back to the legacy heuristic
    /// (GeneratedByCommand == "Narrative") when resolving the variant for retry.
    /// </summary>
    public DreamGenClone.Domain.RolePlay.PromptVariant? GeneratedVariant { get; set; }

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
    /// Set to true by the semantic encounter-start detection method when LLM inference
    /// confirms this interaction marks the beginning of a new sexual encounter.
    /// Distinct from WasInSexScene (which fires on any keyword match) — this fires only
    /// on the single interaction where the encounter transition was detected.
    /// Null for interactions that were not evaluated.
    /// </summary>
    public bool? WasEncounterStart { get; set; }

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

    /// <summary>
    /// This interaction's position in the session (0-based). null = legacy data.
    /// </summary>
    public int? SessionInteractionIndex { get; set; }

    /// <summary>
    /// Which global encounter # this interaction belongs to. null = no active encounter / legacy.
    /// Stamped from GlobalEncounterCount at creation time.
    /// </summary>
    public int? EncounterNumberAtCreation { get; set; }

    /// <summary>
    /// Position within the encounter (0-based). null = not in an encounter / legacy.
    /// </summary>
    public int? InteractionIndexInEncounter { get; set; }

    /// <summary>
    /// Session's resolved intensity label at creation time (e.g. "Explicit", "Hardcore"). null = legacy.
    /// Captured from session.LastResolvedIntensityLabel at creation time.
    /// </summary>
    public string? ExplicitnessLevelAtCreation { get; set; }

    /// <summary>
    /// The full LLM prompt text sent for this interaction, with the prior interactions
    /// block trimmed to first N + last N characters for storage efficiency.
    /// Null means "not captured" (pre-deployment interactions or best-effort capture failure).
    /// </summary>
    public string? PromptText { get; set; }

    /// <summary>
    /// B-075: Raw LLM response text (including reasoning) stored at generation time.
    /// Populated in ContinueAsync after the model response is received.
    /// Rendered in the Interaction Info modal Prompt/Response tab.
    /// </summary>
    public string? RawResponseText { get; set; }

    /// <summary>
    /// B-075: Serialized RolePlaySteeringDirective for staged steering instructions.
    /// Non-null when this instruction row is a per-character steering directive.
    /// Read by StagedDirectionsSlot.WriteAsync to emit role-aware steering blocks.
    /// </summary>
    public string? SteeringMetadataJson { get; set; }

    /// <summary>
    /// B-075: Links this staged steering instruction to the SteeringGenerationRecord
    /// that produced the all-character options. Null for non-steering interactions.
    /// </summary>
    public string? SteeringGenerationId { get; set; }
}
