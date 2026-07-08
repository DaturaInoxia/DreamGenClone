using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Immutable context built once per prompt by the coordinator. Contains all resolved values
/// that injectors need — injectors MUST read from context rather than detecting phase or
/// resolving markers themselves.
/// </summary>
public sealed record PromptInjectionContext
{
    // ── Session state ──────────────────────────────────────────
    public required RolePlaySession Session { get; init; }

    // ── Resolved scene direction (single source of truth from SceneDirectionResolver) ──
    public required SceneDirection SceneDirection { get; init; }

    // ── Phase identifier — available for data-selection ONLY, NOT for behavioral branching ──
    /// <summary>
    /// Current narrative phase. Injectors MAY read this for data selection (e.g., selecting
    /// which phase's guidance prose to inject). They MUST NOT branch on phase to emit
    /// hardcoded text. Any phase-aware data selection MUST be documented and justified.
    /// </summary>
    public required string Phase { get; init; }

    // ── Prompt metadata ────────────────────────────────────────
    public PromptIntent Intent { get; init; }
    public int? PositionInTurn { get; init; }
    public int? TurnActorCount { get; init; }
    public required string ActorName { get; init; }

    // ── Theme data ─────────────────────────────────────────────
    public RPTheme? ActiveTheme { get; init; }
    public IReadOnlyDictionary<string, int>? ActorStats { get; init; }

    // ── Theme guidance text (pre-filtered for current phase) ───
    public IReadOnlyList<string> PhaseGuidanceLines { get; init; } = [];
    public IReadOnlyList<string> PhaseDirectiveLines { get; init; } = [];

    // ── Theme constraints ──────────────────────────────────────
    public IReadOnlyList<RPThemeAIGuidanceNote> AiGuidanceNotes { get; init; } = [];
    public IReadOnlyList<string> ThemeHardConstraintLines { get; init; } = [];

    // ── Location awareness ─────────────────────────────────────
    /// <summary>
    /// Whether this actor is confirmed in the current scene location.
    ///   true  — actor is confirmed in the current scene location
    ///   false — actor is confirmed NOT in the current scene location
    ///   null  — unknown (location services off, scene location absent, or actor's truth state not tracked)
    /// </summary>
    public bool? IsActorInScene { get; init; }

    // ── Helpers ────────────────────────────────────────────────
    /// <summary>
    /// Checks if the specified marker string exists in the current phase's guidance lines.
    /// Marker format: [MarkerName] or [MarkerName:value].
    /// </summary>
    public bool HasMarker(string marker)
        => PhaseGuidanceLines.Any(l => l.Contains($"[{marker}]"));
}
