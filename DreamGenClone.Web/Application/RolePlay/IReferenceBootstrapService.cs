using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IReferenceBootstrapService
{
    Task<ReferenceBootstrapBatch> CreateBatchAsync(
        ReferenceBootstrapBatch batch,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> GenerateCandidatesAsync(
        string batchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProducedImage>> ListCandidatesAsync(
        string batchId,
        CancellationToken cancellationToken = default);

    Task SetCandidateDecisionAsync(
        string producedImageId,
        ProducedImageStatus status,
        string? notes,
        CancellationToken cancellationToken = default);

    Task PromoteAcceptedCharacterFaceAsync(
        string batchId,
        string producedImageId,
        CancellationToken cancellationToken = default);

    Task PromoteAcceptedCharacterBodyAsync(
        string batchId,
        string producedImageId,
        CancellationToken cancellationToken = default);

    Task PromoteAcceptedWardrobeAsync(
        string batchId,
        string producedImageId,
        CancellationToken cancellationToken = default);

    Task PromoteAcceptedLocationAsync(
        string batchId,
        string producedImageId,
        CancellationToken cancellationToken = default);
}