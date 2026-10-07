using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Persistence for the moment→location container link. One CURRENT row per moment (keyed on
/// <see cref="SceneMomentLocationLink.MomentId"/>), written only by the operator's bind action.
/// </summary>
public interface ISceneMomentLocationLinkRepository
{
    Task<SceneMomentLocationLink?> GetAsync(string momentId, CancellationToken cancellationToken = default);

    /// <summary>Upserts the link keyed on <see cref="SceneMomentLocationLink.MomentId"/> — idempotent per moment.</summary>
    Task UpsertAsync(SceneMomentLocationLink link, CancellationToken cancellationToken = default);
}
