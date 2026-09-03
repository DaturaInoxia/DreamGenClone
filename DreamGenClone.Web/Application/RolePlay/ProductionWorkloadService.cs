using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class ProductionDispatchAdapterRegistry : IProductionDispatchAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, IProductionDispatchAdapter> _adapters;

    public ProductionDispatchAdapterRegistry(IEnumerable<IProductionDispatchAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(adapter => adapter.AdapterKey, StringComparer.Ordinal);
    }

    public IProductionDispatchAdapter Resolve(string adapterKey)
    {
        if (string.IsNullOrWhiteSpace(adapterKey))
            throw new InvalidOperationException("Production dispatch adapter key is required.");
        if (!_adapters.TryGetValue(adapterKey.Trim(), out var adapter))
            throw new InvalidOperationException($"No production dispatch adapter is registered for '{adapterKey}'.");
        return adapter;
    }
}

public sealed class ProductionWorkloadService : IProductionWorkloadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IProductionMediaRepository _repository;
    private readonly IProductionDispatchAdapterRegistry _adapters;
    private readonly IProductionReconciliationService _reconciliation;
    private readonly ISceneAssetRepository _assets;

    public ProductionWorkloadService(
        IProductionMediaRepository repository,
        IProductionDispatchAdapterRegistry adapters,
        IProductionReconciliationService reconciliation,
        ISceneAssetRepository assets)
    {
        _repository = repository;
        _adapters = adapters;
        _reconciliation = reconciliation;
        _assets = assets;
    }

    public async Task<IReadOnlyList<ProductionWorkloadSnapshot>> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var workloads = await _repository.ListWorkloadsBySessionAsync(sessionId, cancellationToken);
        var snapshots = new List<ProductionWorkloadSnapshot>(workloads.Count);
        foreach (var workload in workloads)
        {
            var items = await _repository.ListWorkloadItemsAsync(workload.Id, cancellationToken);
            var itemSnapshots = new List<ProductionWorkloadItemSnapshot>(items.Count);
            foreach (var item in items)
            {
                var intent = await _repository.GetIntentAsync(item.IntentSnapshotId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Production intent '{item.IntentSnapshotId}' was not found for workload item '{item.Id}'.");
                var request = await _repository.GetCompiledRequestAsync(item.CompiledRequestId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Compiled request '{item.CompiledRequestId}' was not found for workload item '{item.Id}'.");
                var references = await _repository.ListReferenceBindingsAsync(request.Id, cancellationToken);
                var attempts = await _repository.ListAttemptsAsync(item.Id, cancellationToken);
                var reviews = await _repository.ListReviewDecisionsAsync(item.Id, cancellationToken);
                itemSnapshots.Add(new ProductionWorkloadItemSnapshot(
                    item, intent, request, references, attempts, reviews));
            }
            snapshots.Add(new ProductionWorkloadSnapshot(workload, itemSnapshots));
        }
        return snapshots;
    }

    public async Task<ProductionWorkloadReadiness> CreateDraftAsync(
        ProductionWorkloadDraft draft,
        CancellationToken cancellationToken = default)
    {
        ValidateDraft(draft);
        var diagnostics = new List<ProductionReadinessDiagnostic>();
        var items = new List<ProductionWorkloadItem>();
        var sourceVersions = new List<object>();
        var endpointReadiness = new List<object>();
        decimal estimatedCost = 0;

        for (var ordinal = 0; ordinal < draft.Items.Count; ordinal++)
        {
            var itemDraft = draft.Items[ordinal];
            ValidateItemConfiguration(itemDraft, ordinal, diagnostics);
            var intent = await _repository.GetIntentAsync(itemDraft.IntentSnapshotId, cancellationToken)
                ?? throw new InvalidOperationException($"Production intent '{itemDraft.IntentSnapshotId}' was not found for workload item {ordinal}.");
            var request = await _repository.GetCompiledRequestAsync(itemDraft.CompiledRequestId, cancellationToken)
                ?? throw new InvalidOperationException($"Compiled request '{itemDraft.CompiledRequestId}' was not found for workload item {ordinal}.");
            var profile = await _repository.GetCapabilityProfileAsync(request.CapabilityProfileId, cancellationToken)
                ?? throw new InvalidOperationException($"Capability profile '{request.CapabilityProfileId}' was not found for workload item {ordinal}.");
            var cell = (await _repository.ListCapabilityCellsAsync(profile.Id, cancellationToken))
                .SingleOrDefault(candidate => string.Equals(candidate.Id, request.CapabilityCellId, StringComparison.Ordinal));

            if (!string.Equals(intent.Id, request.IntentSnapshotId, StringComparison.Ordinal))
                diagnostics.Add(Block("request_intent_mismatch", "The compiled request does not bind the selected intent snapshot.", ordinal));
            if (intent.ContextKind != draft.ContextKind
                || !string.Equals(intent.ContextId, draft.ContextId, StringComparison.Ordinal))
                diagnostics.Add(Block("context_mismatch", "The intent snapshot belongs to a different production context.", ordinal));
            if (!string.Equals(profile.ProviderKey, itemDraft.Endpoint.ProviderKey, StringComparison.Ordinal)
                || !string.Equals(request.ProviderKey, itemDraft.Endpoint.ProviderKey, StringComparison.Ordinal))
                diagnostics.Add(Block("provider_mismatch", "Endpoint, profile, and compiled request provider keys must match exactly.", ordinal));
            if (!string.Equals(profile.ContentPolicyKey, draft.ContentPolicyKey, StringComparison.Ordinal))
                diagnostics.Add(Block("policy_mismatch", "The workload content policy does not match the selected capability profile.", ordinal));
            if (!profile.Enabled || profile.Status != MediaCapabilityProfileStatus.Qualified)
                diagnostics.Add(Block("profile_unqualified", $"Capability profile '{profile.Id}' is not enabled and qualified.", ordinal));
            if (cell?.Status != MediaCapabilityCellStatus.Qualified)
                diagnostics.Add(Block("cell_unqualified", $"Capability cell '{request.CapabilityCellId}' is not qualified.", ordinal));

            var compatibilityKey = CompatibilityKey(request, draft.ContentPolicyKey, itemDraft);
            items.Add(new ProductionWorkloadItem
            {
                Id = Guid.NewGuid().ToString("N"), WorkloadId = draft.WorkloadId, Ordinal = ordinal,
                IntentSnapshotId = intent.Id, CompiledRequestId = request.Id,
                CompatibilityKey = compatibilityKey, VariationCount = itemDraft.VariationCount,
                Status = ProductionWorkloadItemStatus.Draft, ConcurrencyVersion = 1,
                RetryPolicySnapshotJson = CanonicalJson(itemDraft.RetryPolicySnapshotJson, "Retry policy"),
                EndpointSnapshotJson = JsonSerializer.Serialize(itemDraft.Endpoint, JsonOptions),
                DispatchPolicySnapshotJson = JsonSerializer.Serialize(itemDraft.DispatchPolicy, JsonOptions),
                CostBasisSnapshotJson = JsonSerializer.Serialize(itemDraft.CostBasis, JsonOptions),
                DependsOnItemId = itemDraft.DependsOnItemId, CreatedUtc = draft.CreatedUtc
            });
            sourceVersions.Add(new
            {
                ordinal, intent.ProductionGroupId, intent.BeatProductionPlanId, intent.BeatProductionPlanVersion,
                intent.MomentSetId, intent.MomentSetVersion, intent.MomentEnrichmentId,
                intent.MomentEnrichmentRevision, request.Id, request.ContentHash, request.CapabilityProfileId,
                request.CapabilityCellId, request.CompilerId, request.CompilerVersion, request.ModelId,
                request.ModelVersion, request.WorkflowRevision
            });
            endpointReadiness.Add(new
            {
                ordinal, itemDraft.Endpoint.ProviderKey, itemDraft.Endpoint.EndpointId,
                itemDraft.Endpoint.BaseUrl, itemDraft.Endpoint.ProtocolKey, itemDraft.Endpoint.TimeoutSeconds,
                readiness = JsonDocument.Parse(itemDraft.Endpoint.ReadinessSnapshotJson).RootElement.Clone()
            });
            estimatedCost += itemDraft.CostBasis.UnitCostPerOutput * itemDraft.VariationCount;
        }

        var groupCount = items.Select(item => item.CompatibilityKey).Distinct(StringComparer.Ordinal).Count();
        var workload = new ProductionWorkload
        {
            Id = draft.WorkloadId, ContextKind = draft.ContextKind, ContextId = draft.ContextId,
            ContextSnapshotJson = CanonicalJson(draft.ContextSnapshotJson, "Production context snapshot"),
            SessionId = draft.SessionId, Revision = draft.Revision,
            Status = ProductionWorkloadStatus.Draft, ConcurrencyVersion = 1,
            Goal = draft.Goal, ContentPolicyKey = draft.ContentPolicyKey,
            SourceVersionSnapshotJson = JsonSerializer.Serialize(sourceVersions, JsonOptions),
            ReadinessSnapshotJson = JsonSerializer.Serialize(new
            {
                ready = diagnostics.All(diagnostic => !diagnostic.Blocking), diagnostics
            }, JsonOptions),
            EndpointReadinessJson = JsonSerializer.Serialize(endpointReadiness, JsonOptions),
            CostEstimateJson = JsonSerializer.Serialize(new
            {
                currency = SingleCurrency(draft.Items), estimatedCost
            }, JsonOptions),
            ItemCount = items.Count, OutputCount = items.Sum(item => item.VariationCount),
            CompatibilityGroupCount = groupCount, CreatedUtc = draft.CreatedUtc
        };

        await _repository.CreateWorkloadAsync(workload, items, cancellationToken);
        var validating = await _repository.TransitionWorkloadAsync(
            workload.Id, ProductionWorkloadStatus.Draft, ProductionWorkloadStatus.Validating, 1, cancellationToken);
        if (diagnostics.Any(diagnostic => diagnostic.Blocking))
        {
            workload = await _repository.TransitionWorkloadAsync(
                workload.Id, ProductionWorkloadStatus.Validating, ProductionWorkloadStatus.Blocked,
                validating.ConcurrencyVersion, cancellationToken);
        }
        else
        {
            for (var index = 0; index < items.Count; index++)
            {
                items[index] = await _repository.TransitionWorkloadItemAsync(
                    items[index].Id, ProductionWorkloadItemStatus.Draft, ProductionWorkloadItemStatus.Ready,
                    items[index].ConcurrencyVersion, cancellationToken);
            }
            workload = await _repository.TransitionWorkloadAsync(
                workload.Id, ProductionWorkloadStatus.Validating, ProductionWorkloadStatus.Ready,
                validating.ConcurrencyVersion, cancellationToken);
        }
        return new ProductionWorkloadReadiness(workload, items, diagnostics);
    }

    public async Task SubmitAsync(string workloadId, CancellationToken cancellationToken = default)
    {
        var workload = await _repository.GetWorkloadAsync(workloadId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload '{workloadId}' was not found.");
        if (workload.Status is not (ProductionWorkloadStatus.Ready or ProductionWorkloadStatus.Queued))
            throw new InvalidOperationException($"Production workload '{workloadId}' is {workload.Status} and cannot be submitted.");
        var items = (await _repository.ListWorkloadItemsAsync(workload.Id, cancellationToken)).ToList();

        if (workload.Status == ProductionWorkloadStatus.Ready)
        {
            workload = await _repository.TransitionWorkloadAsync(
                workload.Id, ProductionWorkloadStatus.Ready, ProductionWorkloadStatus.Queued,
                workload.ConcurrencyVersion, cancellationToken);
            for (var index = 0; index < items.Count; index++)
            {
                items[index] = await _repository.TransitionWorkloadItemAsync(
                    items[index].Id, ProductionWorkloadItemStatus.Ready, ProductionWorkloadItemStatus.Queued,
                    items[index].ConcurrencyVersion, cancellationToken);
                await CreateInitialAttemptsAsync(items[index], cancellationToken);
            }
        }

        foreach (var item in items)
            await SubmitQueuedItemAsync(item.Id, cancellationToken);

        workload = await _repository.GetWorkloadAsync(workload.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload '{workloadId}' disappeared during submission.");
        if (workload.Status == ProductionWorkloadStatus.Queued)
        {
            await _repository.TransitionWorkloadAsync(
                workload.Id, ProductionWorkloadStatus.Queued, ProductionWorkloadStatus.Running,
                workload.ConcurrencyVersion, cancellationToken);
            await _reconciliation.ReconcileWorkloadAsync(workload.Id, cancellationToken);
        }
    }

    public async Task CancelAsync(string workloadId, CancellationToken cancellationToken = default)
    {
        var workload = await _repository.GetWorkloadAsync(workloadId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload '{workloadId}' was not found.");
        var items = await _repository.ListWorkloadItemsAsync(workload.Id, cancellationToken);
        foreach (var item in items.Where(item => item.Status is not (ProductionWorkloadItemStatus.Approved or ProductionWorkloadItemStatus.Rejected or ProductionWorkloadItemStatus.Cancelled)))
        {
            var endpoint = Deserialize<ProductionProviderEndpoint>(item.EndpointSnapshotJson, "endpoint snapshot");
            var policy = Deserialize<ProductionDispatchPolicy>(item.DispatchPolicySnapshotJson, "dispatch policy");
            var adapter = _adapters.Resolve(policy.AdapterKey);
            foreach (var attempt in await _repository.ListAttemptsAsync(item.Id, cancellationToken))
            {
                if (attempt.Status is ProductionAttemptStatus.Submitted or ProductionAttemptStatus.Running)
                    await adapter.CancelAsync(endpoint, attempt.ProviderRequestId!, cancellationToken);
                if (attempt.Status is ProductionAttemptStatus.Pending or ProductionAttemptStatus.Submitted or ProductionAttemptStatus.Running)
                    await _repository.RecordAttemptFailureAsync(
                        attempt.Id, ProductionAttemptStatus.Cancelled, "operator_cancelled",
                        "The production workload was cancelled by the operator.", "{}", "{}",
                        attempt.ConcurrencyVersion, cancellationToken);
            }
            var refreshed = await _repository.GetWorkloadItemAsync(item.Id, cancellationToken);
            if (refreshed?.Status is ProductionWorkloadItemStatus.Ready or ProductionWorkloadItemStatus.Queued
                or ProductionWorkloadItemStatus.Submitted or ProductionWorkloadItemStatus.Running)
            {
                await _repository.TransitionWorkloadItemAsync(
                    refreshed.Id, refreshed.Status, ProductionWorkloadItemStatus.Cancelled,
                    refreshed.ConcurrencyVersion, cancellationToken);
            }
        }
        workload = await _repository.GetWorkloadAsync(workload.Id, cancellationToken) ?? workload;
        if (workload.Status is ProductionWorkloadStatus.Draft or ProductionWorkloadStatus.Ready or ProductionWorkloadStatus.Queued
            or ProductionWorkloadStatus.Running or ProductionWorkloadStatus.PartiallyComplete)
        {
            await _repository.TransitionWorkloadAsync(
                workload.Id, workload.Status, ProductionWorkloadStatus.Cancelled,
                workload.ConcurrencyVersion, cancellationToken);
        }
    }

    public Task ReconcileAsync(string workloadId, CancellationToken cancellationToken = default) =>
        _reconciliation.ReconcileWorkloadAsync(workloadId, cancellationToken);

    public async Task<ProductionReviewDecision> ReviewAsync(
        ProductionReviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Decision == ProductionReviewDecisionValue.Approved)
            throw new InvalidOperationException("Use the production approval command to approve an exact attempt and derivative.");
        var decision = await _repository.AddReviewDecisionAsync(new ProductionReviewDecision
        {
            Id = Guid.NewGuid().ToString("N"),
            WorkloadItemId = command.WorkloadItemId,
            AttemptId = command.AttemptId,
            Decision = command.Decision,
            ReasonCode = command.ReasonCode,
            Notes = command.Notes,
            DecidedBy = command.DecidedBy,
            DecidedUtc = command.DecidedUtc
        }, cancellationToken);
        if (command.Decision == ProductionReviewDecisionValue.Rejected)
        {
            var item = await _repository.GetWorkloadItemAsync(command.WorkloadItemId, cancellationToken)
                ?? throw new InvalidOperationException($"Production workload item '{command.WorkloadItemId}' was not found.");
            await _repository.TransitionWorkloadItemAsync(
                item.Id, ProductionWorkloadItemStatus.Reviewable, ProductionWorkloadItemStatus.Rejected,
                item.ConcurrencyVersion, cancellationToken);
        }
        return decision;
    }

    public async Task<ProductionDerivative> ApproveAsync(
        ProductionApprovalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var item = await _repository.GetWorkloadItemAsync(command.WorkloadItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload item '{command.WorkloadItemId}' was not found.");
        var attempt = await _repository.GetAttemptAsync(command.AttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Production attempt '{command.AttemptId}' was not found.");
        if (!string.Equals(attempt.WorkloadItemId, item.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected production attempt is not owned by the selected workload item.");
        if (attempt.Status != ProductionAttemptStatus.Succeeded || string.IsNullOrWhiteSpace(attempt.OutputSha256))
            throw new InvalidOperationException("Only a successful production attempt with captured output can be approved.");
        var request = await _repository.GetCompiledRequestAsync(item.CompiledRequestId, cancellationToken)
            ?? throw new InvalidOperationException($"Compiled request '{item.CompiledRequestId}' was not found.");
        var asset = await _assets.GetAsync(attempt.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Captured Scene Asset '{attempt.Id}' was not found.");
        if (!string.Equals(asset.Sha256, attempt.OutputSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Captured Scene Asset checksum does not match the selected attempt output.");

        var review = await _repository.AddReviewDecisionAsync(new ProductionReviewDecision
        {
            Id = Guid.NewGuid().ToString("N"), WorkloadItemId = item.Id, AttemptId = attempt.Id,
            Decision = ProductionReviewDecisionValue.Approved, ReasonCode = command.ReasonCode,
            Notes = command.Notes, DecidedBy = command.ApprovedBy, DecidedUtc = command.ApprovedUtc
        }, cancellationToken);
        var approvedAsset = await _assets.ApproveForProductionAsync(
            asset.Id, command.SourceProvenanceJson, command.ConsentState, command.LicenseState,
            command.LicenseLabel, command.ApprovedUseScope, command.ContentPolicyKey,
            command.CompatibilityMetadataJson, cancellationToken);
        var derivative = new ProductionDerivative
        {
            Id = Guid.NewGuid().ToString("N"), WorkloadItemId = item.Id, AttemptId = attempt.Id,
            ReviewDecisionId = review.Id, SceneAssetId = approvedAsset.Id,
            CapabilityProfileId = request.CapabilityProfileId,
            SourceLineageJson = command.SourceProvenanceJson, UseScopeKey = command.UseScopeKey,
            Status = ProductionDerivativeStatus.Approved, ApprovedBy = command.ApprovedBy,
            ApprovedUtc = command.ApprovedUtc
        };
        await _repository.CreateDerivativeAsync(derivative, cancellationToken);
        await _repository.TransitionWorkloadItemAsync(
            item.Id, ProductionWorkloadItemStatus.Reviewable, ProductionWorkloadItemStatus.Approved,
            item.ConcurrencyVersion, cancellationToken);
        return derivative;
    }

    public async Task<ProductionAttempt> RetryAsync(
        string workloadItemId,
        string failedAttemptId,
        DateTime createdUtc,
        CancellationToken cancellationToken = default)
    {
        if (createdUtc.Kind != DateTimeKind.Utc) throw new InvalidOperationException("Retry creation time must be UTC.");
        var item = await _repository.GetWorkloadItemAsync(workloadItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload item '{workloadItemId}' was not found.");
        var parent = await _repository.GetAttemptAsync(failedAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Production attempt '{failedAttemptId}' was not found.");
        if (!string.Equals(parent.WorkloadItemId, item.Id, StringComparison.Ordinal)
            || parent.Status is not (ProductionAttemptStatus.Failed or ProductionAttemptStatus.Cancelled or ProductionAttemptStatus.Indeterminate))
            throw new InvalidOperationException("Retry requires a terminal failed attempt owned by the selected workload item.");
        var attempts = await _repository.ListAttemptsAsync(item.Id, cancellationToken);
        var retry = new ProductionAttempt
        {
            Id = Guid.NewGuid().ToString("N"), WorkloadItemId = item.Id,
            AttemptNumber = attempts.Max(attempt => attempt.AttemptNumber) + 1,
            Kind = ProductionAttemptKind.Retry, Status = ProductionAttemptStatus.Pending,
            ConcurrencyVersion = 1, CompiledRequestId = parent.CompiledRequestId,
            CompiledRequestHash = parent.CompiledRequestHash, RequestSnapshotJson = parent.RequestSnapshotJson,
            ReferenceSnapshotJson = parent.ReferenceSnapshotJson,
            ModelWorkflowSnapshotJson = parent.ModelWorkflowSnapshotJson,
            SettingsSnapshotJson = parent.SettingsSnapshotJson, Seed = parent.Seed,
            ParentAttemptId = parent.Id, CreatedUtc = createdUtc
        };
        await _repository.CreateAttemptAsync(retry, cancellationToken);
        return retry;
    }

    private async Task CreateInitialAttemptsAsync(ProductionWorkloadItem item, CancellationToken cancellationToken)
    {
        if ((await _repository.ListAttemptsAsync(item.Id, cancellationToken)).Count != 0) return;
        var request = await _repository.GetCompiledRequestAsync(item.CompiledRequestId, cancellationToken)
            ?? throw new InvalidOperationException($"Compiled request '{item.CompiledRequestId}' was not found.");
        var bindings = await _repository.ListReferenceBindingsAsync(request.Id, cancellationToken);
        var seed = RequiredSeed(request.CanonicalProviderRequestJson);
        for (var variation = 0; variation < item.VariationCount; variation++)
        {
            await _repository.CreateAttemptAsync(new ProductionAttempt
            {
                Id = Guid.NewGuid().ToString("N"), WorkloadItemId = item.Id, AttemptNumber = variation + 1,
                Kind = variation == 0 ? ProductionAttemptKind.Initial : ProductionAttemptKind.Variation,
                Status = ProductionAttemptStatus.Pending, ConcurrencyVersion = 1,
                CompiledRequestId = request.Id, CompiledRequestHash = request.ContentHash,
                RequestSnapshotJson = request.CanonicalProviderRequestJson,
                ReferenceSnapshotJson = JsonSerializer.Serialize(bindings, JsonOptions),
                ModelWorkflowSnapshotJson = JsonSerializer.Serialize(new
                {
                    request.ProviderKey, request.ModelId, request.ModelVersion, request.WorkflowRevision,
                    request.CompilerId, request.CompilerVersion, request.RequestSchemaVersion
                }, JsonOptions),
                SettingsSnapshotJson = request.CanonicalProviderRequestJson,
                Seed = checked(seed + variation), CreatedUtc = DateTime.UtcNow
            }, cancellationToken);
        }
    }

    private async Task SubmitQueuedItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var item = await _repository.GetWorkloadItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload item '{itemId}' was not found.");
        if (item.Status != ProductionWorkloadItemStatus.Queued) return;
        var endpoint = Deserialize<ProductionProviderEndpoint>(item.EndpointSnapshotJson, "endpoint snapshot");
        var policy = Deserialize<ProductionDispatchPolicy>(item.DispatchPolicySnapshotJson, "dispatch policy");
        var adapter = _adapters.Resolve(policy.AdapterKey);
        var attempts = (await _repository.ListAttemptsAsync(item.Id, cancellationToken)).ToList();
        var pending = attempts.Where(attempt => attempt.Status == ProductionAttemptStatus.Pending).ToList();
        var request = await _repository.GetCompiledRequestAsync(item.CompiledRequestId, cancellationToken)
            ?? throw new InvalidOperationException($"Compiled request '{item.CompiledRequestId}' was not found.");

        var units = policy.SupportsNativeVariations
            ? pending.Chunk(policy.MaximumOutputsPerRequest).Select(chunk => chunk.ToList())
            : pending.Select(attempt => new List<ProductionAttempt> { attempt });
        foreach (var unit in units)
        {
            var group = new ProductionDispatchGroup(item.CompatibilityKey, endpoint, policy,
                unit.Select(attempt => new ProductionDispatchAttempt(attempt, request)).ToList());
            var submissions = await adapter.SubmitAsync(group, cancellationToken);
            if (submissions.Count != unit.Count)
                throw new InvalidOperationException("Provider submission count did not match the persisted attempt count.");
            foreach (var submission in submissions)
            {
                var attempt = unit.Single(candidate => string.Equals(candidate.Id, submission.AttemptId, StringComparison.Ordinal));
                await _repository.RecordProviderSubmissionAsync(
                    attempt.Id, endpoint.ProviderKey, submission.ProviderRequestId,
                    submission.ProviderStatusUrl, attempt.ConcurrencyVersion, cancellationToken);
                if (submission.State is ProductionProviderJobState.Succeeded or ProductionProviderJobState.Failed
                    or ProductionProviderJobState.Cancelled or ProductionProviderJobState.Expired)
                    await _reconciliation.CaptureSubmissionAsync(submission, cancellationToken);
            }
        }
        item = await _repository.GetWorkloadItemAsync(item.Id, cancellationToken) ?? item;
        var persisted = await _repository.ListAttemptsAsync(item.Id, cancellationToken);
        if (persisted.All(attempt => !string.IsNullOrWhiteSpace(attempt.ProviderRequestId))
            && item.Status == ProductionWorkloadItemStatus.Queued)
        {
            await _repository.TransitionWorkloadItemAsync(
                item.Id, ProductionWorkloadItemStatus.Queued, ProductionWorkloadItemStatus.Submitted,
                item.ConcurrencyVersion, cancellationToken);
            await _reconciliation.ReconcileWorkloadAsync(item.WorkloadId, cancellationToken);
        }
    }

    private static string CompatibilityKey(
        CompiledMediaRequest request,
        string policyKey,
        ProductionWorkloadDraftItem item)
    {
        using var document = JsonDocument.Parse(request.CanonicalProviderRequestJson);
        var width = RequiredJsonScalar(document.RootElement, "width");
        var height = RequiredJsonScalar(document.RootElement, "height");
        return ProductionContentHash.Compute(
            request.ProviderKey, item.Endpoint.EndpointId, item.Endpoint.BaseUrl,
            request.ModelId, request.ModelVersion, request.WorkflowRevision,
            request.CompilerId, request.CompilerVersion, request.RequestSchemaVersion,
            policyKey, width, height, item.DispatchPolicy.AdapterKey,
            item.DispatchPolicy.WorkerImage, item.DispatchPolicy.ArtifactSet,
            item.DispatchPolicy.ReferenceAccessibility, item.DispatchPolicy.ResultRetentionSeconds.ToString(CultureInfo.InvariantCulture));
    }

    private static string RequiredJsonScalar(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new InvalidOperationException($"Compiled request field '{name}' is required for dispatch grouping.");
        return value.ToString();
    }

    private static long RequiredSeed(string requestJson)
    {
        using var document = JsonDocument.Parse(requestJson);
        if (!document.RootElement.TryGetProperty("seed", out var value) || !value.TryGetInt64(out var seed))
            throw new InvalidOperationException("Compiled request requires an explicit integer seed before workload submission.");
        return seed;
    }

    private static void ValidateDraft(ProductionWorkloadDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Require(draft.WorkloadId, "Workload id");
        if (!Enum.IsDefined(draft.ContextKind)) throw new InvalidOperationException("Workload context kind must be explicit.");
        Require(draft.ContextId, "Workload context id");
        CanonicalJson(draft.ContextSnapshotJson, "Production context snapshot");
        if (draft.ContextKind == ProductionContextKind.SceneMoment)
        {
            Require(draft.SessionId, "Workload session id");
            if (!string.Equals(draft.ContextId, draft.SessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Scene workload context id must equal the exact session id.");
        }
        else if (!string.IsNullOrWhiteSpace(draft.SessionId))
            throw new InvalidOperationException("Character Asset workload cannot contain a scene session id.");
        if (draft.Revision <= 0) throw new InvalidOperationException("Workload revision must be positive.");
        Require(draft.Goal, "Workload goal");
        Require(draft.ContentPolicyKey, "Workload content policy key");
        CanonicalJson(draft.SourceVersionSnapshotJson, "Source version snapshot");
        if (draft.Items.Count == 0) throw new InvalidOperationException("A production workload requires at least one item.");
        if (draft.CreatedUtc.Kind != DateTimeKind.Utc) throw new InvalidOperationException("Workload creation time must be UTC.");
    }

    private static void ValidateItemConfiguration(
        ProductionWorkloadDraftItem item,
        int ordinal,
        ICollection<ProductionReadinessDiagnostic> diagnostics)
    {
        Require(item.IntentSnapshotId, $"Item {ordinal} intent snapshot id");
        Require(item.CompiledRequestId, $"Item {ordinal} compiled request id");
        if (item.VariationCount <= 0) throw new InvalidOperationException($"Item {ordinal} variation count must be positive.");
        CanonicalJson(item.RetryPolicySnapshotJson, $"Item {ordinal} retry policy");
        Require(item.Endpoint.ProviderKey, $"Item {ordinal} provider key");
        Require(item.Endpoint.EndpointId, $"Item {ordinal} endpoint id");
        if (!Uri.TryCreate(item.Endpoint.BaseUrl, UriKind.Absolute, out _))
            diagnostics.Add(Block("endpoint_url_invalid", "The endpoint base URL must be absolute.", ordinal));
        Require(item.Endpoint.SubmitPath, $"Item {ordinal} submit path");
        Require(item.Endpoint.StatusPathTemplate, $"Item {ordinal} status path template");
        Require(item.Endpoint.CancelPathTemplate, $"Item {ordinal} cancel path template");
        Require(item.Endpoint.ProtocolKey, $"Item {ordinal} endpoint protocol");
        if (item.Endpoint.TimeoutSeconds <= 0)
            diagnostics.Add(Block("endpoint_timeout_invalid", "Endpoint timeout must be positive.", ordinal));
        using var readiness = JsonDocument.Parse(CanonicalJson(item.Endpoint.ReadinessSnapshotJson, "Endpoint readiness"));
        if (!readiness.RootElement.TryGetProperty("ready", out var ready) || ready.ValueKind != JsonValueKind.True)
            diagnostics.Add(Block("endpoint_not_ready", "The persisted endpoint readiness check is not ready.", ordinal));
        Require(item.DispatchPolicy.AdapterKey, $"Item {ordinal} adapter key");
        Require(item.DispatchPolicy.WorkerImage, $"Item {ordinal} worker image");
        Require(item.DispatchPolicy.ArtifactSet, $"Item {ordinal} artifact set");
        Require(item.DispatchPolicy.ReferenceAccessibility, $"Item {ordinal} reference accessibility");
        if (item.DispatchPolicy.MaximumOutputsPerRequest <= 0 || item.DispatchPolicy.ResultRetentionSeconds <= 0)
            diagnostics.Add(Block("dispatch_policy_invalid", "Output limit and result retention must be positive.", ordinal));
        if (item.VariationCount > 1 && !item.DispatchPolicy.SupportsNativeVariations)
            diagnostics.Add(new ProductionReadinessDiagnostic(
                "variations_split", "Variations will be submitted as independent provider jobs.", false, ordinal));
        if (item.DispatchPolicy.SupportsNativeVariations && item.VariationCount > item.DispatchPolicy.MaximumOutputsPerRequest)
            diagnostics.Add(new ProductionReadinessDiagnostic(
                "variations_chunked", "Variations will be split into bounded provider requests.", false, ordinal));
        Require(item.CostBasis.Currency, $"Item {ordinal} cost currency");
        if (item.CostBasis.UnitCostPerOutput < 0)
            diagnostics.Add(Block("cost_invalid", "Unit cost per output cannot be negative.", ordinal));
    }

    private static string SingleCurrency(IReadOnlyList<ProductionWorkloadDraftItem> items)
    {
        var currencies = items.Select(item => item.CostBasis.Currency.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (currencies.Count != 1) throw new InvalidOperationException("A workload cost estimate requires one explicit currency.");
        return currencies[0];
    }

    private static ProductionReadinessDiagnostic Block(string code, string message, int ordinal) =>
        new(code, message, true, ordinal);

    private static string CanonicalJson(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.GetRawText();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{label} must be valid JSON.", exception);
        }
    }

    private static T Deserialize<T>(string value, string label) =>
        JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidOperationException($"Persisted {label} was null.");

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }
}