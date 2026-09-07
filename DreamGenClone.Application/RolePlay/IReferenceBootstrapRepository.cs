using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IReferenceBootstrapRepository
{
    Task<ReferenceBootstrapBatch?> GetBatchAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferenceBootstrapBatch>> ListBatchesAsync(CancellationToken cancellationToken = default);
    Task UpsertBatchAsync(ReferenceBootstrapBatch batch, CancellationToken cancellationToken = default);
    Task DeleteBatchAsync(string id, CancellationToken cancellationToken = default);

    Task<ReferenceBootstrapLocationProfile?> GetLocationProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferenceBootstrapLocationProfile>> ListLocationProfilesAsync(CancellationToken cancellationToken = default);
    Task UpsertLocationProfileAsync(ReferenceBootstrapLocationProfile profile, CancellationToken cancellationToken = default);
    Task DeleteLocationProfileAsync(string id, CancellationToken cancellationToken = default);

    Task<ReferenceBootstrapLocationReference?> GetLocationReferenceAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferenceBootstrapLocationReference>> ListLocationReferencesAsync(string profileId, CancellationToken cancellationToken = default);
    Task UpsertLocationReferenceAsync(ReferenceBootstrapLocationReference reference, CancellationToken cancellationToken = default);
    Task DeleteLocationReferenceAsync(string id, CancellationToken cancellationToken = default);
}