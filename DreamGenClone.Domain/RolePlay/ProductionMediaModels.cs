using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

public enum MediaOperation
{
    Generate = 1,
    Edit = 2
}

public enum ProductionContextKind
{
    SceneMoment = 1,
    CharacterAsset = 2
}

public enum MediaCapabilityProfileStatus
{
    Candidate = 1,
    Qualified = 2,
    Rejected = 3,
    Suspended = 4,
    Superseded = 5
}

public enum MediaCapabilityCellStatus
{
    Draft = 1,
    Proving = 2,
    Qualified = 3,
    Rejected = 4,
    Retired = 5
}

public enum ProductionWorkloadStatus
{
    Draft = 1,
    Validating = 2,
    Ready = 3,
    Blocked = 4,
    Queued = 5,
    Running = 6,
    PartiallyComplete = 7,
    Complete = 8,
    Failed = 9,
    Cancelled = 10
}

public enum ProductionWorkloadItemStatus
{
    Draft = 1,
    Ready = 2,
    Queued = 3,
    Submitted = 4,
    Running = 5,
    Reviewable = 6,
    Approved = 7,
    Rejected = 8,
    Failed = 9,
    Cancelled = 10
}

public enum ProductionAttemptStatus
{
    Pending = 1,
    Submitted = 2,
    Running = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6,
    Indeterminate = 7
}

public enum ProductionAttemptKind
{
    Initial = 1,
    Variation = 2,
    Retry = 3,
    Repair = 4,
    Regeneration = 5
}

public enum ProductionReviewDecisionValue
{
    Shortlisted = 1,
    Rejected = 2,
    Approved = 3,
    Revoked = 4
}

public enum ProductionDerivativeStatus
{
    Approved = 1,
    Revoked = 2
}

