using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IReferenceBootstrapRepository
{
    Task<ReferenceBootstrapBatch?> GetBatchAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferenceBootstrapBatch>> ListBatchesAsync(CancellationToken cancellationToken = default);
    Task UpsertBatchAsync(ReferenceBootstrapBatch batch, CancellationToken cancellationToken = default);
    Task DeleteBatchAsync(string id, CancellationToken cancellationToken = default);
}