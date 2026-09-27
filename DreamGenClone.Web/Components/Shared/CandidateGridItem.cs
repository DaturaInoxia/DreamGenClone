namespace DreamGenClone.Components.Shared;

using DreamGenClone.Domain.RolePlay;

/// <summary>
/// One candidate as the shared grid presents it.
/// </summary>
/// <param name="Decision">
/// The review decision recorded against this candidate, or null when none was recorded. Carried so the grid can show
/// the SAME verdict the review deck shows, from the one <c>SceneAssetCandidateDecision</c> the render path writes.
/// </param>
public sealed record CandidateGridItem(
    string Id,
    string? ImagePath,
    string Status,
    string? Subtitle,
    string? Decision = null);

/// <summary>
/// One review decision the operator took on a candidate, as the grid reports it to its host.
/// </summary>
/// <remarks>
/// Declared here rather than in the component, because a type declared inside a <c>.razor</c> file belongs to that
/// component: a host could then only name it as <c>CandidateGrid.CandidateGridDecision</c>, which is not a contract
/// worth having.
/// </remarks>
public sealed record CandidateGridDecision(string CandidateId, SceneAssetCandidateDecision Decision);