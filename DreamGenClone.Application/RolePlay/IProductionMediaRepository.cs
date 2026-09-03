using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IProductionMediaRepository
{
    Task CreateCapabilityProfileAsync(MediaCapabilityProfile profile, CancellationToken cancellationToken = default);
    Task<MediaCapabilityProfile?> GetCapabilityProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaCapabilityProfile>> ListCapabilityProfilesAsync(CancellationToken cancellationToken = default);
    Task AddCapabilityCellAsync(MediaCapabilityCell cell, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaCapabilityCell>> ListCapabilityCellsAsync(string profileId, CancellationToken cancellationToken = default);

    Task CreateIntentAsync(ProductionIntentSnapshot intent, CancellationToken cancellationToken = default);
    Task<ProductionIntentSnapshot?> GetIntentAsync(string id, CancellationToken cancellationToken = default);

    Task CreateCompiledRequestAsync(
        CompiledMediaRequest request,
        IReadOnlyList<OrderedMediaReferenceBinding> bindings,
        CancellationToken cancellationToken = default);
    Task CreateIdentityCompiledRequestAsync(
        CompiledMediaRequest request,
        IReadOnlyList<OrderedMediaReferenceBinding> referenceBindings,
        IReadOnlyList<IdentityStrategyBinding> identityBindings,
        CancellationToken cancellationToken = default);
    Task<CompiledMediaRequest?> GetCompiledRequestAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderedMediaReferenceBinding>> ListReferenceBindingsAsync(string compiledRequestId, CancellationToken cancellationToken = default);

    Task CreateWorkloadAsync(
        ProductionWorkload workload,
        IReadOnlyList<ProductionWorkloadItem> items,
        CancellationToken cancellationToken = default);
    Task<ProductionWorkload?> GetWorkloadAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductionWorkload>> ListWorkloadsBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
    Task<ProductionWorkloadItem?> GetWorkloadItemAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductionWorkloadItem>> ListWorkloadItemsAsync(string workloadId, CancellationToken cancellationToken = default);
    Task<ProductionWorkload> TransitionWorkloadAsync(
        string id,
        ProductionWorkloadStatus expectedStatus,
        ProductionWorkloadStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);
    Task<ProductionWorkloadItem> TransitionWorkloadItemAsync(
        string id,
        ProductionWorkloadItemStatus expectedStatus,
        ProductionWorkloadItemStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    Task CreateAttemptAsync(ProductionAttempt attempt, CancellationToken cancellationToken = default);
    Task<ProductionAttempt?> GetAttemptAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductionAttempt>> ListAttemptsAsync(string workloadItemId, CancellationToken cancellationToken = default);
    Task<ProductionAttempt> RecordProviderSubmissionAsync(
        string id,
        string providerKey,
        string providerRequestId,
        string providerStatusUrl,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);
    Task<ProductionAttempt> TransitionAttemptAsync(
        string id,
        ProductionAttemptStatus expectedStatus,
        ProductionAttemptStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);
    Task<ProductionAttempt> RecordAttemptResultAsync(
        string id,
        string outputFileRelativePath,
        string outputSha256,
        long outputByteLength,
        string outputMetadataJson,
        string providerResponseSnapshotJson,
        string costSnapshotJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);
    Task<ProductionAttempt> RecordLateAttemptResultAsync(
        string id,
        string outputFileRelativePath,
        string outputSha256,
        long outputByteLength,
        string outputMetadataJson,
        string providerResponseSnapshotJson,
        string costSnapshotJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);
    Task<ProductionAttempt> RecordAttemptFailureAsync(
        string id,
        ProductionAttemptStatus terminalStatus,
        string failureCode,
        string failureDiagnostic,
        string providerResponseSnapshotJson,
        string costSnapshotJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    Task<ProductionReviewDecision> AddReviewDecisionAsync(
        ProductionReviewDecision decision,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductionReviewDecision>> ListReviewDecisionsAsync(
        string workloadItemId,
        CancellationToken cancellationToken = default);
    Task CreateDerivativeAsync(ProductionDerivative derivative, CancellationToken cancellationToken = default);
    Task<ProductionDerivative?> GetDerivativeAsync(string id, CancellationToken cancellationToken = default);
}