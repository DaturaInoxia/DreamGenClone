using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Persistence for the moment→location container link. One CURRENT row per (moment, POV) (keyed on
/// <see cref="SceneMomentLocationLink.MomentId"/> and <see cref="SceneMomentLocationLink.Pov"/>), written only by the
/// operator's bind action — Omniscient and each character POV keep their own location binding.
/// </summary>
public interface ISceneMomentLocationLinkRepository
{
    /// <summary>The link for one POV of a moment, or null when that POV has bound nothing.</summary>
    Task<SceneMomentLocationLink?> GetAsync(
        string momentId,
        string pov,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the link keyed on (<see cref="SceneMomentLocationLink.MomentId"/>,
    /// <see cref="SceneMomentLocationLink.Pov"/>) — idempotent per POV of a moment.
    /// </summary>
    Task UpsertAsync(SceneMomentLocationLink link, CancellationToken cancellationToken = default);
}
