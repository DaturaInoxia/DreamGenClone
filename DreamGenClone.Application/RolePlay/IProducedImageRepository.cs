using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IProducedImageRepository
{
    Task InsertAsync(ProducedImage image, CancellationToken cancellationToken = default);

    Task UpdateAsync(ProducedImage image, CancellationToken cancellationToken = default);

    Task<ProducedImage?> GetAsync(string imageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProducedImage>> ListBySessionAsync(
        string sessionId, string? interactionId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProducedImage>> ListByBatchAsync(
        string batchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProducedImage>> ListByParentAsync(
        string parentImageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProducedImage>> ListByStatusAsync(
        ProducedImageStatus status, CancellationToken cancellationToken = default);

    Task<ProducedImagePage> QueryAsync(
        ProducedImageQuery query, CancellationToken cancellationToken = default);
}
