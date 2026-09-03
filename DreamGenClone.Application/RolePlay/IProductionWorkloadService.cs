using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public enum ProductionProviderJobState
{
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6
}

public sealed record ProductionProviderEndpoint(
    string ProviderKey,
    string EndpointId,
    string BaseUrl,
    string SubmitPath,
    string StatusPathTemplate,
    string CancelPathTemplate,
    int TimeoutSeconds,
    string ProtocolKey,
    string ReadinessSnapshotJson);

public sealed record ProductionDispatchPolicy(
    string AdapterKey,
    bool SupportsNativeVariations,
    int MaximumOutputsPerRequest,
    string WorkerImage,
    string ArtifactSet,
    string ReferenceAccessibility,
    int ResultRetentionSeconds);

public sealed record ProductionCostBasis(string Currency, decimal UnitCostPerOutput);

public sealed record ProductionWorkloadDraftItem(
    string IntentSnapshotId,
    string CompiledRequestId,
    int VariationCount,
    string RetryPolicySnapshotJson,
    string? DependsOnItemId,
    ProductionProviderEndpoint Endpoint,
    ProductionDispatchPolicy DispatchPolicy,
    ProductionCostBasis CostBasis);

public sealed record ProductionWorkloadDraft(
    string WorkloadId,
    ProductionContextKind ContextKind,
    string ContextId,
    string ContextSnapshotJson,
    string SessionId,
    int Revision,
    string Goal,
    string ContentPolicyKey,
    string SourceVersionSnapshotJson,
    IReadOnlyList<ProductionWorkloadDraftItem> Items,
    DateTime CreatedUtc);

public sealed record ProductionReadinessDiagnostic(
    string Code,
    string Message,
    bool Blocking,
    int? ItemOrdinal = null);

public sealed record ProductionWorkloadReadiness(
    ProductionWorkload Workload,
    IReadOnlyList<ProductionWorkloadItem> Items,
    IReadOnlyList<ProductionReadinessDiagnostic> Diagnostics);

public sealed record ProductionWorkloadItemSnapshot(
    ProductionWorkloadItem Item,
    ProductionIntentSnapshot Intent,
    CompiledMediaRequest Request,
    IReadOnlyList<OrderedMediaReferenceBinding> ReferenceBindings,
    IReadOnlyList<ProductionAttempt> Attempts,
    IReadOnlyList<ProductionReviewDecision> ReviewDecisions);

public sealed record ProductionWorkloadSnapshot(
    ProductionWorkload Workload,
    IReadOnlyList<ProductionWorkloadItemSnapshot> Items);

public sealed record ProductionReviewCommand(
    string WorkloadItemId,
    string AttemptId,
    ProductionReviewDecisionValue Decision,
    string ReasonCode,
    string? Notes,
    string DecidedBy,
    DateTime DecidedUtc);

public sealed record ProductionApprovalCommand(
    string WorkloadItemId,
    string AttemptId,
    string ReasonCode,
    string? Notes,
    string ApprovedBy,
    string SourceProvenanceJson,
    SceneAssetConsentState ConsentState,
    SceneAssetLicenseState LicenseState,
    string LicenseLabel,
    SceneAssetApprovedUseScope ApprovedUseScope,
    string ContentPolicyKey,
    string CompatibilityMetadataJson,
    string UseScopeKey,
    DateTime ApprovedUtc);

public sealed record ProductionDispatchAttempt(
    ProductionAttempt Attempt,
    CompiledMediaRequest Request);

public sealed record ProductionDispatchGroup(
    string CompatibilityKey,
    ProductionProviderEndpoint Endpoint,
    ProductionDispatchPolicy Policy,
    IReadOnlyList<ProductionDispatchAttempt> Attempts);

public sealed record ProductionProviderOutput(
    int Ordinal,
    string MediaType,
    string? Base64Data,
    string? TransientUrl,
    string MetadataJson);

public sealed record ProductionProviderSubmission(
    string AttemptId,
    string ProviderRequestId,
    string ProviderStatusUrl,
    ProductionProviderJobState State,
    string ProviderResponseSnapshotJson,
    string CostSnapshotJson,
    IReadOnlyList<ProductionProviderOutput> Outputs);

public sealed record ProductionProviderPollResult(
    ProductionProviderJobState State,
    string ProviderResponseSnapshotJson,
    string CostSnapshotJson,
    IReadOnlyList<ProductionProviderOutput> Outputs,
    string? FailureCode,
    string? FailureDiagnostic);

public interface IProductionDispatchAdapter
{
    string AdapterKey { get; }

    Task<IReadOnlyList<ProductionProviderSubmission>> SubmitAsync(
        ProductionDispatchGroup group,
        CancellationToken cancellationToken = default);

    Task<ProductionProviderPollResult> PollAsync(
        ProductionProviderEndpoint endpoint,
        string providerRequestId,
        CancellationToken cancellationToken = default);

    Task CancelAsync(
        ProductionProviderEndpoint endpoint,
        string providerRequestId,
        CancellationToken cancellationToken = default);
}

public interface IProductionDispatchAdapterRegistry
{
    IProductionDispatchAdapter Resolve(string adapterKey);
}

public interface IProductionWorkloadService
{
    Task<IReadOnlyList<ProductionWorkloadSnapshot>> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<ProductionWorkloadReadiness> CreateDraftAsync(
        ProductionWorkloadDraft draft,
        CancellationToken cancellationToken = default);

    Task SubmitAsync(string workloadId, CancellationToken cancellationToken = default);

    Task CancelAsync(string workloadId, CancellationToken cancellationToken = default);

    Task ReconcileAsync(string workloadId, CancellationToken cancellationToken = default);

    Task<ProductionReviewDecision> ReviewAsync(
        ProductionReviewCommand command,
        CancellationToken cancellationToken = default);

    Task<ProductionDerivative> ApproveAsync(
        ProductionApprovalCommand command,
        CancellationToken cancellationToken = default);

    Task<ProductionAttempt> RetryAsync(
        string workloadItemId,
        string failedAttemptId,
        DateTime createdUtc,
        CancellationToken cancellationToken = default);
}

public interface IProductionReconciliationService
{
    Task CaptureSubmissionAsync(
        ProductionProviderSubmission submission,
        CancellationToken cancellationToken = default);

    Task ReconcileWorkloadAsync(string workloadId, CancellationToken cancellationToken = default);

    Task ReconcileAttemptAsync(string attemptId, CancellationToken cancellationToken = default);
}