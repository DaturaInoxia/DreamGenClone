using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class CharacterAssetGenerationService : ICharacterAssetGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IProductionMediaRepository _repository;
    private readonly IProductionMediaCompilationService _compilation;
    private readonly IProductionWorkloadService _workloads;
    private readonly ICharacterImageIdentityRepository _identityRepository;

    public CharacterAssetGenerationService(
        IProductionMediaRepository repository,
        IProductionMediaCompilationService compilation,
        IProductionWorkloadService workloads,
        ICharacterImageIdentityRepository identityRepository)
    {
        _repository = repository;
        _compilation = compilation;
        _workloads = workloads;
        _identityRepository = identityRepository;
    }

    public async Task<IReadOnlyList<ProductionWorkloadReadiness>> CreateBatchAsync(
        CharacterAssetGenerationBatch batch,
        CancellationToken cancellationToken = default)
    {
        Validate(batch);
        var identityPack = await _identityRepository.GetPackAsync(batch.IdentityPackId, cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{batch.IdentityPackId}' was not found.");
        if (!string.Equals(identityPack.CharacterProfileId, batch.CharacterProfileId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Identity pack '{identityPack.Id}' belongs to character '{identityPack.CharacterProfileId}', not '{batch.CharacterProfileId}'.");
        if (identityPack.Status != CharacterImageIdentityPackStatus.Approved)
            throw new InvalidOperationException(
                $"Identity pack '{identityPack.Id}' is {identityPack.Status}; character asset generation requires an approved pack.");

        var results = new List<ProductionWorkloadReadiness>(batch.Candidates.Count);
        foreach (var candidate in batch.Candidates)
        {
            var contextJson = JsonSerializer.Serialize(new
            {
                batch.DatasetId, batch.CharacterProfileId, batch.IdentityPackId,
                candidateKind = candidate.Kind.ToString(), candidate.CoverageKey,
                candidate.AssetName, assetType = candidate.AssetType.ToString()
            }, JsonOptions);
            var intent = new ProductionIntentSnapshot
            {
                Id = candidate.IntentId,
                ContextKind = ProductionContextKind.CharacterAsset,
                ContextId = candidate.IntentId,
                ContextSnapshotJson = contextJson,
                Pov = candidate.Pov,
                Operation = MediaOperation.Generate,
                VisibleActorsJson = candidate.VisibleActorsJson,
                CompositionIntentJson = candidate.CompositionIntentJson,
                CameraIntentJson = candidate.CameraIntentJson,
                StyleIntentJson = candidate.StyleIntentJson,
                PreservationConstraintsJson = candidate.PreservationConstraintsJson,
                ChangeIntentJson = candidate.ChangeIntentJson,
                ContentPolicyJson = JsonSerializer.Serialize(new { key = batch.ContentPolicyKey }, JsonOptions),
                CreatedUtc = batch.CreatedUtc
            };
            intent.ContentHash = ProductionContentHash.ForIntent(intent);
            await _repository.CreateIntentAsync(intent, cancellationToken);
            if (candidate.IdentityBindings.Count == 0)
            {
                await _compilation.CompileAndPersistAsync(
                    candidate.CompiledRequestId, intent.Id, batch.CapabilityProfileId,
                    batch.CapabilityCellId, candidate.SettingsJson, candidate.ReferenceBindings,
                    batch.CreatedUtc, cancellationToken);
            }
            else
            {
                await _compilation.CompileIdentityAndPersistAsync(
                    candidate.CompiledRequestId, intent.Id, batch.CapabilityProfileId,
                    batch.CapabilityCellId, candidate.SettingsJson, candidate.ReferenceBindings,
                    candidate.IdentityBindings, batch.CreatedUtc, cancellationToken);
            }
            results.Add(await _workloads.CreateDraftAsync(new ProductionWorkloadDraft(
                candidate.WorkloadId, ProductionContextKind.CharacterAsset, candidate.IntentId,
                contextJson, string.Empty, 1, $"Generate {candidate.Kind} candidate {candidate.CoverageKey}",
                batch.ContentPolicyKey, batch.SourceVersionSnapshotJson,
                [new ProductionWorkloadDraftItem(
                    candidate.IntentId, candidate.CompiledRequestId, 1,
                    batch.RetryPolicySnapshotJson, null, batch.Endpoint,
                    batch.DispatchPolicy, batch.CostBasis)], batch.CreatedUtc), cancellationToken));
        }
        return results;
    }

    private static void Validate(CharacterAssetGenerationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Require(batch.DatasetId, "Dataset id");
        Require(batch.CharacterProfileId, "Character profile id");
        Require(batch.IdentityPackId, "Identity pack id");
        Require(batch.CapabilityProfileId, "Capability profile id");
        Require(batch.CapabilityCellId, "Capability cell id");
        if (batch.Candidates.Count == 0) throw new InvalidOperationException("At least one character asset candidate is required.");
        if (batch.Candidates.Select(candidate => candidate.IntentId).Distinct(StringComparer.Ordinal).Count() != batch.Candidates.Count
            || batch.Candidates.Select(candidate => candidate.CompiledRequestId).Distinct(StringComparer.Ordinal).Count() != batch.Candidates.Count
            || batch.Candidates.Select(candidate => candidate.WorkloadId).Distinct(StringComparer.Ordinal).Count() != batch.Candidates.Count)
            throw new InvalidOperationException("Candidate intent, compiled request, and workload ids must be unique within the batch.");
        foreach (var candidate in batch.Candidates)
        {
            Require(candidate.WorkloadId, "Candidate workload id");
            Require(candidate.IntentId, "Candidate intent id");
            Require(candidate.CompiledRequestId, "Candidate compiled request id");
            Require(candidate.AssetName, "Candidate asset name");
            Require(candidate.CoverageKey, "Candidate coverage key");
            Require(candidate.SettingsJson, "Candidate settings JSON");
            ArgumentNullException.ThrowIfNull(candidate.IdentityBindings);
            if (!Enum.IsDefined(candidate.Kind) || !Enum.IsDefined(candidate.AssetType))
                throw new InvalidOperationException("Candidate kind and asset type must be explicit.");
        }
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }
}