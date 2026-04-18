using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface INarrativeGateProfileService
{
    Task<NarrativeGateProfile> SaveAsync(NarrativeGateProfile profile, CancellationToken cancellationToken = default);

    Task<List<NarrativeGateProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<NarrativeGateProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<NarrativeGateProfile?> GetDefaultAsync(CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
