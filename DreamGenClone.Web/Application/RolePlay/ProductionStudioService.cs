using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class ProductionStudioService : IProductionStudioService
{
    private readonly IProductionMediaRepository _repository;
    private readonly IProductionMediaCompilationService _compilation;
    private readonly IProductionWorkloadService _workloads;
    private readonly ISceneAssetRepository _assets;

    public ProductionStudioService(
        IProductionMediaRepository repository,
        IProductionMediaCompilationService compilation,
        IProductionWorkloadService workloads,
        ISceneAssetRepository assets)
    {
        _repository = repository;
        _compilation = compilation;
        _workloads = workloads;
        _assets = assets;
    }

    public async Task<IReadOnlyList<ProductionStudioCapability>> ListCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProductionStudioCapability>();
        foreach (var profile in (await _repository.ListCapabilityProfilesAsync(cancellationToken))
            .Where(profile => profile.Enabled && profile.Status == MediaCapabilityProfileStatus.Qualified))
        {
            foreach (var cell in (await _repository.ListCapabilityCellsAsync(profile.Id, cancellationToken))
                .Where(cell => cell.Status == MediaCapabilityCellStatus.Qualified))
            {
                results.Add(new ProductionStudioCapability(
                    profile.Id, cell.Id, profile.ProviderKey, profile.ModelId, profile.ModelVersion,
                    profile.CompilerId, profile.CompilerVersion, profile.ContentPolicyKey,
                    $"{cell.ActorCount} actor(s) · {cell.FaceAngleKey} · {cell.CropKey} · {cell.PoseClassKey} · {cell.CompositionClassKey}"));
            }
        }
        return results.OrderBy(result => result.ProviderKey, StringComparer.Ordinal)
            .ThenBy(result => result.ModelId, StringComparer.Ordinal)
            .ThenBy(result => result.CellLabel, StringComparer.Ordinal).ToList();
    }

    public async Task<ProductionWorkloadReadiness> PrepareAsync(
        ProductionPrepareCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CreatedUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Production preparation time must be UTC.");
        var sourceItem = await _repository.GetWorkloadItemAsync(command.SourceWorkloadItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Source workload item '{command.SourceWorkloadItemId}' was not found.");
        var source = await _repository.GetIntentAsync(sourceItem.IntentSnapshotId, cancellationToken)
            ?? throw new InvalidOperationException($"Source production intent '{sourceItem.IntentSnapshotId}' was not found.");
        if (source.ContextKind != ProductionContextKind.SceneMoment)
            throw new InvalidOperationException("Production Studio preparation requires a Scene Moment source intent.");
        RequireJson(command.VisibleActorsJson, "Visible actors");
        RequireJson(command.CompositionIntentJson, "Composition intent");
        RequireJson(command.CameraIntentJson, "Camera intent");
        RequireJson(command.StyleIntentJson, "Style intent");
        RequireJson(command.PreservationConstraintsJson, "Preservation constraints");
        RequireJson(command.ChangeIntentJson, "Change intent");
        RequireJson(command.ContentPolicyJson, "Content policy");

        var intent = new ProductionIntentSnapshot
        {
            Id = Guid.NewGuid().ToString("N"), ContextKind = source.ContextKind,
            ContextId = source.ContextId, ContextSnapshotJson = source.ContextSnapshotJson,
            ProductionGroupId = source.ProductionGroupId, SessionId = source.SessionId,
            CatalogueId = source.CatalogueId, BeatId = source.BeatId,
            BeatProductionPlanId = source.BeatProductionPlanId,
            BeatProductionPlanVersion = source.BeatProductionPlanVersion,
            MomentSetId = source.MomentSetId, MomentSetVersion = source.MomentSetVersion,
            MomentId = source.MomentId, MomentEnrichmentId = source.MomentEnrichmentId,
            MomentEnrichmentRevision = source.MomentEnrichmentRevision, Pov = source.Pov,
            Operation = source.Operation, SourceDerivativeId = source.SourceDerivativeId,
            VisibleActorsJson = command.VisibleActorsJson,
            CompositionIntentJson = command.CompositionIntentJson,
            CameraIntentJson = command.CameraIntentJson, StyleIntentJson = command.StyleIntentJson,
            PreservationConstraintsJson = command.PreservationConstraintsJson,
            ChangeIntentJson = command.ChangeIntentJson, ContentPolicyJson = command.ContentPolicyJson,
            CreatedUtc = command.CreatedUtc
        };
        intent.ContentHash = ProductionContentHash.ForIntent(intent);

        var requestId = Guid.NewGuid().ToString("N");
        var bindings = new List<OrderedMediaReferenceBinding>(command.References.Count);
        for (var ordinal = 0; ordinal < command.References.Count; ordinal++)
        {
            var reference = command.References[ordinal];
            var asset = await _assets.GetAsync(reference.SceneAssetId, cancellationToken)
                ?? throw new InvalidOperationException($"Reference Scene Asset '{reference.SceneAssetId}' was not found.");
            if (asset.ProductionApprovalStatus != SceneAssetProductionApprovalStatus.Approved
                || asset.ProductionVersion != reference.SceneAssetVersion
                || !string.Equals(asset.Sha256, reference.SceneAssetSha256, StringComparison.Ordinal))
                throw new InvalidOperationException($"Reference Scene Asset '{reference.SceneAssetId}' does not match the exact approved version and checksum.");
            bindings.Add(new OrderedMediaReferenceBinding
            {
                Id = Guid.NewGuid().ToString("N"), CompiledRequestId = requestId, Ordinal = ordinal,
                SemanticRole = reference.SemanticRole, ActorKey = reference.ActorKey,
                SceneAssetId = asset.Id, SceneAssetVersion = reference.SceneAssetVersion,
                SceneAssetSha256 = reference.SceneAssetSha256,
                IdentityVersionId = reference.IdentityVersionId,
                BodyProfileVersionId = reference.BodyProfileVersionId,
                WardrobeLookVersionId = reference.WardrobeLookVersionId,
                BindingSnapshotJson = reference.BindingSnapshotJson, CreatedUtc = command.CreatedUtc
            });
        }

        await _repository.CreateIntentAsync(intent, cancellationToken);
        var compilation = await _compilation.CompileAndPersistAsync(
            requestId, intent.Id, command.CapabilityProfileId, command.CapabilityCellId,
            command.SettingsJson, bindings, command.CreatedUtc, cancellationToken);
        var existing = await _repository.ListWorkloadsBySessionAsync(source.SessionId, cancellationToken);
        var workloadId = Guid.NewGuid().ToString("N");
        return await _workloads.CreateDraftAsync(new ProductionWorkloadDraft(
            workloadId, ProductionContextKind.SceneMoment, source.SessionId, source.ContextSnapshotJson,
            source.SessionId, existing.Count == 0 ? 1 : existing.Max(workload => workload.Revision) + 1,
            command.Goal, (await RequiredProfileAsync(command.CapabilityProfileId, cancellationToken)).ContentPolicyKey,
            JsonSerializer.Serialize(new
            {
                sourceIntentId = source.Id, sourceIntentHash = source.ContentHash,
                intentId = intent.Id, intentHash = intent.ContentHash,
                requestId = compilation.Request.Id, requestHash = compilation.Request.ContentHash
            }),
            [new ProductionWorkloadDraftItem(
                intent.Id, compilation.Request.Id, command.VariationCount,
                command.RetryPolicySnapshotJson, null, command.Endpoint,
                command.DispatchPolicy, command.CostBasis)], command.CreatedUtc), cancellationToken);
    }

    private async Task<MediaCapabilityProfile> RequiredProfileAsync(
        string id,
        CancellationToken cancellationToken) =>
        await _repository.GetCapabilityProfileAsync(id, cancellationToken)
        ?? throw new InvalidOperationException($"Media capability profile '{id}' was not found.");

    private static void RequireJson(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} JSON is required.");
        try { using var document = JsonDocument.Parse(value); }
        catch (JsonException exception) { throw new InvalidOperationException($"{label} must be valid JSON.", exception); }
    }
}