public sealed class MediaCapabilityProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RegisteredModelId { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public MediaOperation Operation { get; set; }
    public string CompilerId { get; set; } = string.Empty;
    public string CompilerVersion { get; set; } = string.Empty;
    public string WorkflowRevision { get; set; } = string.Empty;
    public string NodeRevision { get; set; } = string.Empty;
    public string ArtifactManifestJson { get; set; } = string.Empty;
    public string SettingsSchemaJson { get; set; } = string.Empty;
    public string ReferenceLayoutJson { get; set; } = string.Empty;
    public string ControlLayoutJson { get; set; } = string.Empty;
    public string SupportedIdentityStrategiesJson { get; set; } = "[]";
    public string ContentPolicyKey { get; set; } = string.Empty;
    public MediaCapabilityProfileStatus Status { get; set; }
    public bool Enabled { get; set; }
    public string EvidenceRunId { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public sealed class MediaCapabilityCell
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CapabilityProfileId { get; set; } = string.Empty;
    public int ActorCount { get; set; }
    public string FaceAngleKey { get; set; } = string.Empty;
    public string CropKey { get; set; } = string.Empty;
    public string PoseClassKey { get; set; } = string.Empty;
    public string CompositionClassKey { get; set; } = string.Empty;
    public string ReferenceControlTupleJson { get; set; } = string.Empty;
    public CharacterIdentityStrategyKind? IdentityStrategyKind { get; set; }
    public MediaCapabilityCellStatus Status { get; set; }
    public string EvidenceRunId { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class ProductionIntentSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProductionContextKind ContextKind { get; set; } = ProductionContextKind.SceneMoment;
    public string ContextId { get; set; } = string.Empty;
    public string ContextSnapshotJson { get; set; } = "{}";
    public string ProductionGroupId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public string BeatProductionPlanId { get; set; } = string.Empty;
    public int BeatProductionPlanVersion { get; set; }
    public string MomentSetId { get; set; } = string.Empty;
    public int MomentSetVersion { get; set; }
    public string MomentId { get; set; } = string.Empty;
    public string MomentEnrichmentId { get; set; } = string.Empty;
    public int MomentEnrichmentRevision { get; set; }
    public string Pov { get; set; } = string.Empty;
    public MediaOperation Operation { get; set; }
    public string? SourceDerivativeId { get; set; }
    public string VisibleActorsJson { get; set; } = string.Empty;
    public string CompositionIntentJson { get; set; } = string.Empty;
    public string CameraIntentJson { get; set; } = string.Empty;
    public string StyleIntentJson { get; set; } = string.Empty;
    public string PreservationConstraintsJson { get; set; } = string.Empty;
    public string ChangeIntentJson { get; set; } = string.Empty;
    public string ContentPolicyJson { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public sealed class CompiledMediaRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string IntentSnapshotId { get; set; } = string.Empty;
    public string CapabilityProfileId { get; set; } = string.Empty;
    public string CapabilityCellId { get; set; } = string.Empty;
    public string CompilerId { get; set; } = string.Empty;
    public string CompilerVersion { get; set; } = string.Empty;
    public string RequestSchemaVersion { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public string WorkflowRevision { get; set; } = string.Empty;
    public string CanonicalProviderRequestJson { get; set; } = string.Empty;
    public string ValidationResultJson { get; set; } = string.Empty;
    public string IdentityStrategySnapshotJson { get; set; } = "[]";
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public sealed class OrderedMediaReferenceBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompiledRequestId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string SemanticRole { get; set; } = string.Empty;
    public string? ActorKey { get; set; }
    public string SceneAssetId { get; set; } = string.Empty;
    public int SceneAssetVersion { get; set; }
    public string SceneAssetSha256 { get; set; } = string.Empty;
    public string? IdentityVersionId { get; set; }
    public string? BodyProfileVersionId { get; set; }
    public string? WardrobeLookVersionId { get; set; }
    public string? RegionAssetId { get; set; }
    public string BindingSnapshotJson { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public sealed class ProductionWorkload
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProductionContextKind ContextKind { get; set; } = ProductionContextKind.SceneMoment;
    public string ContextId { get; set; } = string.Empty;
    public string ContextSnapshotJson { get; set; } = "{}";
    public string SessionId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public ProductionWorkloadStatus Status { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string Goal { get; set; } = string.Empty;
    public string ContentPolicyKey { get; set; } = string.Empty;
    public string SourceVersionSnapshotJson { get; set; } = string.Empty;
    public string ReadinessSnapshotJson { get; set; } = string.Empty;
    public string EndpointReadinessJson { get; set; } = string.Empty;
    public string CostEstimateJson { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public int OutputCount { get; set; }
    public int CompatibilityGroupCount { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public sealed class ProductionWorkloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkloadId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string IntentSnapshotId { get; set; } = string.Empty;
    public string CompiledRequestId { get; set; } = string.Empty;
    public string CompatibilityKey { get; set; } = string.Empty;
    public int VariationCount { get; set; }
    public ProductionWorkloadItemStatus Status { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string RetryPolicySnapshotJson { get; set; } = string.Empty;
    public string EndpointSnapshotJson { get; set; } = string.Empty;
    public string DispatchPolicySnapshotJson { get; set; } = string.Empty;
    public string CostBasisSnapshotJson { get; set; } = string.Empty;
    public string? DependsOnItemId { get; set; }
    public string? CurrentAttemptId { get; set; }
    public string? FailureCode { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class ProductionAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkloadItemId { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public ProductionAttemptKind Kind { get; set; }
    public ProductionAttemptStatus Status { get; set; }
    public long ConcurrencyVersion { get; set; }
    public string CompiledRequestId { get; set; } = string.Empty;
    public string CompiledRequestHash { get; set; } = string.Empty;
    public string RequestSnapshotJson { get; set; } = string.Empty;
    public string ReferenceSnapshotJson { get; set; } = string.Empty;
    public string ModelWorkflowSnapshotJson { get; set; } = string.Empty;
    public string SettingsSnapshotJson { get; set; } = string.Empty;
    public long Seed { get; set; }
    public string? ParentAttemptId { get; set; }
    public string? RepairSourceAttemptId { get; set; }
    public string? ProviderKey { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? ProviderStatusUrl { get; set; }
    public string? ProviderResponseSnapshotJson { get; set; }
    public string? OutputFileRelativePath { get; set; }
    public string? OutputSha256 { get; set; }
    public long? OutputByteLength { get; set; }
    public string? OutputMetadataJson { get; set; }
    public string? CostSnapshotJson { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureDiagnostic { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public sealed class ProductionReviewDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkloadItemId { get; set; } = string.Empty;
    public string AttemptId { get; set; } = string.Empty;
    public int Version { get; set; }
    public ProductionReviewDecisionValue Decision { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string DecidedBy { get; set; } = string.Empty;
    public DateTime DecidedUtc { get; set; }
}

public sealed class ProductionDerivative
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkloadItemId { get; set; } = string.Empty;
    public string AttemptId { get; set; } = string.Empty;
    public string ReviewDecisionId { get; set; } = string.Empty;
    public string SceneAssetId { get; set; } = string.Empty;
    public string CapabilityProfileId { get; set; } = string.Empty;
    public string SourceLineageJson { get; set; } = string.Empty;
    public string UseScopeKey { get; set; } = string.Empty;
    public ProductionDerivativeStatus Status { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime ApprovedUtc { get; set; }
}

public static class ProductionContentHash
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string ForIntent(ProductionIntentSnapshot intent)
    {
        var semanticValues = new[]
        {
            intent.ProductionGroupId, intent.SessionId, intent.CatalogueId, intent.BeatId,
            intent.BeatProductionPlanId, intent.BeatProductionPlanVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            intent.MomentSetId, intent.MomentSetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), intent.MomentId,
            intent.MomentEnrichmentId, intent.MomentEnrichmentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            intent.Pov, intent.Operation.ToString(), intent.SourceDerivativeId ?? string.Empty,
            intent.VisibleActorsJson, intent.CompositionIntentJson, intent.CameraIntentJson,
            intent.StyleIntentJson, intent.PreservationConstraintsJson, intent.ChangeIntentJson, intent.ContentPolicyJson
        };
        return intent.ContextKind == ProductionContextKind.SceneMoment
            ? Compute(semanticValues)
            : Compute(new[] { intent.ContextKind.ToString(), intent.ContextId, intent.ContextSnapshotJson }
                .Concat(semanticValues).ToArray());
    }

    public static string ForCompiledRequest(
        CompiledMediaRequest request,
        IEnumerable<OrderedMediaReferenceBinding> bindings) => Compute(
        new[]
        {
            request.IntentSnapshotId, request.CapabilityProfileId, request.CapabilityCellId,
            request.CompilerId, request.CompilerVersion, request.RequestSchemaVersion,
            request.ProviderKey, request.ModelId, request.ModelVersion, request.WorkflowRevision,
            request.CanonicalProviderRequestJson, request.ValidationResultJson,
            request.IdentityStrategySnapshotJson
        }.Concat(bindings.OrderBy(binding => binding.Ordinal)
            .Select(binding => JsonSerializer.Serialize(binding, SerializerOptions))).ToArray());

    public static string Compute(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}