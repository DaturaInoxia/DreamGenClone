using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public enum CharacterAssetCandidateKind
{
    IdentitySeed = 1,
    Coverage = 2
}

public sealed record CharacterAssetCandidateDraft(
    string WorkloadId,
    string IntentId,
    string CompiledRequestId,
    string AssetName,
    SceneAssetType AssetType,
    CharacterAssetCandidateKind Kind,
    string CoverageKey,
    string Pov,
    string VisibleActorsJson,
    string CompositionIntentJson,
    string CameraIntentJson,
    string StyleIntentJson,
    string PreservationConstraintsJson,
    string ChangeIntentJson,
    string SettingsJson,
    IReadOnlyList<OrderedMediaReferenceBinding> ReferenceBindings,
    IReadOnlyList<IdentityStrategyBinding> IdentityBindings);

public sealed record CharacterAssetGenerationBatch(
    string DatasetId,
    string CharacterProfileId,
    string IdentityPackId,
    string CapabilityProfileId,
    string CapabilityCellId,
    string ContentPolicyKey,
    string SourceVersionSnapshotJson,
    string RetryPolicySnapshotJson,
    ProductionProviderEndpoint Endpoint,
    ProductionDispatchPolicy DispatchPolicy,
    ProductionCostBasis CostBasis,
    IReadOnlyList<CharacterAssetCandidateDraft> Candidates,
    DateTime CreatedUtc);

public interface ICharacterAssetGenerationService
{
    Task<IReadOnlyList<ProductionWorkloadReadiness>> CreateBatchAsync(
        CharacterAssetGenerationBatch batch,
        CancellationToken cancellationToken = default);
}