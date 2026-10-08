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
}
