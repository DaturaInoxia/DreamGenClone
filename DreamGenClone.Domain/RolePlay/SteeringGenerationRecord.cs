namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// B-075: Persisted record of one steering-options generation request and its
/// all-character response. Stored once per Direction-flow Generate/Regenerate.
/// Linked from the staged steering interaction and final continuation.
/// </summary>
public sealed class SteeringGenerationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Full steering-options generation prompt sent to the model.</summary>
    public string GenerationPrompt { get; set; } = string.Empty;

    /// <summary>Raw LLM response text (before parsing).</summary>
    public string GenerationResponse { get; set; } = string.Empty;

    /// <summary>
    /// Parsed all-character options as structured JSON.
    /// Shape: {"characters":[{"characterId":"..","characterName":"..","role":"..","options":{"away":"..","neutral":"..","towards":"..","hard":".."}}]}
    /// </summary>
    public string? ParsedOptionsJson { get; set; }

    /// <summary>JSON snapshot of all active character stats/roles at generation time.</summary>
    public string? CharacterSnapshotJson { get; set; }

    /// <summary>Active theme ID at generation time.</summary>
    public string? ActiveThemeId { get; set; }

    /// <summary>Active theme label.</summary>
    public string? ActiveThemeLabel { get; set; }

    /// <summary>Narrative phase at generation time.</summary>
    public string? Phase { get; set; }

    /// <summary>Model identifier used for generation.</summary>
    public string? ModelIdentifier { get; set; }

    /// <summary>Provider name.</summary>
    public string? ProviderName { get; set; }

    /// <summary>Temperature used.</summary>
    public double? Temperature { get; set; }

    /// <summary>Top-P used.</summary>
    public double? TopP { get; set; }

    /// <summary>Max tokens used.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Whether generation succeeded (false when the model returned unparseable output).</summary>
    public bool Succeeded { get; set; } = true;

    /// <summary>Error message when generation/parsing failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Summary of the user's eventual selection after generation:
    /// "{TargetCharacterLabel} | {Direction} | {FreeTextDirective}"
    /// </summary>
    public string? SelectedDirectiveSummary { get; set; }

    /// <summary>ID of the staged steering instruction interaction.</summary>
    public string? StagedInteractionId { get; set; }

    /// <summary>ID of the final continuation interaction (linked after generation).</summary>
    public string? ContinuationInteractionId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
