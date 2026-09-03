namespace DreamGenClone.Application.RolePlay;

public sealed record ProductionStudioCapability(
    string ProfileId,
    string CellId,
    string ProviderKey,
    string ModelId,
    string ModelVersion,
    string CompilerId,
    string CompilerVersion,
    string ContentPolicyKey,
    string CellLabel);

public sealed record ProductionPrepareReference(
    string SemanticRole,
    string? ActorKey,
    string SceneAssetId,
    int SceneAssetVersion,
    string SceneAssetSha256,
    string? IdentityVersionId,
    string? BodyProfileVersionId,
    string? WardrobeLookVersionId,
    string BindingSnapshotJson);

public sealed record ProductionPrepareCommand(
    string SourceWorkloadItemId,
    string VisibleActorsJson,
    string CompositionIntentJson,
    string CameraIntentJson,
    string StyleIntentJson,
    string PreservationConstraintsJson,
    string ChangeIntentJson,
    string ContentPolicyJson,
    string CapabilityProfileId,
    string CapabilityCellId,
    string SettingsJson,
    IReadOnlyList<ProductionPrepareReference> References,
    int VariationCount,
    string Goal,
    string RetryPolicySnapshotJson,
    ProductionProviderEndpoint Endpoint,
    ProductionDispatchPolicy DispatchPolicy,
    ProductionCostBasis CostBasis,
    DateTime CreatedUtc);

public interface IProductionStudioService
{
    Task<IReadOnlyList<ProductionStudioCapability>> ListCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    Task<ProductionWorkloadReadiness> PrepareAsync(
        ProductionPrepareCommand command,
        CancellationToken cancellationToken = default);
}