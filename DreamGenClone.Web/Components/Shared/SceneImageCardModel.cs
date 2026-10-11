using DreamGenClone.Web.Application.RolePlay.Evaluation;

namespace DreamGenClone.Components.Shared;

/// <summary>
/// The display data every image list hands to the shared <see cref="ImageCard"/>. It is deliberately entity-agnostic:
/// an asset candidate (<c>SceneAssetImage</c>) and a role-play gallery image (<c>SceneImageRecord</c>) both project
/// into it, so the one card renders them instead of the two lists drifting apart. Anything the card draws is either in
/// this record or supplied by the host through the card's callbacks and content slots.
/// </summary>
public sealed record SceneImageCardModel
{
    public required string Id { get; init; }

    public string? FileRelativePath { get; init; }

    public required string StatusText { get; init; }

    public string StatusBadgeClass { get; init; } = "text-bg-secondary";

    public bool IsComplete { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The one-line caption under the id, e.g. "Kind · date" or "Style · size".</summary>
    public string? Subtitle { get; init; }

    public string? DecisionBadgeText { get; init; }

    public string? DecisionBadgeTitle { get; init; }

    public string? ApprovalBadgeText { get; init; }

    public IReadOnlyList<ImageGateResult> GateResults { get; init; } = [];

    public string? CandidateBatchId { get; init; }

    public string? DisplayName { get; init; }

    public bool IsProductionApproved { get; init; }

    /// <summary>
    /// True when this card is the one the host is showing in detail (the review deck's current candidate). Draws the
    /// selection highlight the host would otherwise re-implement around the card.
    /// </summary>
    public bool IsSelected { get; init; }

    /// <summary>
    /// This card's decision in a normalized vocabulary the shared status filter understands —
    /// <c>undecided</c>, <c>shortlisted</c>, <c>accepted</c>, <c>approved</c>, <c>rejected</c>, <c>revoked</c>,
    /// <c>archived</c>, <c>pending</c> or <c>failed</c>. Null means the host has no decision to filter by, and the
    /// card then only ever matches "All".
    /// </summary>
    public string? DecisionKey { get; init; }

    /// <summary>The chip label for <see cref="DecisionKey"/>, e.g. "Approved" rather than "accepted".</summary>
    public string? DecisionLabel { get; init; }
}
