using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ProductionMediaRepository : IProductionMediaRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public ProductionMediaRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateCapabilityProfileAsync(
        MediaCapabilityProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateCapabilityProfile(profile);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MediaCapabilityProfiles
                (Id, ProviderKey, ModelId, ModelVersion, Operation, CompilerId, CompilerVersion,
                 WorkflowRevision, ContentPolicyKey, Status, Enabled, EvidenceRunId, PayloadJson, CreatedUtc)
            VALUES ($id, $provider, $model, $modelVersion, $operation, $compiler, $compilerVersion,
                    $workflow, $policy, $status, $enabled, $evidence, $payload, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", profile.Id.Trim());
        command.Parameters.AddWithValue("$provider", profile.ProviderKey.Trim());
        command.Parameters.AddWithValue("$model", profile.ModelId.Trim());
        command.Parameters.AddWithValue("$modelVersion", profile.ModelVersion.Trim());
        command.Parameters.AddWithValue("$operation", profile.Operation.ToString());
        command.Parameters.AddWithValue("$compiler", profile.CompilerId.Trim());
        command.Parameters.AddWithValue("$compilerVersion", profile.CompilerVersion.Trim());
        command.Parameters.AddWithValue("$workflow", profile.WorkflowRevision.Trim());
        command.Parameters.AddWithValue("$policy", profile.ContentPolicyKey.Trim());
        command.Parameters.AddWithValue("$status", profile.Status.ToString());
        command.Parameters.AddWithValue("$enabled", profile.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$evidence", profile.EvidenceRunId.Trim());
        command.Parameters.AddWithValue("$payload", Serialize(profile));
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(profile.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MediaCapabilityProfile?> GetCapabilityProfileAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Capability profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM MediaCapabilityProfiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Trim());
        return DeserializeOrNull<MediaCapabilityProfile>(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<MediaCapabilityProfile>> ListCapabilityProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM MediaCapabilityProfiles ORDER BY CreatedUtc, Id;";
        return await ReadPayloadsAsync<MediaCapabilityProfile>(command, cancellationToken);
    }

    public async Task AddCapabilityCellAsync(
        MediaCapabilityCell cell, CancellationToken cancellationToken = default)
    {
        ValidateCapabilityCell(cell);
        var profile = await GetCapabilityProfileAsync(cell.CapabilityProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"Capability profile '{cell.CapabilityProfileId}' was not found.");
        var supportedStrategies = ParseIdentityStrategies(profile.SupportedIdentityStrategiesJson);
        if (cell.IdentityStrategyKind is { } strategy && !supportedStrategies.Contains(strategy))
        {
            throw new InvalidOperationException(
                $"Capability profile '{profile.Id}' does not declare identity strategy '{strategy}'.");
        }
            await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MediaCapabilityCells
                (Id, CapabilityProfileId, ActorCount, FaceAngleKey, CropKey, PoseClassKey,
                 CompositionClassKey, ReferenceControlTupleJson, Status, EvidenceRunId,
                 FailureReason, PayloadJson, CreatedUtc)
            VALUES ($id, $profile, $actors, $angle, $crop, $pose, $composition, $tuple,
                    $status, $evidence, $failure, $payload, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", cell.Id.Trim());
        command.Parameters.AddWithValue("$profile", cell.CapabilityProfileId.Trim());
        command.Parameters.AddWithValue("$actors", cell.ActorCount);
        command.Parameters.AddWithValue("$angle", cell.FaceAngleKey.Trim());
        command.Parameters.AddWithValue("$crop", cell.CropKey.Trim());
        command.Parameters.AddWithValue("$pose", cell.PoseClassKey.Trim());
        command.Parameters.AddWithValue("$composition", cell.CompositionClassKey.Trim());
        command.Parameters.AddWithValue("$tuple", cell.ReferenceControlTupleJson.Trim());
        command.Parameters.AddWithValue("$status", cell.Status.ToString());
        command.Parameters.AddWithValue("$evidence", cell.EvidenceRunId.Trim());
        command.Parameters.AddWithValue("$failure", (object?)cell.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", Serialize(cell));
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(cell.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaCapabilityCell>> ListCapabilityCellsAsync(
        string profileId, CancellationToken cancellationToken = default)
    {
        Require(profileId, "Capability profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM MediaCapabilityCells WHERE CapabilityProfileId = $id ORDER BY CreatedUtc, Id;";
        command.Parameters.AddWithValue("$id", profileId.Trim());
        return await ReadPayloadsAsync<MediaCapabilityCell>(command, cancellationToken);
    }

    public async Task CreateIntentAsync(
        ProductionIntentSnapshot intent, CancellationToken cancellationToken = default)
    {
        ValidateIntent(intent);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProductionIntentSnapshots
                (Id, ContextKind, ContextId, ContextSnapshotJson,
                 ProductionGroupId, SessionId, CatalogueId, BeatId, BeatProductionPlanId,
                 BeatProductionPlanVersion, MomentSetId, MomentSetVersion, MomentId,
                 MomentEnrichmentId, MomentEnrichmentRevision, Pov, Operation, ContentHash,
                 PayloadJson, CreatedUtc)
            VALUES ($id, $contextKind, $contextId, $contextSnapshot,
                    $group, $session, $catalogue, $beat, $plan, $planVersion, $momentSet,
                    $momentSetVersion, $moment, $enrichment, $enrichmentRevision, $pov,
                    $operation, $hash, $payload, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", intent.Id.Trim());
        command.Parameters.AddWithValue("$contextKind", intent.ContextKind.ToString());
        command.Parameters.AddWithValue("$contextId", intent.ContextId.Trim());
        command.Parameters.AddWithValue("$contextSnapshot", intent.ContextSnapshotJson.Trim());
        command.Parameters.AddWithValue("$group", DbText(intent.ProductionGroupId));
        command.Parameters.AddWithValue("$session", DbText(intent.SessionId));
        command.Parameters.AddWithValue("$catalogue", DbText(intent.CatalogueId));
        command.Parameters.AddWithValue("$beat", DbText(intent.BeatId));
        command.Parameters.AddWithValue("$plan", DbText(intent.BeatProductionPlanId));
        command.Parameters.AddWithValue("$planVersion", DbPositive(intent.BeatProductionPlanVersion));
        command.Parameters.AddWithValue("$momentSet", DbText(intent.MomentSetId));
        command.Parameters.AddWithValue("$momentSetVersion", DbPositive(intent.MomentSetVersion));
        command.Parameters.AddWithValue("$moment", DbText(intent.MomentId));
        command.Parameters.AddWithValue("$enrichment", DbText(intent.MomentEnrichmentId));
        command.Parameters.AddWithValue("$enrichmentRevision", DbPositive(intent.MomentEnrichmentRevision));
        command.Parameters.AddWithValue("$pov", intent.Pov.Trim());
        command.Parameters.AddWithValue("$operation", intent.Operation.ToString());
        command.Parameters.AddWithValue("$hash", intent.ContentHash);
        command.Parameters.AddWithValue("$payload", Serialize(intent));
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(intent.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProductionIntentSnapshot?> GetIntentAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Production intent id");
        return await GetPayloadAsync<ProductionIntentSnapshot>("ProductionIntentSnapshots", id, cancellationToken);
    }

    public async Task CreateCompiledRequestAsync(
        CompiledMediaRequest request,
        IReadOnlyList<OrderedMediaReferenceBinding> bindings,
        CancellationToken cancellationToken = default) =>
        await CreateCompiledRequestGraphAsync(request, bindings, [], cancellationToken);

    public async Task CreateIdentityCompiledRequestAsync(
        CompiledMediaRequest request,
        IReadOnlyList<OrderedMediaReferenceBinding> referenceBindings,
        IReadOnlyList<IdentityStrategyBinding> identityBindings,
        CancellationToken cancellationToken = default)
    {
        if (identityBindings.Count == 0)
            throw new InvalidOperationException("Identity compiled requests require at least one strategy binding.");
        await CreateCompiledRequestGraphAsync(request, referenceBindings, identityBindings, cancellationToken);
    }

    private async Task CreateCompiledRequestGraphAsync(
        CompiledMediaRequest request,
        IReadOnlyList<OrderedMediaReferenceBinding> bindings,
        IReadOnlyList<IdentityStrategyBinding> identityBindings,
        CancellationToken cancellationToken)
    {
        ValidateCompiledRequest(request, bindings);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO CompiledMediaRequests
                    (Id, IntentSnapshotId, CapabilityProfileId, CapabilityCellId, CompilerId,
                     CompilerVersion, RequestSchemaVersion, ProviderKey, ModelId, ModelVersion,
                     WorkflowRevision, ContentHash, PayloadJson, CreatedUtc)
                VALUES ($id, $intent, $profile, $cell, $compiler, $compilerVersion, $schema,
                        $provider, $model, $modelVersion, $workflow, $hash, $payload, $createdUtc);
                """;
            command.Parameters.AddWithValue("$id", request.Id.Trim());
            command.Parameters.AddWithValue("$intent", request.IntentSnapshotId.Trim());
            command.Parameters.AddWithValue("$profile", request.CapabilityProfileId.Trim());
            command.Parameters.AddWithValue("$cell", request.CapabilityCellId.Trim());
            command.Parameters.AddWithValue("$compiler", request.CompilerId.Trim());
            command.Parameters.AddWithValue("$compilerVersion", request.CompilerVersion.Trim());
            command.Parameters.AddWithValue("$schema", request.RequestSchemaVersion.Trim());
            command.Parameters.AddWithValue("$provider", request.ProviderKey.Trim());
            command.Parameters.AddWithValue("$model", request.ModelId.Trim());
            command.Parameters.AddWithValue("$modelVersion", request.ModelVersion.Trim());
            command.Parameters.AddWithValue("$workflow", request.WorkflowRevision.Trim());
            command.Parameters.AddWithValue("$hash", request.ContentHash);
            command.Parameters.AddWithValue("$payload", Serialize(request));
            command.Parameters.AddWithValue("$createdUtc", FormatUtc(request.CreatedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var binding in bindings.OrderBy(binding => binding.Ordinal))
            await InsertReferenceBindingAsync(connection, transaction, request.Id, binding, cancellationToken);
        foreach (var binding in identityBindings.OrderBy(binding => binding.ActorKey, StringComparer.Ordinal))
        {
            if (!string.Equals(binding.CompiledRequestId, request.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Identity strategy binding request ownership does not match the compiled request.");
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO IdentityStrategyBindings
                    (Id, CompiledRequestId, ActorKey, StrategyKind, CapabilityProfileId,
                     CapabilityCellId, LoraArtifactId, LoraArtifactSha256, PayloadJson, CreatedUtc)
                VALUES ($id, $request, $actor, $strategy, $profile,
                        $cell, $artifact, $sha, $payload, $created);
                """;
            command.Parameters.AddWithValue("$id", binding.Id.Trim());
            command.Parameters.AddWithValue("$request", binding.CompiledRequestId.Trim());
            command.Parameters.AddWithValue("$actor", binding.ActorKey.Trim());
            command.Parameters.AddWithValue("$strategy", binding.StrategyKind.ToString());
            command.Parameters.AddWithValue("$profile", binding.CapabilityProfileId.Trim());
            command.Parameters.AddWithValue("$cell", binding.CapabilityCellId.Trim());
            command.Parameters.AddWithValue("$artifact", DbText(binding.LoraArtifactId));
            command.Parameters.AddWithValue("$sha", DbText(binding.LoraArtifactSha256));
            command.Parameters.AddWithValue("$payload", Serialize(binding));
            command.Parameters.AddWithValue("$created", FormatUtc(binding.CreatedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CompiledMediaRequest?> GetCompiledRequestAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Compiled media request id");
        return await GetPayloadAsync<CompiledMediaRequest>("CompiledMediaRequests", id, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderedMediaReferenceBinding>> ListReferenceBindingsAsync(
        string compiledRequestId, CancellationToken cancellationToken = default)
    {
        Require(compiledRequestId, "Compiled media request id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM OrderedMediaReferenceBindings WHERE CompiledRequestId = $id ORDER BY Ordinal;";
        command.Parameters.AddWithValue("$id", compiledRequestId.Trim());
        return await ReadPayloadsAsync<OrderedMediaReferenceBinding>(command, cancellationToken);
    }

    public async Task CreateWorkloadAsync(
        ProductionWorkload workload,
        IReadOnlyList<ProductionWorkloadItem> items,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkload(workload, items);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertWorkloadAsync(connection, transaction, workload, cancellationToken);
        foreach (var item in items.OrderBy(item => item.Ordinal))
            await InsertWorkloadItemAsync(connection, transaction, workload.Id, item, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProductionWorkload?> GetWorkloadAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Production workload id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetWorkloadAsync(connection, id.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<ProductionWorkload>> ListWorkloadsBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Production workload session id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson, Status, ConcurrencyVersion FROM ProductionWorkloads WHERE SessionId = $session ORDER BY Revision DESC, CreatedUtc DESC;";
        command.Parameters.AddWithValue("$session", sessionId.Trim());
        var results = new List<ProductionWorkload>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var workload = Deserialize<ProductionWorkload>(reader.GetString(0));
            workload.Status = ParseEnum<ProductionWorkloadStatus>(reader.GetString(1), "workload", workload.Id);
            workload.ConcurrencyVersion = reader.GetInt64(2);
            results.Add(workload);
        }
        return results;
    }

    public async Task<ProductionWorkloadItem?> GetWorkloadItemAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Production workload item id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetWorkloadItemAsync(connection, id.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<ProductionWorkloadItem>> ListWorkloadItemsAsync(
        string workloadId, CancellationToken cancellationToken = default)
    {
        Require(workloadId, "Production workload id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson, Status, ConcurrencyVersion, CurrentAttemptId, FailureCode FROM ProductionWorkloadItems WHERE WorkloadId = $id ORDER BY Ordinal;";
        command.Parameters.AddWithValue("$id", workloadId.Trim());
        var results = new List<ProductionWorkloadItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadWorkloadItem(reader));
        return results;
    }

    public async Task<ProductionWorkload> TransitionWorkloadAsync(
        string id,
        ProductionWorkloadStatus expectedStatus,
        ProductionWorkloadStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Production workload id");
        if (!IsAllowed(expectedStatus, nextStatus))
            throw new InvalidOperationException($"Production workload cannot transition from {expectedStatus} to {nextStatus}.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProductionWorkloads SET Status = $next, ConcurrencyVersion = ConcurrencyVersion + 1
            WHERE Id = $id AND Status = $expected AND ConcurrencyVersion = $version;
            """;
        command.Parameters.AddWithValue("$next", nextStatus.ToString());
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$expected", expectedStatus.ToString());
        command.Parameters.AddWithValue("$version", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "Production workload", id);
        return (await GetWorkloadAsync(connection, id.Trim(), cancellationToken))!;
    }

    public async Task<ProductionWorkloadItem> TransitionWorkloadItemAsync(
        string id,
        ProductionWorkloadItemStatus expectedStatus,
        ProductionWorkloadItemStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Production workload item id");
        if (!IsAllowed(expectedStatus, nextStatus))
            throw new InvalidOperationException($"Production workload item cannot transition from {expectedStatus} to {nextStatus}.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProductionWorkloadItems SET Status = $next, ConcurrencyVersion = ConcurrencyVersion + 1
            WHERE Id = $id AND Status = $expected AND ConcurrencyVersion = $version;
            """;
        command.Parameters.AddWithValue("$next", nextStatus.ToString());
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$expected", expectedStatus.ToString());
        command.Parameters.AddWithValue("$version", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "Production workload item", id);
        return (await GetWorkloadItemAsync(connection, id.Trim(), cancellationToken))!;
    }

    public async Task CreateAttemptAsync(
        ProductionAttempt attempt, CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var hashCheck = connection.CreateCommand())
        {
            hashCheck.Transaction = transaction;
            hashCheck.CommandText = "SELECT ContentHash FROM CompiledMediaRequests WHERE Id = $id;";
            hashCheck.Parameters.AddWithValue("$id", attempt.CompiledRequestId.Trim());
            var persistedHash = await hashCheck.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.Equals(persistedHash, attempt.CompiledRequestHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Production attempt compiled request hash does not match the persisted request.");
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ProductionAttempts
                    (Id, WorkloadItemId, AttemptNumber, Kind, Status, ConcurrencyVersion,
                     CompiledRequestId, CompiledRequestHash, ProviderKey, ProviderRequestId,
                     ProviderStatusUrl, OutputFileRelativePath, OutputSha256, OutputByteLength,
                     PayloadJson, CreatedUtc)
                VALUES ($id, $item, $number, $kind, $status, $version, $request, $hash,
                        NULL, NULL, NULL, NULL, NULL, NULL, $payload, $createdUtc);
                """;
            command.Parameters.AddWithValue("$id", attempt.Id.Trim());
            command.Parameters.AddWithValue("$item", attempt.WorkloadItemId.Trim());
            command.Parameters.AddWithValue("$number", attempt.AttemptNumber);
            command.Parameters.AddWithValue("$kind", attempt.Kind.ToString());
            command.Parameters.AddWithValue("$status", attempt.Status.ToString());
            command.Parameters.AddWithValue("$version", attempt.ConcurrencyVersion);
            command.Parameters.AddWithValue("$request", attempt.CompiledRequestId.Trim());
            command.Parameters.AddWithValue("$hash", attempt.CompiledRequestHash);
            command.Parameters.AddWithValue("$payload", Serialize(attempt));
            command.Parameters.AddWithValue("$createdUtc", FormatUtc(attempt.CreatedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var pointer = connection.CreateCommand())
        {
            pointer.Transaction = transaction;
            pointer.CommandText = "UPDATE ProductionWorkloadItems SET CurrentAttemptId = $attempt WHERE Id = $item;";
            pointer.Parameters.AddWithValue("$attempt", attempt.Id.Trim());
            pointer.Parameters.AddWithValue("$item", attempt.WorkloadItemId.Trim());
            EnsureChanged(await pointer.ExecuteNonQueryAsync(cancellationToken), "Production workload item", attempt.WorkloadItemId);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProductionAttempt?> GetAttemptAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Production attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetAttemptAsync(connection, id.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<ProductionAttempt>> ListAttemptsAsync(
        string workloadItemId, CancellationToken cancellationToken = default)
    {
        Require(workloadItemId, "Production workload item id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{AttemptSelect} WHERE WorkloadItemId = $id ORDER BY AttemptNumber;";
        command.Parameters.AddWithValue("$id", workloadItemId.Trim());
        var results = new List<ProductionAttempt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadAttempt(reader));
        return results;
    }

    public async Task<ProductionAttempt> RecordProviderSubmissionAsync(
        string id,
        string providerKey,
        string providerRequestId,
        string providerStatusUrl,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Production attempt id");
        Require(providerKey, "Provider key");
        Require(providerRequestId, "Provider request id");
        Require(providerStatusUrl, "Provider status URL");
        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetAttemptAsync(connection, id.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Production attempt '{id}' was not found.");
        if (existing.Status == ProductionAttemptStatus.Submitted
            && string.Equals(existing.ProviderKey, providerKey.Trim(), StringComparison.Ordinal)
            && string.Equals(existing.ProviderRequestId, providerRequestId.Trim(), StringComparison.Ordinal)
            && string.Equals(existing.ProviderStatusUrl, providerStatusUrl.Trim(), StringComparison.Ordinal))
            return existing;
        if (existing.Status != ProductionAttemptStatus.Pending)
            throw new InvalidOperationException($"Production attempt '{id}' is {existing.Status} and cannot record a new provider submission.");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProductionAttempts
            SET Status = 'Submitted', ProviderKey = $provider, ProviderRequestId = $requestId,
                ProviderStatusUrl = $statusUrl, SubmittedUtc = $submittedUtc,
                ConcurrencyVersion = ConcurrencyVersion + 1
            WHERE Id = $id AND Status = 'Pending' AND ConcurrencyVersion = $version;
            """;
        command.Parameters.AddWithValue("$provider", providerKey.Trim());
        command.Parameters.AddWithValue("$requestId", providerRequestId.Trim());
        command.Parameters.AddWithValue("$statusUrl", providerStatusUrl.Trim());
        command.Parameters.AddWithValue("$submittedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$version", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "Production attempt", id);
        return (await GetAttemptAsync(connection, id.Trim(), cancellationToken))!;
    }

    public async Task<ProductionAttempt> TransitionAttemptAsync(
        string id,
        ProductionAttemptStatus expectedStatus,
        ProductionAttemptStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Production attempt id");
        if (!IsAllowed(expectedStatus, nextStatus))
            throw new InvalidOperationException($"Production attempt cannot transition from {expectedStatus} to {nextStatus}.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProductionAttempts SET Status = $next, ConcurrencyVersion = ConcurrencyVersion + 1
            WHERE Id = $id AND Status = $expected AND ConcurrencyVersion = $version;
            """;
        command.Parameters.AddWithValue("$next", nextStatus.ToString());
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$expected", expectedStatus.ToString());
        command.Parameters.AddWithValue("$version", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "Production attempt", id);
        return (await GetAttemptAsync(connection, id.Trim(), cancellationToken))!;
    }

    public async Task<ProductionAttempt> RecordAttemptResultAsync(
        string id,
        string outputFileRelativePath,
        string outputSha256,
        long outputByteLength,
        string outputMetadataJson,
        string providerResponseSnapshotJson,
        string costSnapshotJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Production attempt id");
        Require(outputFileRelativePath, "Output file relative path");
        RequireSha256(outputSha256, "Output SHA-256");
        if (outputByteLength <= 0) throw new InvalidOperationException("Output byte length must be positive.");
        RequireJson(outputMetadataJson, "Output metadata");
        RequireSecretFreeJson(providerResponseSnapshotJson, "Provider response snapshot");
        RequireJson(costSnapshotJson, "Cost snapshot");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProductionAttempts
            SET Status = 'Succeeded', OutputFileRelativePath = $path, OutputSha256 = $sha,
                OutputByteLength = $bytes, OutputMetadataJson = $metadata,
                ProviderResponseSnapshotJson = $response, CostSnapshotJson = $cost,
                CompletedUtc = $completedUtc, ConcurrencyVersion = ConcurrencyVersion + 1
            WHERE Id = $id AND Status IN ('Submitted', 'Running') AND ConcurrencyVersion = $version;
            """;
        command.Parameters.AddWithValue("$path", outputFileRelativePath.Trim());
        command.Parameters.AddWithValue("$sha", outputSha256.ToUpperInvariant());
        command.Parameters.AddWithValue("$bytes", outputByteLength);
        command.Parameters.AddWithValue("$metadata", outputMetadataJson.Trim());
        command.Parameters.AddWithValue("$response", providerResponseSnapshotJson.Trim());
        command.Parameters.AddWithValue("$cost", costSnapshotJson.Trim());
        command.Parameters.AddWithValue("$completedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$version", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "Production attempt", id);
        return (await GetAttemptAsync(connection, id.Trim(), cancellationToken))!;
    }

    public async Task<ProductionAttempt> RecordAttemptFailureAsync(
        string id,
        ProductionAttemptStatus terminalStatus,
        string failureCode,
        string failureDiagnostic,
        string providerResponseSnapshotJson,
        string costSnapshotJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Production attempt id");
        if (terminalStatus is not (ProductionAttemptStatus.Failed or ProductionAttemptStatus.Cancelled or ProductionAttemptStatus.Indeterminate))
            throw new InvalidOperationException("Attempt failure status must be Failed, Cancelled, or Indeterminate.");
        Require(failureCode, "Attempt failure code");
        Require(failureDiagnostic, "Attempt failure diagnostic");
        RequireSecretFreeJson(providerResponseSnapshotJson, "Provider response snapshot");
        RequireJson(costSnapshotJson, "Cost snapshot");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProductionAttempts
            SET Status = $status, FailureCode = $code, FailureDiagnostic = $diagnostic,
                ProviderResponseSnapshotJson = $response, CostSnapshotJson = $cost,
                CompletedUtc = $completedUtc, ConcurrencyVersion = ConcurrencyVersion + 1
            WHERE Id = $id AND Status IN ('Pending', 'Submitted', 'Running') AND ConcurrencyVersion = $version;
            """;
        command.Parameters.AddWithValue("$status", terminalStatus.ToString());
        command.Parameters.AddWithValue("$code", failureCode.Trim());
        command.Parameters.AddWithValue("$diagnostic", failureDiagnostic.Trim());
        command.Parameters.AddWithValue("$response", providerResponseSnapshotJson.Trim());
        command.Parameters.AddWithValue("$cost", costSnapshotJson.Trim());
        command.Parameters.AddWithValue("$completedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$version", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "Production attempt", id);
        return (await GetAttemptAsync(connection, id.Trim(), cancellationToken))!;
    }

    public async Task<ProductionAttempt> RecordLateAttemptResultAsync(
        string id,
        string outputFileRelativePath,
        string outputSha256,
        long outputByteLength,
        string outputMetadataJson,
        string providerResponseSnapshotJson,
        string costSnapshotJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Production attempt id");
        Require(outputFileRelativePath, "Output file relative path");
        RequireSha256(outputSha256, "Output SHA-256");
        if (outputByteLength <= 0) throw new InvalidOperationException("Output byte length must be positive.");
        RequireJson(outputMetadataJson, "Output metadata");
        RequireSecretFreeJson(providerResponseSnapshotJson, "Provider response snapshot");
        RequireJson(costSnapshotJson, "Cost snapshot");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProductionAttempts
            SET OutputFileRelativePath = $path, OutputSha256 = $sha, OutputByteLength = $bytes,
                OutputMetadataJson = $metadata, ProviderResponseSnapshotJson = $response,
                CostSnapshotJson = $cost, ConcurrencyVersion = ConcurrencyVersion + 1
            WHERE Id = $id AND Status = 'Indeterminate' AND OutputSha256 IS NULL
                AND ConcurrencyVersion = $version;
            """;
        command.Parameters.AddWithValue("$path", outputFileRelativePath.Trim());
        command.Parameters.AddWithValue("$sha", outputSha256.ToUpperInvariant());
        command.Parameters.AddWithValue("$bytes", outputByteLength);
        command.Parameters.AddWithValue("$metadata", outputMetadataJson.Trim());
        command.Parameters.AddWithValue("$response", providerResponseSnapshotJson.Trim());
        command.Parameters.AddWithValue("$cost", costSnapshotJson.Trim());
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$version", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "Production attempt", id);
        return (await GetAttemptAsync(connection, id.Trim(), cancellationToken))!;
    }

    public async Task<ProductionReviewDecision> AddReviewDecisionAsync(
        ProductionReviewDecision decision, CancellationToken cancellationToken = default)
    {
        ValidateReviewDecision(decision);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var attempt = await GetAttemptAsync(connection, decision.AttemptId.Trim(), cancellationToken, transaction)
            ?? throw new InvalidOperationException($"Production attempt '{decision.AttemptId}' was not found.");
        if (attempt.Status != ProductionAttemptStatus.Succeeded)
            throw new InvalidOperationException("Only a successful production attempt can be reviewed.");
        if (!string.Equals(attempt.WorkloadItemId, decision.WorkloadItemId, StringComparison.Ordinal))
            throw new InvalidOperationException("Review decision workload item does not own the selected attempt.");
        await using (var version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText = "SELECT COALESCE(MAX(Version), 0) + 1 FROM ProductionReviewDecisions WHERE WorkloadItemId = $item;";
            version.Parameters.AddWithValue("$item", decision.WorkloadItemId.Trim());
            decision.Version = Convert.ToInt32(await version.ExecuteScalarAsync(cancellationToken));
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ProductionReviewDecisions
                (Id, WorkloadItemId, AttemptId, Version, Decision, ReasonCode, Notes, DecidedBy, DecidedUtc, PayloadJson)
            VALUES ($id, $item, $attempt, $version, $decision, $reason, $notes, $by, $utc, $payload);
            """;
        command.Parameters.AddWithValue("$id", decision.Id.Trim());
        command.Parameters.AddWithValue("$item", decision.WorkloadItemId.Trim());
        command.Parameters.AddWithValue("$attempt", decision.AttemptId.Trim());
        command.Parameters.AddWithValue("$version", decision.Version);
        command.Parameters.AddWithValue("$decision", decision.Decision.ToString());
        command.Parameters.AddWithValue("$reason", decision.ReasonCode.Trim());
        command.Parameters.AddWithValue("$notes", (object?)decision.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$by", decision.DecidedBy.Trim());
        command.Parameters.AddWithValue("$utc", FormatUtc(decision.DecidedUtc));
        command.Parameters.AddWithValue("$payload", Serialize(decision));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return decision;
    }

    public async Task<IReadOnlyList<ProductionReviewDecision>> ListReviewDecisionsAsync(
        string workloadItemId, CancellationToken cancellationToken = default)
    {
        Require(workloadItemId, "Production workload item id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM ProductionReviewDecisions WHERE WorkloadItemId = $id ORDER BY Version;";
        command.Parameters.AddWithValue("$id", workloadItemId.Trim());
        return await ReadPayloadsAsync<ProductionReviewDecision>(command, cancellationToken);
    }

    public async Task CreateDerivativeAsync(
        ProductionDerivative derivative, CancellationToken cancellationToken = default)
    {
        ValidateDerivative(derivative);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var attempt = await GetAttemptAsync(connection, derivative.AttemptId.Trim(), cancellationToken, transaction)
            ?? throw new InvalidOperationException($"Production attempt '{derivative.AttemptId}' was not found.");
        if (attempt.Status != ProductionAttemptStatus.Succeeded || string.IsNullOrWhiteSpace(attempt.OutputSha256))
            throw new InvalidOperationException("An approved derivative requires a successful attempt with owned output bytes.");
        await RequireApprovedReviewAsync(connection, transaction, derivative, cancellationToken);
        await RequireDerivativeAssetAsync(connection, transaction, derivative.SceneAssetId, attempt.OutputSha256, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ProductionDerivatives
                (Id, WorkloadItemId, AttemptId, ReviewDecisionId, SceneAssetId, CapabilityProfileId,
                 Status, UseScopeKey, PayloadJson, ApprovedUtc)
            VALUES ($id, $item, $attempt, $review, $asset, $profile, $status, $scope, $payload, $approvedUtc);
            """;
        command.Parameters.AddWithValue("$id", derivative.Id.Trim());
        command.Parameters.AddWithValue("$item", derivative.WorkloadItemId.Trim());
        command.Parameters.AddWithValue("$attempt", derivative.AttemptId.Trim());
        command.Parameters.AddWithValue("$review", derivative.ReviewDecisionId.Trim());
        command.Parameters.AddWithValue("$asset", derivative.SceneAssetId.Trim());
        command.Parameters.AddWithValue("$profile", derivative.CapabilityProfileId.Trim());
        command.Parameters.AddWithValue("$status", derivative.Status.ToString());
        command.Parameters.AddWithValue("$scope", derivative.UseScopeKey.Trim());
        command.Parameters.AddWithValue("$payload", Serialize(derivative));
        command.Parameters.AddWithValue("$approvedUtc", FormatUtc(derivative.ApprovedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProductionDerivative?> GetDerivativeAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Production derivative id");
        return await GetPayloadAsync<ProductionDerivative>("ProductionDerivatives", id, cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await MigrateProductionContextSchemaAsync(connection, cancellationToken);
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
        await using var schema = connection.CreateCommand();
        schema.CommandText = SchemaSql;
        await schema.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "ProductionWorkloadItems", "FailureCode", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "ProductionAttempts", "FailureCode", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "ProductionAttempts", "FailureDiagnostic", "TEXT NULL", cancellationToken);
        return connection;
    }

    private static async Task MigrateProductionContextSchemaAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        var migrateIntents = await TableExistsAsync(connection, "ProductionIntentSnapshots", cancellationToken)
            && !await ColumnExistsAsync(connection, "ProductionIntentSnapshots", "ContextKind", cancellationToken);
        var migrateWorkloads = await TableExistsAsync(connection, "ProductionWorkloads", cancellationToken)
            && !await ColumnExistsAsync(connection, "ProductionWorkloads", "ContextKind", cancellationToken);
        if (!migrateIntents && !migrateWorkloads) return;

        await using (var disable = connection.CreateCommand())
        {
            disable.CommandText = "PRAGMA foreign_keys = OFF;";
            await disable.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (migrateIntents)
        {
            await using var migrate = connection.CreateCommand();
            migrate.Transaction = (SqliteTransaction)transaction;
            migrate.CommandText = """
                CREATE TABLE ProductionIntentSnapshots_ContextMigration (
                    Id TEXT PRIMARY KEY, ContextKind TEXT NOT NULL, ContextId TEXT NOT NULL,
                    ContextSnapshotJson TEXT NOT NULL, ProductionGroupId TEXT NULL, SessionId TEXT NULL,
                    CatalogueId TEXT NULL, BeatId TEXT NULL, BeatProductionPlanId TEXT NULL,
                    BeatProductionPlanVersion INTEGER NULL CHECK (BeatProductionPlanVersion > 0),
                    MomentSetId TEXT NULL, MomentSetVersion INTEGER NULL CHECK (MomentSetVersion > 0),
                    MomentId TEXT NULL, MomentEnrichmentId TEXT NULL,
                    MomentEnrichmentRevision INTEGER NULL CHECK (MomentEnrichmentRevision > 0),
                    Pov TEXT NOT NULL, Operation TEXT NOT NULL, ContentHash TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
                    FOREIGN KEY (ProductionGroupId) REFERENCES SceneImageProductionGroups(Id) ON DELETE RESTRICT
                );
                INSERT INTO ProductionIntentSnapshots_ContextMigration
                    (Id, ContextKind, ContextId, ContextSnapshotJson, ProductionGroupId, SessionId,
                     CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                     MomentSetId, MomentSetVersion, MomentId, MomentEnrichmentId,
                     MomentEnrichmentRevision, Pov, Operation, ContentHash, PayloadJson, CreatedUtc)
                SELECT Id, 'SceneMoment', SessionId, '{}', ProductionGroupId, SessionId,
                       CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                       MomentSetId, MomentSetVersion, MomentId, MomentEnrichmentId,
                       MomentEnrichmentRevision, Pov, Operation, ContentHash,
                       json_set(PayloadJson,
                           '$.contextKind', 1,
                           '$.contextId', SessionId,
                           '$.contextSnapshotJson', '{}'),
                       CreatedUtc
                FROM ProductionIntentSnapshots;
                DROP TABLE ProductionIntentSnapshots;
                ALTER TABLE ProductionIntentSnapshots_ContextMigration RENAME TO ProductionIntentSnapshots;
                """;
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }
        if (migrateWorkloads)
        {
            await using var migrate = connection.CreateCommand();
            migrate.Transaction = (SqliteTransaction)transaction;
            migrate.CommandText = """
                CREATE TABLE ProductionWorkloads_ContextMigration (
                    Id TEXT PRIMARY KEY, ContextKind TEXT NOT NULL, ContextId TEXT NOT NULL,
                    ContextSnapshotJson TEXT NOT NULL, SessionId TEXT NULL,
                    Revision INTEGER NOT NULL CHECK (Revision > 0), Status TEXT NOT NULL,
                    ConcurrencyVersion INTEGER NOT NULL CHECK (ConcurrencyVersion > 0),
                    Goal TEXT NOT NULL, ContentPolicyKey TEXT NOT NULL,
                    ItemCount INTEGER NOT NULL CHECK (ItemCount > 0),
                    OutputCount INTEGER NOT NULL CHECK (OutputCount > 0),
                    CompatibilityGroupCount INTEGER NOT NULL CHECK (CompatibilityGroupCount > 0),
                    PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
                    UNIQUE (ContextKind, ContextId, Revision)
                );
                INSERT INTO ProductionWorkloads_ContextMigration
                    (Id, ContextKind, ContextId, ContextSnapshotJson, SessionId, Revision, Status,
                     ConcurrencyVersion, Goal, ContentPolicyKey, ItemCount, OutputCount,
                     CompatibilityGroupCount, PayloadJson, CreatedUtc)
                SELECT Id, 'SceneMoment', SessionId, '{}', SessionId, Revision, Status,
                       ConcurrencyVersion, Goal, ContentPolicyKey, ItemCount, OutputCount,
                       CompatibilityGroupCount,
                       json_set(PayloadJson,
                           '$.contextKind', 1,
                           '$.contextId', SessionId,
                           '$.contextSnapshotJson', '{}'),
                       CreatedUtc
                FROM ProductionWorkloads;
                DROP TABLE ProductionWorkloads;
                ALTER TABLE ProductionWorkloads_ContextMigration RENAME TO ProductionWorkloads;
                """;
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await using var enable = connection.CreateCommand();
        enable.CommandText = "PRAGMA foreign_keys = ON;";
        await enable.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        }
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<T?> GetPayloadAsync<T>(string table, string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT PayloadJson FROM {table} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Trim());
        return DeserializeOrNull<T>(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertReferenceBindingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string requestId,
        OrderedMediaReferenceBinding binding,
        CancellationToken cancellationToken)
    {
        ValidateBinding(binding, requestId);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO OrderedMediaReferenceBindings
                (Id, CompiledRequestId, Ordinal, SemanticRole, ActorKey, SceneAssetId,
                 SceneAssetVersion, SceneAssetSha256, IdentityVersionId, BodyProfileVersionId,
                 WardrobeLookVersionId, RegionAssetId, BindingSnapshotJson, PayloadJson, CreatedUtc)
            VALUES ($id, $request, $ordinal, $role, $actor, $asset, $assetVersion, $sha,
                    $identity, $body, $wardrobe, $region, $snapshot, $payload, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", binding.Id.Trim());
        command.Parameters.AddWithValue("$request", requestId.Trim());
        command.Parameters.AddWithValue("$ordinal", binding.Ordinal);
        command.Parameters.AddWithValue("$role", binding.SemanticRole.Trim());
        command.Parameters.AddWithValue("$actor", (object?)binding.ActorKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$asset", binding.SceneAssetId.Trim());
        command.Parameters.AddWithValue("$assetVersion", binding.SceneAssetVersion);
        command.Parameters.AddWithValue("$sha", binding.SceneAssetSha256.ToUpperInvariant());
        command.Parameters.AddWithValue("$identity", (object?)binding.IdentityVersionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$body", (object?)binding.BodyProfileVersionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$wardrobe", (object?)binding.WardrobeLookVersionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$region", (object?)binding.RegionAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$snapshot", binding.BindingSnapshotJson.Trim());
        command.Parameters.AddWithValue("$payload", Serialize(binding));
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(binding.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertWorkloadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProductionWorkload workload,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ProductionWorkloads
                (Id, ContextKind, ContextId, ContextSnapshotJson, SessionId,
                 Revision, Status, ConcurrencyVersion, Goal, ContentPolicyKey,
                 ItemCount, OutputCount, CompatibilityGroupCount, PayloadJson, CreatedUtc)
            VALUES ($id, $contextKind, $contextId, $contextSnapshot, $session,
                    $revision, $status, $version, $goal, $policy,
                    $items, $outputs, $groups, $payload, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", workload.Id.Trim());
        command.Parameters.AddWithValue("$contextKind", workload.ContextKind.ToString());
        command.Parameters.AddWithValue("$contextId", workload.ContextId.Trim());
        command.Parameters.AddWithValue("$contextSnapshot", workload.ContextSnapshotJson.Trim());
        command.Parameters.AddWithValue("$session", DbText(workload.SessionId));
        command.Parameters.AddWithValue("$revision", workload.Revision);
        command.Parameters.AddWithValue("$status", workload.Status.ToString());
        command.Parameters.AddWithValue("$version", workload.ConcurrencyVersion);
        command.Parameters.AddWithValue("$goal", workload.Goal.Trim());
        command.Parameters.AddWithValue("$policy", workload.ContentPolicyKey.Trim());
        command.Parameters.AddWithValue("$items", workload.ItemCount);
        command.Parameters.AddWithValue("$outputs", workload.OutputCount);
        command.Parameters.AddWithValue("$groups", workload.CompatibilityGroupCount);
        command.Parameters.AddWithValue("$payload", Serialize(workload));
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(workload.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertWorkloadItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workloadId,
        ProductionWorkloadItem item,
        CancellationToken cancellationToken)
    {
        ValidateWorkloadItem(item, workloadId);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ProductionWorkloadItems
                (Id, WorkloadId, Ordinal, IntentSnapshotId, CompiledRequestId, CompatibilityKey,
                 VariationCount, Status, ConcurrencyVersion, CurrentAttemptId, PayloadJson, CreatedUtc)
            VALUES ($id, $workload, $ordinal, $intent, $request, $key, $variations,
                    $status, $version, NULL, $payload, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", item.Id.Trim());
        command.Parameters.AddWithValue("$workload", workloadId.Trim());
        command.Parameters.AddWithValue("$ordinal", item.Ordinal);
        command.Parameters.AddWithValue("$intent", item.IntentSnapshotId.Trim());
        command.Parameters.AddWithValue("$request", item.CompiledRequestId.Trim());
        command.Parameters.AddWithValue("$key", item.CompatibilityKey.Trim());
        command.Parameters.AddWithValue("$variations", item.VariationCount);
        command.Parameters.AddWithValue("$status", item.Status.ToString());
        command.Parameters.AddWithValue("$version", item.ConcurrencyVersion);
        command.Parameters.AddWithValue("$payload", Serialize(item));
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(item.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ProductionWorkload?> GetWorkloadAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson, Status, ConcurrencyVersion FROM ProductionWorkloads WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var workload = Deserialize<ProductionWorkload>(reader.GetString(0));
        workload.Status = ParseEnum<ProductionWorkloadStatus>(reader.GetString(1), "workload", id);
        workload.ConcurrencyVersion = reader.GetInt64(2);
        return workload;
    }

    private static async Task<ProductionWorkloadItem?> GetWorkloadItemAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson, Status, ConcurrencyVersion, CurrentAttemptId, FailureCode FROM ProductionWorkloadItems WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadWorkloadItem(reader) : null;
    }

    private static ProductionWorkloadItem ReadWorkloadItem(SqliteDataReader reader)
    {
        var item = Deserialize<ProductionWorkloadItem>(reader.GetString(0));
        item.Status = ParseEnum<ProductionWorkloadItemStatus>(reader.GetString(1), "workload item", item.Id);
        item.ConcurrencyVersion = reader.GetInt64(2);
        item.CurrentAttemptId = reader.IsDBNull(3) ? null : reader.GetString(3);
        item.FailureCode = reader.IsDBNull(4) ? null : reader.GetString(4);
        return item;
    }

    private const string AttemptSelect = """
        SELECT PayloadJson, Status, ConcurrencyVersion, ProviderKey, ProviderRequestId, ProviderStatusUrl,
               ProviderResponseSnapshotJson, OutputFileRelativePath, OutputSha256, OutputByteLength,
               OutputMetadataJson, CostSnapshotJson, CompletedUtc, SubmittedUtc, FailureCode, FailureDiagnostic
        FROM ProductionAttempts
        """;

    private static async Task<ProductionAttempt?> GetAttemptAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"{AttemptSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    private static ProductionAttempt ReadAttempt(SqliteDataReader reader)
    {
        var attempt = Deserialize<ProductionAttempt>(reader.GetString(0));
        attempt.Status = ParseEnum<ProductionAttemptStatus>(reader.GetString(1), "attempt", attempt.Id);
        attempt.ConcurrencyVersion = reader.GetInt64(2);
        attempt.ProviderKey = reader.IsDBNull(3) ? null : reader.GetString(3);
        attempt.ProviderRequestId = reader.IsDBNull(4) ? null : reader.GetString(4);
        attempt.ProviderStatusUrl = reader.IsDBNull(5) ? null : reader.GetString(5);
        attempt.ProviderResponseSnapshotJson = reader.IsDBNull(6) ? null : reader.GetString(6);
        attempt.OutputFileRelativePath = reader.IsDBNull(7) ? null : reader.GetString(7);
        attempt.OutputSha256 = reader.IsDBNull(8) ? null : reader.GetString(8);
        attempt.OutputByteLength = reader.IsDBNull(9) ? null : reader.GetInt64(9);
        attempt.OutputMetadataJson = reader.IsDBNull(10) ? null : reader.GetString(10);
        attempt.CostSnapshotJson = reader.IsDBNull(11) ? null : reader.GetString(11);
        attempt.CompletedUtc = reader.IsDBNull(12) ? null : ParseUtc(reader.GetString(12));
        attempt.SubmittedUtc = reader.IsDBNull(13) ? null : ParseUtc(reader.GetString(13));
        attempt.FailureCode = reader.IsDBNull(14) ? null : reader.GetString(14);
        attempt.FailureDiagnostic = reader.IsDBNull(15) ? null : reader.GetString(15);
        return attempt;
    }

    private static async Task RequireApprovedReviewAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProductionDerivative derivative,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Decision, AttemptId, WorkloadItemId FROM ProductionReviewDecisions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", derivative.ReviewDecisionId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !string.Equals(reader.GetString(0), ProductionReviewDecisionValue.Approved.ToString(), StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), derivative.AttemptId, StringComparison.Ordinal)
            || !string.Equals(reader.GetString(2), derivative.WorkloadItemId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Production derivative requires an approved review decision for the exact attempt and workload item.");
        }
    }

    private static async Task RequireDerivativeAssetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string assetId,
        string outputSha256,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Sha256, ProductionApprovalStatus FROM SceneAssets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", assetId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !string.Equals(reader.GetString(0), outputSha256, StringComparison.Ordinal)
            || reader.IsDBNull(1)
            || !string.Equals(reader.GetString(1), SceneAssetProductionApprovalStatus.Approved.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Production derivative requires an approved shared Scene Asset with the exact attempt checksum.");
        }
    }

    private static void ValidateCapabilityProfile(MediaCapabilityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Require(profile.Id, "Capability profile id");
        Require(profile.ProviderKey, "Capability provider key");
        Require(profile.ModelId, "Capability model id");
        Require(profile.ModelVersion, "Capability model version");
        RequireEnum(profile.Operation, "Capability operation");
        Require(profile.CompilerId, "Capability compiler id");
        Require(profile.CompilerVersion, "Capability compiler version");
        Require(profile.WorkflowRevision, "Capability workflow revision");
        Require(profile.NodeRevision, "Capability node revision");
        RequireJson(profile.ArtifactManifestJson, "Capability artifact manifest");
        RequireJson(profile.SettingsSchemaJson, "Capability settings schema");
        RequireJson(profile.ReferenceLayoutJson, "Capability reference layout");
        RequireJson(profile.ControlLayoutJson, "Capability control layout");
        ParseIdentityStrategies(profile.SupportedIdentityStrategiesJson);
        Require(profile.ContentPolicyKey, "Capability content policy key");
        RequireEnum(profile.Status, "Capability profile status");
        Require(profile.EvidenceRunId, "Capability evidence run id");
        RequireUtc(profile.CreatedUtc, "Capability creation time");
    }

    private static void ValidateCapabilityCell(MediaCapabilityCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        Require(cell.Id, "Capability cell id");
        Require(cell.CapabilityProfileId, "Capability profile id");
        if (cell.ActorCount <= 0) throw new InvalidOperationException("Capability cell actor count must be positive.");
        Require(cell.FaceAngleKey, "Capability face angle key");
        Require(cell.CropKey, "Capability crop key");
        Require(cell.PoseClassKey, "Capability pose class key");
        Require(cell.CompositionClassKey, "Capability composition class key");
        RequireJson(cell.ReferenceControlTupleJson, "Capability reference/control tuple");
        if (cell.IdentityStrategyKind is { } strategy && !Enum.IsDefined(strategy))
            throw new InvalidOperationException("Capability cell identity strategy is unknown.");
        RequireEnum(cell.Status, "Capability cell status");
        Require(cell.EvidenceRunId, "Capability cell evidence run id");
        if (cell.Status == MediaCapabilityCellStatus.Rejected && string.IsNullOrWhiteSpace(cell.FailureReason))
            throw new InvalidOperationException("A rejected capability cell requires a failure reason.");
        RequireUtc(cell.CreatedUtc, "Capability cell creation time");
    }

    private static HashSet<CharacterIdentityStrategyKind> ParseIdentityStrategies(string json)
    {
        RequireJson(json, "Capability supported identity strategies");
        var values = JsonSerializer.Deserialize<string[]>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Capability supported identity strategies must be a JSON array.");
        var strategies = new HashSet<CharacterIdentityStrategyKind>();
        foreach (var value in values)
        {
            if (!Enum.TryParse<CharacterIdentityStrategyKind>(value, true, out var strategy)
                || !Enum.IsDefined(strategy))
                throw new InvalidOperationException($"Capability identity strategy '{value}' is unknown.");
            if (!strategies.Add(strategy))
                throw new InvalidOperationException($"Capability identity strategy '{strategy}' is duplicated.");
        }
        return strategies;
    }

    private static void ValidateIntent(ProductionIntentSnapshot intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        Require(intent.Id, "Production intent id");
        RequireEnum(intent.ContextKind, "Production context kind");
        Require(intent.ContextId, "Production context id");
        RequireJson(intent.ContextSnapshotJson, "Production context snapshot");
        if (intent.ContextKind == ProductionContextKind.SceneMoment)
        {
            foreach (var (value, label) in new[]
            {
                (intent.ProductionGroupId, "Production group id"), (intent.SessionId, "Session id"),
                (intent.CatalogueId, "Catalogue id"), (intent.BeatId, "Beat id"),
                (intent.BeatProductionPlanId, "Beat production plan id"), (intent.MomentSetId, "Moment set id"),
                (intent.MomentId, "Moment id"), (intent.MomentEnrichmentId, "Moment enrichment id")
            }) Require(value, label);
            if (!string.Equals(intent.ContextId, intent.SessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Scene production context id must equal the exact session id.");
            if (intent.BeatProductionPlanVersion <= 0 || intent.MomentSetVersion <= 0 || intent.MomentEnrichmentRevision <= 0)
                throw new InvalidOperationException("Scene production intent lineage versions must be positive.");
        }
        else
        {
            if (new[] { intent.ProductionGroupId, intent.SessionId, intent.CatalogueId, intent.BeatId,
                    intent.BeatProductionPlanId, intent.MomentSetId, intent.MomentId, intent.MomentEnrichmentId }
                .Any(value => !string.IsNullOrWhiteSpace(value))
                || intent.BeatProductionPlanVersion != 0 || intent.MomentSetVersion != 0
                || intent.MomentEnrichmentRevision != 0)
                throw new InvalidOperationException("Character Asset production intent cannot contain scene lineage.");
            RequireCharacterAssetContext(intent.ContextSnapshotJson);
        }
        Require(intent.Pov, "Production POV");
        RequireEnum(intent.Operation, "Production intent operation");
        foreach (var (json, label) in new[]
        {
            (intent.VisibleActorsJson, "Visible actors"), (intent.CompositionIntentJson, "Composition intent"),
            (intent.CameraIntentJson, "Camera intent"), (intent.StyleIntentJson, "Style intent"),
            (intent.PreservationConstraintsJson, "Preservation constraints"), (intent.ChangeIntentJson, "Change intent"),
            (intent.ContentPolicyJson, "Content policy")
        }) RequireJson(json, label);
        RequireSha256(intent.ContentHash, "Production intent content hash");
        var expected = ProductionContentHash.ForIntent(intent);
        if (!string.Equals(expected, intent.ContentHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Production intent content hash does not match its immutable semantic fields.");
        RequireUtc(intent.CreatedUtc, "Production intent creation time");
    }

    private static void ValidateCompiledRequest(
        CompiledMediaRequest request, IReadOnlyList<OrderedMediaReferenceBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bindings);
        foreach (var (value, label) in new[]
        {
            (request.Id, "Compiled request id"), (request.IntentSnapshotId, "Intent snapshot id"),
            (request.CapabilityProfileId, "Capability profile id"), (request.CapabilityCellId, "Capability cell id"),
            (request.CompilerId, "Compiler id"), (request.CompilerVersion, "Compiler version"),
            (request.RequestSchemaVersion, "Request schema version"), (request.ProviderKey, "Provider key"),
            (request.ModelId, "Model id"), (request.ModelVersion, "Model version"),
            (request.WorkflowRevision, "Workflow revision")
        }) Require(value, label);
        RequireSecretFreeJson(request.CanonicalProviderRequestJson, "Canonical provider request");
        RequireJson(request.ValidationResultJson, "Compilation validation result");
        RequireSecretFreeJson(request.IdentityStrategySnapshotJson, "Identity strategy snapshot");
        RequireSha256(request.ContentHash, "Compiled request content hash");
        var expected = ProductionContentHash.ForCompiledRequest(request, bindings);
        if (!string.Equals(expected, request.ContentHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Compiled request content hash does not match its immutable request and bindings.");
        if (bindings.Select(binding => binding.Ordinal).Distinct().Count() != bindings.Count)
            throw new InvalidOperationException("Compiled request reference binding ordinals must be unique.");
        RequireUtc(request.CreatedUtc, "Compiled request creation time");
    }

    private static void ValidateBinding(OrderedMediaReferenceBinding binding, string requestId)
    {
        if (!string.Equals(binding.CompiledRequestId, requestId, StringComparison.Ordinal))
            throw new InvalidOperationException("Reference binding does not belong to the compiled request.");
        Require(binding.Id, "Reference binding id");
        if (binding.Ordinal < 0) throw new InvalidOperationException("Reference binding ordinal cannot be negative.");
        Require(binding.SemanticRole, "Reference binding semantic role");
        Require(binding.SceneAssetId, "Reference binding Scene Asset id");
        if (binding.SceneAssetVersion <= 0) throw new InvalidOperationException("Reference binding Scene Asset version must be positive.");
        RequireSha256(binding.SceneAssetSha256, "Reference binding Scene Asset SHA-256");
        RequireJson(binding.BindingSnapshotJson, "Reference binding snapshot");
        RequireUtc(binding.CreatedUtc, "Reference binding creation time");
    }

    private static void ValidateWorkload(ProductionWorkload workload, IReadOnlyList<ProductionWorkloadItem> items)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(items);
        Require(workload.Id, "Production workload id");
        RequireEnum(workload.ContextKind, "Production workload context kind");
        Require(workload.ContextId, "Production workload context id");
        RequireJson(workload.ContextSnapshotJson, "Production workload context snapshot");
        if (workload.ContextKind == ProductionContextKind.SceneMoment)
        {
            Require(workload.SessionId, "Production workload session id");
            if (!string.Equals(workload.ContextId, workload.SessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Scene workload context id must equal the exact session id.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(workload.SessionId))
                throw new InvalidOperationException("Character Asset production workload cannot contain a scene session id.");
            RequireCharacterAssetContext(workload.ContextSnapshotJson);
        }
        if (workload.Revision <= 0) throw new InvalidOperationException("Production workload revision must be positive.");
        if (workload.Status != ProductionWorkloadStatus.Draft || workload.ConcurrencyVersion != 1)
            throw new InvalidOperationException("A new production workload must be Draft at concurrency version 1.");
        Require(workload.Goal, "Production workload goal");
        Require(workload.ContentPolicyKey, "Production workload content policy key");
        RequireJson(workload.SourceVersionSnapshotJson, "Production workload source versions");
        RequireJson(workload.ReadinessSnapshotJson, "Production workload readiness");
        RequireJson(workload.EndpointReadinessJson, "Production workload endpoint readiness");
        RequireJson(workload.CostEstimateJson, "Production workload cost estimate");
        if (workload.ItemCount != items.Count || workload.ItemCount <= 0)
            throw new InvalidOperationException("Production workload item count must match a non-empty item list.");
        if (workload.OutputCount <= 0 || workload.CompatibilityGroupCount <= 0)
            throw new InvalidOperationException("Production workload output and compatibility group counts must be positive.");
        if (items.Select(item => item.Ordinal).Distinct().Count() != items.Count)
            throw new InvalidOperationException("Production workload item ordinals must be unique.");
        RequireUtc(workload.CreatedUtc, "Production workload creation time");
    }

    private static void ValidateWorkloadItem(ProductionWorkloadItem item, string workloadId)
    {
        if (!string.Equals(item.WorkloadId, workloadId, StringComparison.Ordinal))
            throw new InvalidOperationException("Production workload item does not belong to the workload.");
        Require(item.Id, "Production workload item id");
        if (item.Ordinal < 0) throw new InvalidOperationException("Production workload item ordinal cannot be negative.");
        Require(item.IntentSnapshotId, "Production workload item intent id");
        Require(item.CompiledRequestId, "Production workload item compiled request id");
        Require(item.CompatibilityKey, "Production workload item compatibility key");
        if (item.VariationCount <= 0) throw new InvalidOperationException("Production workload item variation count must be positive.");
        if (item.Status != ProductionWorkloadItemStatus.Draft || item.ConcurrencyVersion != 1)
            throw new InvalidOperationException("A new production workload item must be Draft at concurrency version 1.");
        RequireJson(item.RetryPolicySnapshotJson, "Production workload item retry policy");
        RequireSecretFreeJson(item.EndpointSnapshotJson, "Production workload item endpoint snapshot");
        RequireJson(item.DispatchPolicySnapshotJson, "Production workload item dispatch policy");
        RequireJson(item.CostBasisSnapshotJson, "Production workload item cost basis");
        RequireUtc(item.CreatedUtc, "Production workload item creation time");
    }

    private static void ValidateAttempt(ProductionAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        Require(attempt.Id, "Production attempt id");
        Require(attempt.WorkloadItemId, "Production attempt workload item id");
        if (attempt.AttemptNumber <= 0) throw new InvalidOperationException("Production attempt number must be positive.");
        RequireEnum(attempt.Kind, "Production attempt kind");
        if (attempt.Status != ProductionAttemptStatus.Pending || attempt.ConcurrencyVersion != 1)
            throw new InvalidOperationException("A new production attempt must be Pending at concurrency version 1.");
        Require(attempt.CompiledRequestId, "Production attempt compiled request id");
        RequireSha256(attempt.CompiledRequestHash, "Production attempt compiled request hash");
        RequireSecretFreeJson(attempt.RequestSnapshotJson, "Production attempt request snapshot");
        RequireJson(attempt.ReferenceSnapshotJson, "Production attempt reference snapshot");
        RequireJson(attempt.ModelWorkflowSnapshotJson, "Production attempt model/workflow snapshot");
        RequireJson(attempt.SettingsSnapshotJson, "Production attempt settings snapshot");
        RequireUtc(attempt.CreatedUtc, "Production attempt creation time");
    }

    private static void ValidateReviewDecision(ProductionReviewDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        Require(decision.Id, "Review decision id");
        Require(decision.WorkloadItemId, "Review decision workload item id");
        Require(decision.AttemptId, "Review decision attempt id");
        RequireEnum(decision.Decision, "Review decision");
        Require(decision.ReasonCode, "Review reason code");
        Require(decision.DecidedBy, "Review decision actor");
        RequireUtc(decision.DecidedUtc, "Review decision time");
    }

    private static void ValidateDerivative(ProductionDerivative derivative)
    {
        ArgumentNullException.ThrowIfNull(derivative);
        Require(derivative.Id, "Production derivative id");
        Require(derivative.WorkloadItemId, "Production derivative workload item id");
        Require(derivative.AttemptId, "Production derivative attempt id");
        Require(derivative.ReviewDecisionId, "Production derivative review decision id");
        Require(derivative.SceneAssetId, "Production derivative Scene Asset id");
        Require(derivative.CapabilityProfileId, "Production derivative capability profile id");
        RequireJson(derivative.SourceLineageJson, "Production derivative source lineage");
        Require(derivative.UseScopeKey, "Production derivative use scope");
        if (derivative.Status != ProductionDerivativeStatus.Approved)
            throw new InvalidOperationException("A new production derivative must be Approved.");
        Require(derivative.ApprovedBy, "Production derivative approver");
        RequireUtc(derivative.ApprovedUtc, "Production derivative approval time");
    }

    private static bool IsAllowed(ProductionWorkloadStatus current, ProductionWorkloadStatus next) =>
        (current, next) switch
        {
            (ProductionWorkloadStatus.Draft, ProductionWorkloadStatus.Validating or ProductionWorkloadStatus.Cancelled) => true,
            (ProductionWorkloadStatus.Validating, ProductionWorkloadStatus.Ready or ProductionWorkloadStatus.Blocked) => true,
            (ProductionWorkloadStatus.Blocked, ProductionWorkloadStatus.Validating or ProductionWorkloadStatus.Cancelled) => true,
            (ProductionWorkloadStatus.Ready, ProductionWorkloadStatus.Queued or ProductionWorkloadStatus.Cancelled) => true,
            (ProductionWorkloadStatus.Queued, ProductionWorkloadStatus.Running or ProductionWorkloadStatus.Cancelled) => true,
            (ProductionWorkloadStatus.Running, ProductionWorkloadStatus.PartiallyComplete or ProductionWorkloadStatus.Complete or ProductionWorkloadStatus.Failed or ProductionWorkloadStatus.Cancelled) => true,
            (ProductionWorkloadStatus.PartiallyComplete, ProductionWorkloadStatus.Running or ProductionWorkloadStatus.Complete or ProductionWorkloadStatus.Failed or ProductionWorkloadStatus.Cancelled) => true,
            _ => false
        };

    private static bool IsAllowed(ProductionWorkloadItemStatus current, ProductionWorkloadItemStatus next) =>
        (current, next) switch
        {
            (ProductionWorkloadItemStatus.Draft, ProductionWorkloadItemStatus.Ready or ProductionWorkloadItemStatus.Cancelled) => true,
            (ProductionWorkloadItemStatus.Ready, ProductionWorkloadItemStatus.Queued or ProductionWorkloadItemStatus.Cancelled) => true,
            (ProductionWorkloadItemStatus.Queued, ProductionWorkloadItemStatus.Submitted or ProductionWorkloadItemStatus.Cancelled) => true,
            (ProductionWorkloadItemStatus.Submitted, ProductionWorkloadItemStatus.Running or ProductionWorkloadItemStatus.Failed or ProductionWorkloadItemStatus.Cancelled) => true,
            (ProductionWorkloadItemStatus.Running, ProductionWorkloadItemStatus.Reviewable or ProductionWorkloadItemStatus.Failed or ProductionWorkloadItemStatus.Cancelled) => true,
            (ProductionWorkloadItemStatus.Reviewable, ProductionWorkloadItemStatus.Approved or ProductionWorkloadItemStatus.Rejected) => true,
            _ => false
        };

    private static bool IsAllowed(ProductionAttemptStatus current, ProductionAttemptStatus next) =>
        (current, next) switch
        {
            (ProductionAttemptStatus.Pending, ProductionAttemptStatus.Cancelled) => true,
            (ProductionAttemptStatus.Submitted, ProductionAttemptStatus.Running or ProductionAttemptStatus.Failed or ProductionAttemptStatus.Cancelled or ProductionAttemptStatus.Indeterminate) => true,
            (ProductionAttemptStatus.Running, ProductionAttemptStatus.Failed or ProductionAttemptStatus.Cancelled or ProductionAttemptStatus.Indeterminate) => true,
            _ => false
        };

    private static void EnsureChanged(int count, string label, string id)
    {
        if (count != 1)
            throw new InvalidOperationException($"{label} '{id}' state or concurrency version changed before the operation completed.");
    }

    private static void RequireSecretFreeJson(string value, string label)
    {
        RequireJson(value, label);
        using var document = JsonDocument.Parse(value);
        InspectForSecrets(document.RootElement, label);
    }

    private static void InspectForSecrets(JsonElement element, string label)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("apiKey", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("authorization", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("accessToken", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("secret", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"{label} cannot contain secret field '{property.Name}'.");
                }
                InspectForSecrets(property.Value, label);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) InspectForSecrets(item, label);
        }
    }

    private static async Task<IReadOnlyList<T>> ReadPayloadsAsync<T>(
        SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(Deserialize<T>(reader.GetString(0)));
        return results;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions)
        ?? throw new InvalidOperationException($"Persisted {typeof(T).Name} payload was null.");

    private static T? DeserializeOrNull<T>(object? value) => value is string json ? Deserialize<T>(json) : default;

    private static TEnum ParseEnum<TEnum>(string value, string label, string id) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new InvalidOperationException($"Invalid {typeof(TEnum).Name} '{value}' for {label} '{id}'.");
    }

    private static void RequireJson(string value, string label)
    {
        Require(value, label);
        try { using var _ = JsonDocument.Parse(value); }
        catch (JsonException exception) { throw new InvalidOperationException($"{label} must be valid JSON.", exception); }
    }

    private static void RequireCharacterAssetContext(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Character Asset production context must be a JSON object.");
        foreach (var propertyName in new[] { "datasetId", "characterProfileId", "identityPackId", "candidateKind", "coverageKey", "assetName", "assetType" })
        {
            if (!document.RootElement.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidOperationException($"Character Asset production context requires explicit '{propertyName}'.");
        }
    }

    private static object DbText(string value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object DbPositive(int value) => value > 0 ? value : DBNull.Value;

    private static void RequireSha256(string value, string label)
    {
        Require(value, label);
        if (value.Length != 64 || !value.All(character => char.IsAsciiHexDigit(character)))
            throw new InvalidOperationException($"{label} must be a 64-character hexadecimal value.");
    }

    private static void RequireEnum<TEnum>(TEnum value, string label) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new InvalidOperationException($"{label} must be explicit.");
    }

    private static void RequireUtc(DateTime value, string label)
    {
        if (value.Kind != DateTimeKind.Utc) throw new InvalidOperationException($"{label} must be UTC.");
    }

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static string FormatUtc(DateTime value)
    {
        RequireUtc(value, "Persistence timestamp");
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS MediaCapabilityProfiles (
            Id TEXT PRIMARY KEY, ProviderKey TEXT NOT NULL, ModelId TEXT NOT NULL,
            ModelVersion TEXT NOT NULL, Operation TEXT NOT NULL, CompilerId TEXT NOT NULL,
            CompilerVersion TEXT NOT NULL, WorkflowRevision TEXT NOT NULL,
            ContentPolicyKey TEXT NOT NULL, Status TEXT NOT NULL, Enabled INTEGER NOT NULL,
            EvidenceRunId TEXT NOT NULL, PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            UNIQUE (ProviderKey, ModelId, ModelVersion, Operation, CompilerId, CompilerVersion, WorkflowRevision)
        );
        CREATE INDEX IF NOT EXISTS IX_MediaCapabilityProfiles_Compiler
            ON MediaCapabilityProfiles (CompilerId, CompilerVersion, Status, Enabled);

        CREATE TABLE IF NOT EXISTS MediaCapabilityCells (
            Id TEXT PRIMARY KEY, CapabilityProfileId TEXT NOT NULL, ActorCount INTEGER NOT NULL CHECK (ActorCount > 0),
            FaceAngleKey TEXT NOT NULL, CropKey TEXT NOT NULL, PoseClassKey TEXT NOT NULL,
            CompositionClassKey TEXT NOT NULL, ReferenceControlTupleJson TEXT NOT NULL,
            Status TEXT NOT NULL, EvidenceRunId TEXT NOT NULL, FailureReason TEXT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (CapabilityProfileId) REFERENCES MediaCapabilityProfiles(Id) ON DELETE RESTRICT,
            UNIQUE (CapabilityProfileId, ActorCount, FaceAngleKey, CropKey, PoseClassKey, CompositionClassKey, ReferenceControlTupleJson)
        );
        CREATE INDEX IF NOT EXISTS IX_MediaCapabilityCells_ProfileStatus
            ON MediaCapabilityCells (CapabilityProfileId, Status);

        CREATE TABLE IF NOT EXISTS ProductionIntentSnapshots (
            Id TEXT PRIMARY KEY, ContextKind TEXT NOT NULL, ContextId TEXT NOT NULL,
            ContextSnapshotJson TEXT NOT NULL, ProductionGroupId TEXT NULL, SessionId TEXT NULL,
            CatalogueId TEXT NULL, BeatId TEXT NULL, BeatProductionPlanId TEXT NULL,
            BeatProductionPlanVersion INTEGER NULL CHECK (BeatProductionPlanVersion > 0),
            MomentSetId TEXT NULL, MomentSetVersion INTEGER NULL CHECK (MomentSetVersion > 0),
            MomentId TEXT NULL, MomentEnrichmentId TEXT NULL,
            MomentEnrichmentRevision INTEGER NULL CHECK (MomentEnrichmentRevision > 0),
            Pov TEXT NOT NULL, Operation TEXT NOT NULL, ContentHash TEXT NOT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (ProductionGroupId) REFERENCES SceneImageProductionGroups(Id) ON DELETE RESTRICT
        );
        CREATE INDEX IF NOT EXISTS IX_ProductionIntentSnapshots_Group
            ON ProductionIntentSnapshots (ProductionGroupId, CreatedUtc);
        CREATE INDEX IF NOT EXISTS IX_ProductionIntentSnapshots_Lineage
            ON ProductionIntentSnapshots (SessionId, MomentEnrichmentId, MomentEnrichmentRevision, Pov);

        CREATE TABLE IF NOT EXISTS CompiledMediaRequests (
            Id TEXT PRIMARY KEY, IntentSnapshotId TEXT NOT NULL, CapabilityProfileId TEXT NOT NULL,
            CapabilityCellId TEXT NOT NULL, CompilerId TEXT NOT NULL, CompilerVersion TEXT NOT NULL,
            RequestSchemaVersion TEXT NOT NULL, ProviderKey TEXT NOT NULL, ModelId TEXT NOT NULL,
            ModelVersion TEXT NOT NULL, WorkflowRevision TEXT NOT NULL, ContentHash TEXT NOT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (IntentSnapshotId) REFERENCES ProductionIntentSnapshots(Id) ON DELETE RESTRICT,
            FOREIGN KEY (CapabilityProfileId) REFERENCES MediaCapabilityProfiles(Id) ON DELETE RESTRICT,
            FOREIGN KEY (CapabilityCellId) REFERENCES MediaCapabilityCells(Id) ON DELETE RESTRICT,
            UNIQUE (IntentSnapshotId, CapabilityProfileId, CapabilityCellId, ContentHash)
        );
        CREATE INDEX IF NOT EXISTS IX_CompiledMediaRequests_Intent
            ON CompiledMediaRequests (IntentSnapshotId, CreatedUtc);

        CREATE TABLE IF NOT EXISTS OrderedMediaReferenceBindings (
            Id TEXT PRIMARY KEY, CompiledRequestId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
            SemanticRole TEXT NOT NULL, ActorKey TEXT NULL, SceneAssetId TEXT NOT NULL,
            SceneAssetVersion INTEGER NOT NULL CHECK (SceneAssetVersion > 0), SceneAssetSha256 TEXT NOT NULL,
            IdentityVersionId TEXT NULL, BodyProfileVersionId TEXT NULL, WardrobeLookVersionId TEXT NULL,
            RegionAssetId TEXT NULL, BindingSnapshotJson TEXT NOT NULL, PayloadJson TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (CompiledRequestId) REFERENCES CompiledMediaRequests(Id) ON DELETE RESTRICT,
            FOREIGN KEY (SceneAssetId) REFERENCES SceneAssets(Id) ON DELETE RESTRICT,
            FOREIGN KEY (BodyProfileVersionId) REFERENCES CharacterBodyProfileVersions(Id) ON DELETE RESTRICT,
            FOREIGN KEY (WardrobeLookVersionId) REFERENCES CharacterWardrobeLookVersions(Id) ON DELETE RESTRICT,
            UNIQUE (CompiledRequestId, Ordinal)
        );
        CREATE INDEX IF NOT EXISTS IX_OrderedMediaReferenceBindings_Asset
            ON OrderedMediaReferenceBindings (SceneAssetId);

        CREATE TABLE IF NOT EXISTS IdentityStrategyBindings (
            Id TEXT PRIMARY KEY, CompiledRequestId TEXT NOT NULL, ActorKey TEXT NOT NULL,
            StrategyKind TEXT NOT NULL, CapabilityProfileId TEXT NOT NULL, CapabilityCellId TEXT NOT NULL,
            LoraArtifactId TEXT NULL, LoraArtifactSha256 TEXT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (CompiledRequestId) REFERENCES CompiledMediaRequests(Id) ON DELETE RESTRICT,
            FOREIGN KEY (CapabilityProfileId) REFERENCES MediaCapabilityProfiles(Id) ON DELETE RESTRICT,
            FOREIGN KEY (CapabilityCellId) REFERENCES MediaCapabilityCells(Id) ON DELETE RESTRICT,
            FOREIGN KEY (LoraArtifactId) REFERENCES CharacterLoraArtifacts(Id) ON DELETE RESTRICT,
            UNIQUE (CompiledRequestId, ActorKey)
        );

        CREATE TABLE IF NOT EXISTS ProductionWorkloads (
            Id TEXT PRIMARY KEY, ContextKind TEXT NOT NULL, ContextId TEXT NOT NULL,
            ContextSnapshotJson TEXT NOT NULL, SessionId TEXT NULL,
            Revision INTEGER NOT NULL CHECK (Revision > 0),
            Status TEXT NOT NULL, ConcurrencyVersion INTEGER NOT NULL CHECK (ConcurrencyVersion > 0),
            Goal TEXT NOT NULL, ContentPolicyKey TEXT NOT NULL, ItemCount INTEGER NOT NULL CHECK (ItemCount > 0),
            OutputCount INTEGER NOT NULL CHECK (OutputCount > 0), CompatibilityGroupCount INTEGER NOT NULL CHECK (CompatibilityGroupCount > 0),
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL, UNIQUE (ContextKind, ContextId, Revision)
        );
        CREATE INDEX IF NOT EXISTS IX_ProductionWorkloads_SessionStatus
            ON ProductionWorkloads (SessionId, Status, CreatedUtc);

        CREATE TABLE IF NOT EXISTS ProductionWorkloadItems (
            Id TEXT PRIMARY KEY, WorkloadId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
            IntentSnapshotId TEXT NOT NULL, CompiledRequestId TEXT NOT NULL, CompatibilityKey TEXT NOT NULL,
            VariationCount INTEGER NOT NULL CHECK (VariationCount > 0), Status TEXT NOT NULL,
            ConcurrencyVersion INTEGER NOT NULL CHECK (ConcurrencyVersion > 0), CurrentAttemptId TEXT NULL,
            FailureCode TEXT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (WorkloadId) REFERENCES ProductionWorkloads(Id) ON DELETE RESTRICT,
            FOREIGN KEY (IntentSnapshotId) REFERENCES ProductionIntentSnapshots(Id) ON DELETE RESTRICT,
            FOREIGN KEY (CompiledRequestId) REFERENCES CompiledMediaRequests(Id) ON DELETE RESTRICT,
            FOREIGN KEY (CurrentAttemptId) REFERENCES ProductionAttempts(Id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED,
            UNIQUE (WorkloadId, Ordinal)
        );
        CREATE INDEX IF NOT EXISTS IX_ProductionWorkloadItems_WorkloadStatus
            ON ProductionWorkloadItems (WorkloadId, Status, Ordinal);
        CREATE INDEX IF NOT EXISTS IX_ProductionWorkloadItems_Compatibility
            ON ProductionWorkloadItems (CompatibilityKey, Status);

        CREATE TABLE IF NOT EXISTS ProductionAttempts (
            Id TEXT PRIMARY KEY, WorkloadItemId TEXT NOT NULL, AttemptNumber INTEGER NOT NULL CHECK (AttemptNumber > 0),
            Kind TEXT NOT NULL, Status TEXT NOT NULL, ConcurrencyVersion INTEGER NOT NULL CHECK (ConcurrencyVersion > 0),
            CompiledRequestId TEXT NOT NULL, CompiledRequestHash TEXT NOT NULL,
            ParentAttemptId TEXT NULL, RepairSourceAttemptId TEXT NULL,
            ProviderKey TEXT NULL, ProviderRequestId TEXT NULL, ProviderStatusUrl TEXT NULL,
            ProviderResponseSnapshotJson TEXT NULL, OutputFileRelativePath TEXT NULL, OutputSha256 TEXT NULL,
            OutputByteLength INTEGER NULL, OutputMetadataJson TEXT NULL, CostSnapshotJson TEXT NULL,
            FailureCode TEXT NULL, FailureDiagnostic TEXT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL, SubmittedUtc TEXT NULL, CompletedUtc TEXT NULL,
            FOREIGN KEY (WorkloadItemId) REFERENCES ProductionWorkloadItems(Id) ON DELETE RESTRICT,
            FOREIGN KEY (CompiledRequestId) REFERENCES CompiledMediaRequests(Id) ON DELETE RESTRICT,
            FOREIGN KEY (ParentAttemptId) REFERENCES ProductionAttempts(Id) ON DELETE RESTRICT,
            UNIQUE (WorkloadItemId, AttemptNumber), UNIQUE (ProviderKey, ProviderRequestId)
        );
        CREATE INDEX IF NOT EXISTS IX_ProductionAttempts_ItemStatus
            ON ProductionAttempts (WorkloadItemId, Status, AttemptNumber);
        CREATE INDEX IF NOT EXISTS IX_ProductionAttempts_Provider
            ON ProductionAttempts (ProviderKey, ProviderRequestId);

        CREATE TABLE IF NOT EXISTS ProductionReviewDecisions (
            Id TEXT PRIMARY KEY, WorkloadItemId TEXT NOT NULL, AttemptId TEXT NOT NULL,
            Version INTEGER NOT NULL CHECK (Version > 0), Decision TEXT NOT NULL,
            ReasonCode TEXT NOT NULL, Notes TEXT NULL, DecidedBy TEXT NOT NULL,
            DecidedUtc TEXT NOT NULL, PayloadJson TEXT NOT NULL,
            FOREIGN KEY (WorkloadItemId) REFERENCES ProductionWorkloadItems(Id) ON DELETE RESTRICT,
            FOREIGN KEY (AttemptId) REFERENCES ProductionAttempts(Id) ON DELETE RESTRICT,
            UNIQUE (WorkloadItemId, Version)
        );
        CREATE INDEX IF NOT EXISTS IX_ProductionReviewDecisions_Attempt
            ON ProductionReviewDecisions (AttemptId, Version);

        CREATE TABLE IF NOT EXISTS ProductionDerivatives (
            Id TEXT PRIMARY KEY, WorkloadItemId TEXT NOT NULL, AttemptId TEXT NOT NULL UNIQUE,
            ReviewDecisionId TEXT NOT NULL UNIQUE, SceneAssetId TEXT NOT NULL,
            CapabilityProfileId TEXT NOT NULL, Status TEXT NOT NULL, UseScopeKey TEXT NOT NULL,
            PayloadJson TEXT NOT NULL, ApprovedUtc TEXT NOT NULL,
            FOREIGN KEY (WorkloadItemId) REFERENCES ProductionWorkloadItems(Id) ON DELETE RESTRICT,
            FOREIGN KEY (AttemptId) REFERENCES ProductionAttempts(Id) ON DELETE RESTRICT,
            FOREIGN KEY (ReviewDecisionId) REFERENCES ProductionReviewDecisions(Id) ON DELETE RESTRICT,
            FOREIGN KEY (SceneAssetId) REFERENCES SceneAssets(Id) ON DELETE RESTRICT,
            FOREIGN KEY (CapabilityProfileId) REFERENCES MediaCapabilityProfiles(Id) ON DELETE RESTRICT
        );
        CREATE INDEX IF NOT EXISTS IX_ProductionDerivatives_Asset
            ON ProductionDerivatives (SceneAssetId, Status);
        """;
}