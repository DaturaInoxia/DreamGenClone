using System.Security.Cryptography;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class ProductionReconciliationService : IProductionReconciliationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IProductionMediaRepository _repository;
    private readonly IProductionDispatchAdapterRegistry _adapters;
    private readonly ISceneImageStorageService _storage;
    private readonly ISceneAssetRepository _assets;
    private readonly IHttpClientFactory _httpClientFactory;

    public ProductionReconciliationService(
        IProductionMediaRepository repository,
        IProductionDispatchAdapterRegistry adapters,
        ISceneImageStorageService storage,
        ISceneAssetRepository assets,
        IHttpClientFactory httpClientFactory)
    {
        _repository = repository;
        _adapters = adapters;
        _storage = storage;
        _assets = assets;
        _httpClientFactory = httpClientFactory;
    }

    public async Task CaptureSubmissionAsync(
        ProductionProviderSubmission submission,
        CancellationToken cancellationToken = default)
    {
        await ApplyResultAsync(submission.AttemptId, new ProductionProviderPollResult(
            submission.State, submission.ProviderResponseSnapshotJson, submission.CostSnapshotJson,
            submission.Outputs,
            submission.State == ProductionProviderJobState.Failed ? "provider_submission_failed" : null,
            submission.State == ProductionProviderJobState.Failed ? "Provider submission returned a failed terminal result." : null),
            cancellationToken);
    }

    public async Task ReconcileWorkloadAsync(
        string workloadId,
        CancellationToken cancellationToken = default)
    {
        var workload = await _repository.GetWorkloadAsync(workloadId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload '{workloadId}' was not found.");
        foreach (var item in await _repository.ListWorkloadItemsAsync(workload.Id, cancellationToken))
        {
            foreach (var attempt in await _repository.ListAttemptsAsync(item.Id, cancellationToken))
            {
                if (attempt.Status is ProductionAttemptStatus.Submitted or ProductionAttemptStatus.Running
                    or ProductionAttemptStatus.Indeterminate)
                    await ReconcileAttemptAsync(attempt.Id, cancellationToken);
            }
            await AggregateItemAsync(item.Id, cancellationToken);
        }
        await AggregateWorkloadAsync(workload.Id, cancellationToken);
    }

    public async Task ReconcileAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await _repository.GetAttemptAsync(attemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Production attempt '{attemptId}' was not found.");
        if (attempt.Status is not (ProductionAttemptStatus.Submitted or ProductionAttemptStatus.Running or ProductionAttemptStatus.Indeterminate)) return;
        if (string.IsNullOrWhiteSpace(attempt.ProviderRequestId))
            throw new InvalidOperationException($"Submitted attempt '{attempt.Id}' has no provider request id.");
        var item = await _repository.GetWorkloadItemAsync(attempt.WorkloadItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload item '{attempt.WorkloadItemId}' was not found.");
        var endpoint = Deserialize<ProductionProviderEndpoint>(item.EndpointSnapshotJson, "endpoint snapshot");
        var policy = Deserialize<ProductionDispatchPolicy>(item.DispatchPolicySnapshotJson, "dispatch policy");
        try
        {
            var result = await _adapters.Resolve(policy.AdapterKey)
                .PollAsync(endpoint, attempt.ProviderRequestId, cancellationToken);
            await ApplyResultAsync(attempt.Id, result, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            attempt = await _repository.GetAttemptAsync(attempt.Id, cancellationToken) ?? attempt;
            await _repository.RecordAttemptFailureAsync(
                attempt.Id, ProductionAttemptStatus.Indeterminate, "provider_poll_timeout",
                $"Provider polling exceeded the explicit {endpoint.TimeoutSeconds}-second endpoint timeout.",
                "{}", "{}", attempt.ConcurrencyVersion, cancellationToken);
        }
        await AggregateItemAsync(item.Id, cancellationToken);
        var workload = await FindWorkloadAsync(item, cancellationToken);
        await AggregateWorkloadAsync(workload.Id, cancellationToken);
    }

    private async Task ApplyResultAsync(
        string attemptId,
        ProductionProviderPollResult result,
        CancellationToken cancellationToken)
    {
        var attempt = await _repository.GetAttemptAsync(attemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Production attempt '{attemptId}' was not found.");
        if (attempt.Status is ProductionAttemptStatus.Succeeded or ProductionAttemptStatus.Failed
            or ProductionAttemptStatus.Cancelled)
            return;

        if (result.State == ProductionProviderJobState.Running
            && attempt.Status == ProductionAttemptStatus.Submitted)
        {
            await _repository.TransitionAttemptAsync(
                attempt.Id, ProductionAttemptStatus.Submitted, ProductionAttemptStatus.Running,
                attempt.ConcurrencyVersion, cancellationToken);
            return;
        }
        if (result.State == ProductionProviderJobState.Queued) return;
        if (result.State == ProductionProviderJobState.Succeeded)
        {
            if (result.Outputs.Count != 1)
                throw new InvalidOperationException("Each immutable production attempt must reconcile exactly one provider output.");
            var item = await _repository.GetWorkloadItemAsync(attempt.WorkloadItemId, cancellationToken)
                ?? throw new InvalidOperationException($"Production workload item '{attempt.WorkloadItemId}' was not found.");
            var workload = await FindWorkloadAsync(item, cancellationToken);
            var output = result.Outputs[0];
            var bytes = await ReadOutputAsync(output, cancellationToken);
            if (bytes.Length == 0) throw new InvalidOperationException("Provider output was empty.");
            var extension = output.MediaType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                _ => throw new InvalidOperationException($"Unsupported production output media type '{output.MediaType}'.")
            };
            await using var stream = new MemoryStream(bytes, writable: false);
            var relativePath = await _storage.SaveAsync(
                workload.ContextId, $"production-{attempt.Id}{extension}", stream, cancellationToken);
            var attempts = await _repository.ListAttemptsAsync(item.Id, cancellationToken);
            var late = attempts.Any(candidate => candidate.AttemptNumber > attempt.AttemptNumber
                && candidate.Kind == ProductionAttemptKind.Retry);
            var costBasis = Deserialize<ProductionCostBasis>(item.CostBasisSnapshotJson, "cost basis");
            attempt = await _repository.GetAttemptAsync(attempt.Id, cancellationToken) ?? attempt;
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            var metadataJson = JsonSerializer.Serialize(new
                {
                    output.Ordinal, output.MediaType, output.MetadataJson, late,
                    capturedUtc = DateTime.UtcNow
                }, JsonOptions);
            var costJson = JsonSerializer.Serialize(new
                {
                    estimated = new { costBasis.Currency, amount = costBasis.UnitCostPerOutput },
                    provider = JsonDocument.Parse(result.CostSnapshotJson).RootElement.Clone()
                }, JsonOptions);
            if (attempt.Status == ProductionAttemptStatus.Indeterminate)
            {
                await _repository.RecordLateAttemptResultAsync(
                    attempt.Id, relativePath, sha256, bytes.LongLength, metadataJson,
                    result.ProviderResponseSnapshotJson, costJson,
                    attempt.ConcurrencyVersion, cancellationToken);
            }
            else
            {
                attempt = await _repository.RecordAttemptResultAsync(
                    attempt.Id, relativePath, sha256, bytes.LongLength, metadataJson,
                    result.ProviderResponseSnapshotJson, costJson,
                    attempt.ConcurrencyVersion, cancellationToken);
            }
            if (!late)
                await RegisterOutputAssetAsync(workload, item, attempt, output, cancellationToken);
            return;
        }

        var terminalStatus = result.State switch
        {
            ProductionProviderJobState.Failed => ProductionAttemptStatus.Failed,
            ProductionProviderJobState.Cancelled => ProductionAttemptStatus.Cancelled,
            ProductionProviderJobState.Expired => ProductionAttemptStatus.Indeterminate,
            _ => throw new InvalidOperationException($"Provider state '{result.State}' is not a terminal failure state.")
        };
        await _repository.RecordAttemptFailureAsync(
            attempt.Id, terminalStatus,
            result.FailureCode ?? (result.State == ProductionProviderJobState.Expired ? "provider_result_expired" : "provider_terminal_failure"),
            result.FailureDiagnostic ?? $"Provider attempt ended as {result.State}.",
            result.ProviderResponseSnapshotJson, result.CostSnapshotJson,
            attempt.ConcurrencyVersion, cancellationToken);
    }

    private async Task RegisterOutputAssetAsync(
        ProductionWorkload workload,
        ProductionWorkloadItem item,
        ProductionAttempt attempt,
        ProductionProviderOutput output,
        CancellationToken cancellationToken)
    {
        var intent = await _repository.GetIntentAsync(item.IntentSnapshotId, cancellationToken)
            ?? throw new InvalidOperationException($"Production intent '{item.IntentSnapshotId}' was not found.");
        var request = await _repository.GetCompiledRequestAsync(item.CompiledRequestId, cancellationToken)
            ?? throw new InvalidOperationException($"Compiled media request '{item.CompiledRequestId}' was not found.");
        using var contextDocument = JsonDocument.Parse(workload.ContextSnapshotJson);
        var context = contextDocument.RootElement;
        var isCharacterAsset = workload.ContextKind == ProductionContextKind.CharacterAsset;
        var assetName = isCharacterAsset
            ? RequiredContextString(context, "assetName")
            : $"Production {intent.MomentId} attempt {attempt.AttemptNumber}";
        var assetType = isCharacterAsset
            ? RequiredAssetType(context)
            : SceneAssetType.ProductionFrame;
        var now = DateTime.UtcNow;
        var provenance = JsonSerializer.Serialize(new
        {
            contextKind = workload.ContextKind.ToString(), workload.ContextId,
            context = context.Clone(), intentId = intent.Id, intentContentHash = intent.ContentHash,
            compiledRequestId = request.Id, requestContentHash = request.ContentHash,
            workloadId = workload.Id, workloadItemId = item.Id, attemptId = attempt.Id,
            attempt.AttemptNumber, attempt.CompiledRequestHash, attempt.OutputFileRelativePath,
            attempt.OutputSha256, attempt.OutputByteLength, output.MetadataJson
        }, JsonOptions);
        await _assets.UpsertAsync(new SceneAsset
        {
            Id = attempt.Id,
            Name = assetName,
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Complete,
            Type = assetType,
            AssociationMetadataJson = workload.ContextSnapshotJson,
            Prompt = request.CanonicalProviderRequestJson,
            ModelSnapshotJson = JsonSerializer.Serialize(new
            {
                request.ProviderKey, request.ModelId, request.ModelVersion,
                request.WorkflowRevision, request.CompilerId, request.CompilerVersion
            }, JsonOptions),
            FileRelativePath = attempt.OutputFileRelativePath,
            MediaType = output.MediaType,
            ByteLength = attempt.OutputByteLength ?? 0,
            Sha256 = attempt.OutputSha256 ?? string.Empty,
            IdentityPackId = isCharacterAsset ? RequiredContextString(context, "identityPackId") : null,
            CharacterProfileId = isCharacterAsset ? RequiredContextString(context, "characterProfileId") : null,
            SourceSha256 = attempt.OutputSha256,
            SourceProvenanceJson = provenance,
            ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Draft,
            ContentPolicyKey = workload.ContentPolicyKey,
            CreatedUtc = now,
            CompletedUtc = now,
            UpdatedUtc = now
        }, cancellationToken);
    }

    private static SceneAssetType RequiredAssetType(JsonElement context)
    {
        var assetTypeText = RequiredContextString(context, "assetType");
        if (!Enum.TryParse<SceneAssetType>(assetTypeText, ignoreCase: true, out var assetType)
            || !Enum.IsDefined(assetType))
            throw new InvalidOperationException($"Character Asset context assetType '{assetTypeText}' is invalid.");
        return assetType;
    }

    private static string RequiredContextString(JsonElement context, string propertyName)
    {
        if (!context.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Character Asset context requires nonempty string property '{propertyName}'.");
        return value.GetString()!.Trim();
    }

    private async Task AggregateItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var item = await _repository.GetWorkloadItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload item '{itemId}' was not found.");
        if (item.Status is not (ProductionWorkloadItemStatus.Submitted or ProductionWorkloadItemStatus.Running)) return;
        var attempts = await _repository.ListAttemptsAsync(item.Id, cancellationToken);
        var relevant = attempts.OrderByDescending(attempt => attempt.AttemptNumber)
            .Take(item.VariationCount).ToList();
        if (item.Status == ProductionWorkloadItemStatus.Submitted
            && relevant.Any(attempt => attempt.Status == ProductionAttemptStatus.Running))
        {
            item = await _repository.TransitionWorkloadItemAsync(
                item.Id, ProductionWorkloadItemStatus.Submitted, ProductionWorkloadItemStatus.Running,
                item.ConcurrencyVersion, cancellationToken);
        }
        if (relevant.Count < item.VariationCount || relevant.Any(attempt => !IsTerminal(attempt.Status))) return;
        if (relevant.Any(attempt => attempt.Status == ProductionAttemptStatus.Succeeded))
        {
            if (item.Status == ProductionWorkloadItemStatus.Submitted)
                item = await _repository.TransitionWorkloadItemAsync(
                    item.Id, ProductionWorkloadItemStatus.Submitted, ProductionWorkloadItemStatus.Running,
                    item.ConcurrencyVersion, cancellationToken);
            await _repository.TransitionWorkloadItemAsync(
                item.Id, ProductionWorkloadItemStatus.Running, ProductionWorkloadItemStatus.Reviewable,
                item.ConcurrencyVersion, cancellationToken);
        }
        else
        {
            await _repository.TransitionWorkloadItemAsync(
                item.Id, item.Status, ProductionWorkloadItemStatus.Failed,
                item.ConcurrencyVersion, cancellationToken);
        }
    }

    private async Task AggregateWorkloadAsync(string workloadId, CancellationToken cancellationToken)
    {
        var workload = await _repository.GetWorkloadAsync(workloadId, cancellationToken)
            ?? throw new InvalidOperationException($"Production workload '{workloadId}' was not found.");
        if (workload.Status is not (ProductionWorkloadStatus.Running or ProductionWorkloadStatus.PartiallyComplete)) return;
        var items = await _repository.ListWorkloadItemsAsync(workload.Id, cancellationToken);
        var finished = items.Count(item => item.Status is ProductionWorkloadItemStatus.Reviewable
            or ProductionWorkloadItemStatus.Approved or ProductionWorkloadItemStatus.Rejected
            or ProductionWorkloadItemStatus.Failed or ProductionWorkloadItemStatus.Cancelled);
        if (finished == 0) return;
        if (finished < items.Count && workload.Status == ProductionWorkloadStatus.Running)
        {
            await _repository.TransitionWorkloadAsync(
                workload.Id, ProductionWorkloadStatus.Running, ProductionWorkloadStatus.PartiallyComplete,
                workload.ConcurrencyVersion, cancellationToken);
            return;
        }
        if (finished == items.Count)
        {
            var next = items.All(item => item.Status is ProductionWorkloadItemStatus.Failed or ProductionWorkloadItemStatus.Cancelled)
                ? ProductionWorkloadStatus.Failed
                : ProductionWorkloadStatus.Complete;
            await _repository.TransitionWorkloadAsync(
                workload.Id, workload.Status, next, workload.ConcurrencyVersion, cancellationToken);
        }
    }

    private async Task<byte[]> ReadOutputAsync(
        ProductionProviderOutput output,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(output.Base64Data))
        {
            var value = output.Base64Data;
            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var separator = value.IndexOf(',');
                if (separator < 0) throw new InvalidOperationException("Provider data URI has no payload separator.");
                value = value[(separator + 1)..];
            }
            return Convert.FromBase64String(value);
        }
        if (string.IsNullOrWhiteSpace(output.TransientUrl)
            || !Uri.TryCreate(output.TransientUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Provider output requires base64 data or an absolute transient URL.");
        return await _httpClientFactory.CreateClient("CompletionClient")
            .GetByteArrayAsync(uri, cancellationToken);
    }

    private async Task<ProductionWorkload> FindWorkloadAsync(
        ProductionWorkloadItem item,
        CancellationToken cancellationToken) =>
        await _repository.GetWorkloadAsync(item.WorkloadId, cancellationToken)
        ?? throw new InvalidOperationException($"Production workload '{item.WorkloadId}' was not found.");

    private static bool IsTerminal(ProductionAttemptStatus status) =>
        status is ProductionAttemptStatus.Succeeded or ProductionAttemptStatus.Failed
            or ProductionAttemptStatus.Cancelled or ProductionAttemptStatus.Indeterminate;

    private static T Deserialize<T>(string value, string label) =>
        JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidOperationException($"Persisted {label} was null.");
}