using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>
/// The full-turn context for a scene image (CR-006 P2). A turn = one user submission cycle that
/// produces one or more interactions (e.g. Becky's action, Dean's reaction, Ken's observation,
/// Narrative's omniscient synthesis). The image prompt should be built from the whole turn, not a
/// single interaction, so the Narrative (omniscient) interaction contributes setting/environment
/// detail.
/// </summary>
public sealed record FullTurnContext
{
    /// <summary>The turn metadata (null when no turn row exists — legacy fallback).</summary>
    public RolePlayTurn? Turn { get; init; }

    /// <summary>All output interactions of the turn, ordered by creation.</summary>
    public IReadOnlyList<RolePlayInteraction> Interactions { get; init; } = [];

    /// <summary>The interaction the user selected to generate an image from.</summary>
    public RolePlayInteraction SelectedInteraction { get; init; } = null!;

    /// <summary>The Narrative (omniscient) interaction of the turn, if any — richest setting detail.</summary>
    public RolePlayInteraction? NarrativeInteraction { get; init; }
}