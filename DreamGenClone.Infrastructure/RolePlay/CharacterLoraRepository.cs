using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class CharacterLoraRepository : ICharacterLoraRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public CharacterLoraRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
    }

    public async Task<CharacterLoraTrainingProfile> CreateTrainingProfileAsync(
        CharacterLoraTrainingProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateNewTrainingProfile(profile);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterLoraTrainingProfiles
                (Id, Name, Version, Status, Enabled, TargetModelFamily, BaseModelId,
                 BaseModelVersion, BaseModelSha256, TrainerId, TrainerVersion,
                 PayloadJson, CreatedUtc, QualifiedUtc)
            VALUES ($id, $name, $version, $status, 0, $family, $model,
                    $modelVersion, $modelSha, $trainer, $trainerVersion,
                    $payload, $created, NULL);
            """;
        command.Parameters.AddWithValue("$id", profile.Id.Trim());
        command.Parameters.AddWithValue("$name", profile.Name.Trim());
        command.Parameters.AddWithValue("$version", profile.Version);
        command.Parameters.AddWithValue("$status", profile.Status.ToString());
        command.Parameters.AddWithValue("$family", profile.TargetModelFamily.Trim());
        command.Parameters.AddWithValue("$model", profile.BaseModelId.Trim());
        command.Parameters.AddWithValue("$modelVersion", profile.BaseModelVersion.Trim());
        command.Parameters.AddWithValue("$modelSha", profile.BaseModelSha256.Trim());
        command.Parameters.AddWithValue("$trainer", profile.TrainerId.Trim());
        command.Parameters.AddWithValue("$trainerVersion", profile.TrainerVersion.Trim());
        command.Parameters.AddWithValue("$payload", Serialize(profile));
        command.Parameters.AddWithValue("$created", FormatUtc(profile.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return profile;
    }

    public async Task<CharacterLoraTrainingProfile?> GetTrainingProfileAsync(
        string profileId, CancellationToken cancellationToken = default)
    {
        Require(profileId, "LoRA training profile id");
        await using var connection = await OpenAsync(cancellationToken);
        return await ReadTrainingProfileAsync(connection, profileId.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterLoraTrainingProfile>> ListTrainingProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraTrainingProfiles ORDER BY Name, Version;";
        return await ReadPayloadsAsync<CharacterLoraTrainingProfile>(command, cancellationToken);
    }

    public async Task<CharacterLoraTrainingProfile> QualifyTrainingProfileAsync(
        string profileId,
        string qualificationEvidenceJson,
        DateTime qualifiedUtc,
        CancellationToken cancellationToken = default)
    {
        Require(profileId, "LoRA training profile id");
        RequireJsonObject(qualificationEvidenceJson, "LoRA training qualification evidence");
        RequireUtc(qualifiedUtc, "LoRA training profile qualification time");
        await using var connection = await OpenAsync(cancellationToken);
        var profile = await ReadTrainingProfileAsync(connection, profileId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"LoRA training profile '{profileId}' was not found.");
        if (profile.Status != CharacterLoraTrainingProfileStatus.Draft || profile.Enabled)
            throw new InvalidOperationException($"LoRA training profile '{profileId}' is not a disabled draft.");
        ValidateCompleteTrainingProfile(profile);
        profile.Status = CharacterLoraTrainingProfileStatus.Qualified;
        profile.Enabled = true;
        profile.QualificationEvidenceJson = qualificationEvidenceJson;
        profile.QualifiedUtc = qualifiedUtc;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CharacterLoraTrainingProfiles
            SET Status = 'Qualified', Enabled = 1, PayloadJson = $payload, QualifiedUtc = $qualified
            WHERE Id = $id AND Status = 'Draft' AND Enabled = 0;
            """;
        command.Parameters.AddWithValue("$payload", Serialize(profile));
        command.Parameters.AddWithValue("$qualified", FormatUtc(qualifiedUtc));
        command.Parameters.AddWithValue("$id", profile.Id);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "LoRA training profile", profile.Id);
        return profile;
    }

    public async Task<CharacterLoraDataset> CreateDatasetAsync(
        CharacterLoraDataset dataset, CancellationToken cancellationToken = default)
    {
        ValidateNewDataset(dataset);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterLoraDatasets
                (Id, CharacterProfileId, IdentityPackId, Version, Status, TargetModelFamily,
                 ManifestSha256, SupersedesId, PayloadJson, CreatedUtc, FrozenUtc)
            VALUES ($id, $character, $pack, $version, $status, $family,
                    NULL, $supersedes, $payload, $created, NULL);
            """;
        command.Parameters.AddWithValue("$id", dataset.Id.Trim());
        command.Parameters.AddWithValue("$character", dataset.CharacterProfileId.Trim());
        command.Parameters.AddWithValue("$pack", dataset.IdentityPackId.Trim());
        command.Parameters.AddWithValue("$version", dataset.Version);
        command.Parameters.AddWithValue("$status", dataset.Status.ToString());
        command.Parameters.AddWithValue("$family", dataset.TargetModelFamily.Trim());
        command.Parameters.AddWithValue("$supersedes", DbValue(dataset.SupersedesId));
        command.Parameters.AddWithValue("$payload", Serialize(dataset));
        command.Parameters.AddWithValue("$created", FormatUtc(dataset.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return dataset;
    }

    public async Task<CharacterLoraDataset?> GetDatasetAsync(
        string datasetId, CancellationToken cancellationToken = default)
    {
        Require(datasetId, "LoRA dataset id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetDatasetAsync(connection, null, datasetId.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterLoraDataset>> ListDatasetsAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraDatasets WHERE CharacterProfileId = $id ORDER BY Version;";
        command.Parameters.AddWithValue("$id", characterProfileId.Trim());
        return await ReadPayloadsAsync<CharacterLoraDataset>(command, cancellationToken);
    }

    public async Task AddDatasetMemberAsync(
        CharacterLoraDatasetMember member, CancellationToken cancellationToken = default)
    {
        ValidateMember(member);
        await using var connection = await OpenAsync(cancellationToken);
        await RequireDraftAsync(connection, null, member.DatasetId, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterLoraDatasetMembers
                (Id, DatasetId, Ordinal, SceneAssetId, SceneAssetVersion, AssetSha256,
                 Role, Split, CurationStatus, PayloadJson)
            VALUES ($id, $dataset, $ordinal, $asset, $assetVersion, $sha,
                    $role, $split, $curation, $payload);
            """;
        command.Parameters.AddWithValue("$id", member.Id.Trim());
        command.Parameters.AddWithValue("$dataset", member.DatasetId.Trim());
        command.Parameters.AddWithValue("$ordinal", member.Ordinal);
        command.Parameters.AddWithValue("$asset", member.SceneAssetId.Trim());
        command.Parameters.AddWithValue("$assetVersion", member.SceneAssetVersion);
        command.Parameters.AddWithValue("$sha", member.AssetSha256.Trim());
        command.Parameters.AddWithValue("$role", member.Role.ToString());
        command.Parameters.AddWithValue("$split", member.Split.ToString());
        command.Parameters.AddWithValue("$curation", member.CurationStatus.ToString());
        command.Parameters.AddWithValue("$payload", Serialize(member));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterLoraDatasetMember>> ListDatasetMembersAsync(
        string datasetId, CancellationToken cancellationToken = default)
    {
        Require(datasetId, "LoRA dataset id");
        await using var connection = await OpenAsync(cancellationToken);
        return await ListMembersAsync(connection, null, datasetId.Trim(), cancellationToken);
    }

    public async Task<CharacterLoraDatasetMember> CurateDatasetMemberAsync(
        CharacterLoraDatasetMember member,
        int expectedCaptionRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateMember(member);
        if (expectedCaptionRevision <= 0 || member.CaptionRevision != expectedCaptionRevision + 1)
            throw new InvalidOperationException("LoRA dataset member curation must increment the exact caption revision by one.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await RequireDraftAsync(connection, transaction, member.DatasetId.Trim(), cancellationToken);
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT PayloadJson FROM CharacterLoraDatasetMembers WHERE Id = $id AND DatasetId = $dataset;";
        read.Parameters.AddWithValue("$id", member.Id.Trim());
        read.Parameters.AddWithValue("$dataset", member.DatasetId.Trim());
        var existing = DeserializeOrNull<CharacterLoraDatasetMember>(await read.ExecuteScalarAsync(cancellationToken))
            ?? throw new InvalidOperationException($"LoRA dataset member '{member.Id}' was not found.");
        if (existing.CaptionRevision != expectedCaptionRevision)
            throw new InvalidOperationException($"LoRA dataset member '{member.Id}' caption revision changed.");
        if (!string.Equals(existing.SceneAssetId, member.SceneAssetId, StringComparison.Ordinal)
            || existing.SceneAssetVersion != member.SceneAssetVersion
            || !string.Equals(existing.AssetSha256, member.AssetSha256, StringComparison.Ordinal)
            || !string.Equals(existing.GenerationAttemptId, member.GenerationAttemptId, StringComparison.Ordinal)
            || existing.Ordinal != member.Ordinal)
            throw new InvalidOperationException("LoRA dataset member asset, attempt, and ordinal lineage are immutable.");
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE CharacterLoraDatasetMembers
            SET Role = $role, Split = $split, CurationStatus = $curation, PayloadJson = $payload
            WHERE Id = $id AND DatasetId = $dataset;
            """;
        update.Parameters.AddWithValue("$role", member.Role.ToString());
        update.Parameters.AddWithValue("$split", member.Split.ToString());
        update.Parameters.AddWithValue("$curation", member.CurationStatus.ToString());
        update.Parameters.AddWithValue("$payload", Serialize(member));
        update.Parameters.AddWithValue("$id", member.Id.Trim());
        update.Parameters.AddWithValue("$dataset", member.DatasetId.Trim());
        EnsureChanged(await update.ExecuteNonQueryAsync(cancellationToken), "LoRA dataset member", member.Id);
        await transaction.CommitAsync(cancellationToken);
        return member;
    }

    public async Task<CharacterLoraDataset> FreezeDatasetAsync(
        string datasetId, string frozenBy, DateTime frozenUtc, CancellationToken cancellationToken = default)
    {
        Require(datasetId, "LoRA dataset id");
        Require(frozenBy, "LoRA dataset freezer");
        RequireUtc(frozenUtc, "LoRA dataset freeze time");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var dataset = await RequireDraftAsync(connection, transaction, datasetId.Trim(), cancellationToken);
        var members = await ListMembersAsync(connection, transaction, dataset.Id, cancellationToken);
        ValidateFreezeMembers(members);
        foreach (var member in members)
            await RequireApprovedAssetAsync(connection, transaction, member, cancellationToken);

        dataset.Status = CharacterLoraDatasetStatus.Frozen;
        dataset.ManifestSha256 = CharacterLoraManifestHash.Compute(dataset, members);
        dataset.FrozenBy = frozenBy.Trim();
        dataset.FrozenUtc = frozenUtc;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE CharacterLoraDatasets
            SET Status = $status, ManifestSha256 = $manifest, PayloadJson = $payload,
                FrozenUtc = $frozen
            WHERE Id = $id AND Status = 'Draft';
            """;
        command.Parameters.AddWithValue("$status", dataset.Status.ToString());
        command.Parameters.AddWithValue("$manifest", dataset.ManifestSha256);
        command.Parameters.AddWithValue("$payload", Serialize(dataset));
        command.Parameters.AddWithValue("$frozen", FormatUtc(frozenUtc));
        command.Parameters.AddWithValue("$id", dataset.Id);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "LoRA dataset", dataset.Id);
        await transaction.CommitAsync(cancellationToken);
        return dataset;
    }

    public async Task<CharacterLoraTrainingJob> CreateTrainingJobAsync(
        CharacterLoraTrainingJob job, CancellationToken cancellationToken = default)
    {
        ValidateNewJob(job);
        await using var connection = await OpenAsync(cancellationToken);
        var dataset = await GetDatasetAsync(connection, null, job.DatasetId, cancellationToken)
            ?? throw new InvalidOperationException($"LoRA dataset '{job.DatasetId}' was not found.");
        if (dataset.Status != CharacterLoraDatasetStatus.Frozen || string.IsNullOrWhiteSpace(dataset.ManifestSha256))
            throw new InvalidOperationException("LoRA training requires a frozen dataset with an exact manifest hash.");
        var profile = await ReadTrainingProfileAsync(connection, job.TrainingProfileId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"LoRA training profile '{job.TrainingProfileId}' was not found.");
        RequireExactQualifiedTrainingProfile(job, dataset, profile);
        job.TrainingProfileVersion = profile.Version;
        job.TrainingProfileSnapshotJson = Serialize(profile);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterLoraTrainingJobs
                (Id, DatasetId, TrainingProfileId, BaseModelId, BaseModelVersion, BaseModelSha256,
                 TrainerId, TrainerVersion, Status, ConcurrencyVersion, PayloadJson, CreatedUtc)
            VALUES ($id, $dataset, $profile, $model, $modelVersion, $modelSha,
                    $trainer, $trainerVersion, $status, $concurrency, $payload, $created);
            """;
        command.Parameters.AddWithValue("$id", job.Id.Trim());
        command.Parameters.AddWithValue("$dataset", job.DatasetId.Trim());
        command.Parameters.AddWithValue("$profile", job.TrainingProfileId.Trim());
        command.Parameters.AddWithValue("$model", job.BaseModelId.Trim());
        command.Parameters.AddWithValue("$modelVersion", job.BaseModelVersion.Trim());
        command.Parameters.AddWithValue("$modelSha", job.BaseModelSha256.Trim());
        command.Parameters.AddWithValue("$trainer", job.TrainerId.Trim());
        command.Parameters.AddWithValue("$trainerVersion", job.TrainerVersion.Trim());
        command.Parameters.AddWithValue("$status", job.Status.ToString());
        command.Parameters.AddWithValue("$concurrency", job.ConcurrencyVersion);
        command.Parameters.AddWithValue("$payload", Serialize(job));
        command.Parameters.AddWithValue("$created", FormatUtc(job.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return job;
    }

    public async Task<CharacterLoraTrainingJob?> GetTrainingJobAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        Require(jobId, "LoRA training job id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraTrainingJobs WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", jobId.Trim());
        return DeserializeOrNull<CharacterLoraTrainingJob>(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<CharacterLoraTrainingJob>> ListTrainingJobsAsync(
        string datasetId, CancellationToken cancellationToken = default)
    {
        Require(datasetId, "LoRA training dataset id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraTrainingJobs WHERE DatasetId = $id ORDER BY CreatedUtc, Id;";
        command.Parameters.AddWithValue("$id", datasetId.Trim());
        return await ReadPayloadsAsync<CharacterLoraTrainingJob>(command, cancellationToken);
    }

    public async Task<CharacterLoraTrainingJob> TransitionTrainingJobAsync(
        string jobId,
        CharacterLoraTrainingJobStatus expectedStatus,
        CharacterLoraTrainingJobStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(jobId, "LoRA training job id");
        if (!IsAllowed(expectedStatus, nextStatus))
            throw new InvalidOperationException($"LoRA training job cannot transition from {expectedStatus} to {nextStatus}.");
        await using var connection = await OpenAsync(cancellationToken);
        var job = await ReadTrainingJobAsync(connection, jobId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"LoRA training job '{jobId}' was not found.");
        if (job.Status != expectedStatus || job.ConcurrencyVersion != expectedConcurrencyVersion)
            throw new InvalidOperationException($"LoRA training job '{jobId}' state or concurrency version changed.");
        var transitionUtc = DateTime.UtcNow;
        job.Status = nextStatus;
        job.ConcurrencyVersion++;
        if (nextStatus == CharacterLoraTrainingJobStatus.Queued) job.QueuedUtc = transitionUtc;
        if (nextStatus is CharacterLoraTrainingJobStatus.Succeeded or CharacterLoraTrainingJobStatus.Failed or CharacterLoraTrainingJobStatus.Cancelled)
            job.CompletedUtc = transitionUtc;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CharacterLoraTrainingJobs
            SET Status = $next, ConcurrencyVersion = $nextVersion, PayloadJson = $payload
            WHERE Id = $id AND Status = $expected AND ConcurrencyVersion = $expectedVersion;
            """;
        command.Parameters.AddWithValue("$next", nextStatus.ToString());
        command.Parameters.AddWithValue("$nextVersion", job.ConcurrencyVersion);
        command.Parameters.AddWithValue("$payload", Serialize(job));
        command.Parameters.AddWithValue("$id", job.Id);
        command.Parameters.AddWithValue("$expected", expectedStatus.ToString());
        command.Parameters.AddWithValue("$expectedVersion", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "LoRA training job", job.Id);
        return job;
    }

    public async Task<CharacterLoraTrainingAttempt> CreateTrainingAttemptAsync(
        CharacterLoraTrainingAttempt attempt, CancellationToken cancellationToken = default)
    {
        ValidateNewAttempt(attempt);
        await using var connection = await OpenAsync(cancellationToken);
        var job = await ReadTrainingJobAsync(connection, attempt.TrainingJobId, cancellationToken)
            ?? throw new InvalidOperationException($"LoRA training job '{attempt.TrainingJobId}' was not found.");
        if (job.Status != CharacterLoraTrainingJobStatus.Running)
            throw new InvalidOperationException("A LoRA training attempt requires a running training job.");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterLoraTrainingAttempts
                (Id, TrainingJobId, AttemptNumber, Status, ConcurrencyVersion,
                 ProviderKey, ProviderRequestId, OutputSha256, PayloadJson, CreatedUtc)
            VALUES ($id, $job, $number, $status, $concurrency,
                    NULL, NULL, NULL, $payload, $created);
            """;
        command.Parameters.AddWithValue("$id", attempt.Id.Trim());
        command.Parameters.AddWithValue("$job", attempt.TrainingJobId.Trim());
        command.Parameters.AddWithValue("$number", attempt.AttemptNumber);
        command.Parameters.AddWithValue("$status", attempt.Status.ToString());
        command.Parameters.AddWithValue("$concurrency", attempt.ConcurrencyVersion);
        command.Parameters.AddWithValue("$payload", Serialize(attempt));
        command.Parameters.AddWithValue("$created", FormatUtc(attempt.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return attempt;
    }

    public async Task<CharacterLoraTrainingAttempt?> GetTrainingAttemptAsync(
        string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "LoRA training attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraTrainingAttempts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", attemptId.Trim());
        return DeserializeOrNull<CharacterLoraTrainingAttempt>(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<CharacterLoraTrainingAttempt> RecordTrainingSubmissionAsync(
        string attemptId,
        string providerKey,
        string providerRequestId,
        string providerStatusUrl,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(providerKey, "LoRA training provider key");
        Require(providerRequestId, "LoRA training provider request id");
        Require(providerStatusUrl, "LoRA training provider status URL");
        await using var connection = await OpenAsync(cancellationToken);
        var attempt = await RequireAttemptAsync(connection, attemptId, cancellationToken);
        if (attempt.Status != CharacterLoraTrainingAttemptStatus.Pending
            || attempt.ConcurrencyVersion != expectedConcurrencyVersion)
            throw new InvalidOperationException($"LoRA training attempt '{attemptId}' is not the expected pending version.");
        attempt.Status = CharacterLoraTrainingAttemptStatus.Submitted;
        attempt.ConcurrencyVersion++;
        attempt.ProviderKey = providerKey.Trim();
        attempt.ProviderRequestId = providerRequestId.Trim();
        attempt.ProviderStatusUrl = providerStatusUrl.Trim();
        attempt.SubmittedUtc = DateTime.UtcNow;
        await UpdateAttemptAsync(connection, attempt, CharacterLoraTrainingAttemptStatus.Pending,
            expectedConcurrencyVersion, cancellationToken);
        return attempt;
    }

    public async Task<CharacterLoraTrainingAttempt> TransitionTrainingAttemptAsync(
        string attemptId,
        CharacterLoraTrainingAttemptStatus expectedStatus,
        CharacterLoraTrainingAttemptStatus nextStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        if (!IsAllowed(expectedStatus, nextStatus))
            throw new InvalidOperationException($"LoRA training attempt cannot transition from {expectedStatus} to {nextStatus}.");
        await using var connection = await OpenAsync(cancellationToken);
        var attempt = await RequireAttemptAsync(connection, attemptId, cancellationToken);
        if (attempt.Status != expectedStatus || attempt.ConcurrencyVersion != expectedConcurrencyVersion)
            throw new InvalidOperationException($"LoRA training attempt '{attemptId}' state or concurrency version changed.");
        attempt.Status = nextStatus;
        attempt.ConcurrencyVersion++;
        if (nextStatus is CharacterLoraTrainingAttemptStatus.Failed or CharacterLoraTrainingAttemptStatus.Cancelled
            or CharacterLoraTrainingAttemptStatus.Indeterminate)
            attempt.CompletedUtc = DateTime.UtcNow;
        await UpdateAttemptAsync(connection, attempt, expectedStatus, expectedConcurrencyVersion, cancellationToken);
        return attempt;
    }

    public async Task<CharacterLoraTrainingAttempt> RecordTrainingResultAsync(
        string attemptId,
        string outputFileRelativePath,
        string outputSha256,
        long outputByteLength,
        string statusHistoryJson,
        string logManifestJson,
        string sampleManifestJson,
        string checkpointManifestJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        Require(outputFileRelativePath, "LoRA training output path");
        RequireSha256(outputSha256, "LoRA training output checksum");
        if (outputByteLength <= 0) throw new InvalidOperationException("LoRA training output byte length must be positive.");
        RequireJson(statusHistoryJson, "LoRA training status history");
        RequireJson(logManifestJson, "LoRA training log manifest");
        RequireJson(sampleManifestJson, "LoRA training sample manifest");
        RequireJson(checkpointManifestJson, "LoRA training checkpoint manifest");
        await using var connection = await OpenAsync(cancellationToken);
        var attempt = await RequireAttemptAsync(connection, attemptId, cancellationToken);
        if (attempt.Status != CharacterLoraTrainingAttemptStatus.Running
            || attempt.ConcurrencyVersion != expectedConcurrencyVersion)
            throw new InvalidOperationException($"LoRA training attempt '{attemptId}' is not the expected running version.");
        attempt.Status = CharacterLoraTrainingAttemptStatus.Succeeded;
        attempt.ConcurrencyVersion++;
        attempt.OutputFileRelativePath = outputFileRelativePath.Trim();
        attempt.OutputSha256 = outputSha256.Trim();
        attempt.OutputByteLength = outputByteLength;
        attempt.StatusHistoryJson = statusHistoryJson;
        attempt.LogManifestJson = logManifestJson;
        attempt.SampleManifestJson = sampleManifestJson;
        attempt.CheckpointManifestJson = checkpointManifestJson;
        attempt.CompletedUtc = DateTime.UtcNow;
        await UpdateAttemptAsync(connection, attempt, CharacterLoraTrainingAttemptStatus.Running,
            expectedConcurrencyVersion, cancellationToken);
        return attempt;
    }

    public async Task<CharacterLoraTrainingAttempt> RecordTrainingFailureAsync(
        string attemptId,
        CharacterLoraTrainingAttemptStatus expectedStatus,
        CharacterLoraTrainingAttemptStatus failureStatus,
        string failureCode,
        string failureDiagnostic,
        string statusHistoryJson,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        if (expectedStatus is not (CharacterLoraTrainingAttemptStatus.Pending
            or CharacterLoraTrainingAttemptStatus.Submitted
            or CharacterLoraTrainingAttemptStatus.Running))
            throw new InvalidOperationException("Only an active LoRA training attempt can record a failure.");
        if (failureStatus is not (CharacterLoraTrainingAttemptStatus.Failed
            or CharacterLoraTrainingAttemptStatus.Cancelled
            or CharacterLoraTrainingAttemptStatus.Indeterminate))
            throw new InvalidOperationException("LoRA training failure status must be Failed, Cancelled, or Indeterminate.");
        Require(failureCode, "LoRA training failure code");
        Require(failureDiagnostic, "LoRA training failure diagnostic");
        RequireJson(statusHistoryJson, "LoRA training status history");
        await using var connection = await OpenAsync(cancellationToken);
        var attempt = await RequireAttemptAsync(connection, attemptId, cancellationToken);
        if (attempt.Status != expectedStatus || attempt.ConcurrencyVersion != expectedConcurrencyVersion)
            throw new InvalidOperationException($"LoRA training attempt '{attemptId}' state or concurrency version changed.");
        attempt.Status = failureStatus;
        attempt.ConcurrencyVersion++;
        attempt.FailureCode = failureCode.Trim();
        attempt.FailureDiagnostic = failureDiagnostic.Trim();
        attempt.StatusHistoryJson = statusHistoryJson;
        attempt.CompletedUtc = DateTime.UtcNow;
        await UpdateAttemptAsync(connection, attempt, expectedStatus, expectedConcurrencyVersion, cancellationToken);
        return attempt;
    }

    public async Task<IReadOnlyList<CharacterLoraTrainingAttempt>> ListTrainingAttemptsAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        Require(jobId, "LoRA training job id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraTrainingAttempts WHERE TrainingJobId = $id ORDER BY AttemptNumber;";
        command.Parameters.AddWithValue("$id", jobId.Trim());
        return await ReadPayloadsAsync<CharacterLoraTrainingAttempt>(command, cancellationToken);
    }

    public async Task<CharacterLoraArtifact> CreateArtifactAsync(
        CharacterLoraArtifact artifact, CancellationToken cancellationToken = default)
    {
        ValidateNewArtifact(artifact);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var lineage = await ReadArtifactLineageAsync(connection, transaction, artifact.TrainingAttemptId, cancellationToken);
        if (lineage.AttemptStatus != CharacterLoraTrainingAttemptStatus.Succeeded)
            throw new InvalidOperationException("A LoRA artifact requires a succeeded training attempt.");
        if (!string.Equals(lineage.OutputSha256, artifact.Sha256, StringComparison.Ordinal)
            || !string.Equals(lineage.OutputFileRelativePath, artifact.FileRelativePath, StringComparison.Ordinal)
            || !string.Equals(lineage.DatasetId, artifact.DatasetId, StringComparison.Ordinal)
            || !string.Equals(lineage.CharacterProfileId, artifact.CharacterProfileId, StringComparison.Ordinal)
            || !string.Equals(lineage.TriggerToken, artifact.TriggerToken, StringComparison.Ordinal)
            || !string.Equals(lineage.BaseModelId, artifact.BaseModelId, StringComparison.Ordinal)
            || !string.Equals(lineage.BaseModelVersion, artifact.BaseModelVersion, StringComparison.Ordinal)
            || !string.Equals(lineage.BaseModelSha256, artifact.BaseModelSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LoRA artifact lineage does not match its dataset, training attempt, base model, and output checksum.");
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CharacterLoraArtifacts
                (Id, CharacterProfileId, DatasetId, TrainingAttemptId, Version,
                 BaseModelId, BaseModelVersion, BaseModelSha256, Status, Sha256,
                 PayloadJson, CreatedUtc)
            VALUES ($id, $character, $dataset, $attempt, $version,
                    $model, $modelVersion, $modelSha, $status, $sha, $payload, $created);
            """;
        command.Parameters.AddWithValue("$id", artifact.Id.Trim());
        command.Parameters.AddWithValue("$character", artifact.CharacterProfileId.Trim());
        command.Parameters.AddWithValue("$dataset", artifact.DatasetId.Trim());
        command.Parameters.AddWithValue("$attempt", artifact.TrainingAttemptId.Trim());
        command.Parameters.AddWithValue("$version", artifact.Version);
        command.Parameters.AddWithValue("$model", artifact.BaseModelId.Trim());
        command.Parameters.AddWithValue("$modelVersion", artifact.BaseModelVersion.Trim());
        command.Parameters.AddWithValue("$modelSha", artifact.BaseModelSha256.Trim());
        command.Parameters.AddWithValue("$status", artifact.Status.ToString());
        command.Parameters.AddWithValue("$sha", artifact.Sha256.Trim());
        command.Parameters.AddWithValue("$payload", Serialize(artifact));
        command.Parameters.AddWithValue("$created", FormatUtc(artifact.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return artifact;
    }

    public async Task<CharacterLoraArtifact?> GetArtifactAsync(
        string artifactId, CancellationToken cancellationToken = default)
    {
        Require(artifactId, "LoRA artifact id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraArtifacts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", artifactId.Trim());
        return DeserializeOrNull<CharacterLoraArtifact>(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<CharacterLoraArtifact>> ListArtifactsAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraArtifacts WHERE CharacterProfileId = $id ORDER BY Version DESC, CreatedUtc DESC;";
        command.Parameters.AddWithValue("$id", characterProfileId.Trim());
        return await ReadPayloadsAsync<CharacterLoraArtifact>(command, cancellationToken);
    }

    public async Task<CharacterLoraArtifact> SetArtifactStatusAsync(
        string artifactId,
        CharacterLoraArtifactStatus status,
        string decisionEvidenceJson,
        DateTime decidedUtc,
        CancellationToken cancellationToken = default)
    {
        Require(artifactId, "LoRA artifact id");
        RequireSecretFreeJsonObject(decisionEvidenceJson, "LoRA artifact decision evidence");
        RequireUtc(decidedUtc, "LoRA artifact decision time");
        if (status is not (CharacterLoraArtifactStatus.Qualified or CharacterLoraArtifactStatus.Rejected))
            throw new InvalidOperationException("A candidate LoRA artifact can only be qualified or rejected.");
        await using var connection = await OpenAsync(cancellationToken);
        var artifact = await ReadArtifactAsync(connection, artifactId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"LoRA artifact '{artifactId}' was not found.");
        if (artifact.Status != CharacterLoraArtifactStatus.Candidate)
            throw new InvalidOperationException($"LoRA artifact '{artifactId}' is {artifact.Status}; only candidates can be decided.");
        artifact.Status = status;
        artifact.DecisionEvidenceJson = decisionEvidenceJson;
        artifact.QualifiedUtc = status == CharacterLoraArtifactStatus.Qualified ? decidedUtc : null;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CharacterLoraArtifacts SET Status = $status, PayloadJson = $payload
            WHERE Id = $id AND Status = 'Candidate';
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$payload", Serialize(artifact));
        command.Parameters.AddWithValue("$id", artifact.Id);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "LoRA artifact", artifact.Id);
        return artifact;
    }

    public async Task CreateIdentityStrategyBindingAsync(
        IdentityStrategyBinding binding, CancellationToken cancellationToken = default)
    {
        ValidateStrategyBinding(binding);
        await using var connection = await OpenAsync(cancellationToken);
        await RequireQualifiedRequestCellAsync(connection, binding, cancellationToken);
        if (binding.StrategyKind is CharacterIdentityStrategyKind.Lora or CharacterIdentityStrategyKind.Combined)
        {
            await using var artifact = connection.CreateCommand();
            artifact.CommandText = "SELECT Status, Sha256 FROM CharacterLoraArtifacts WHERE Id = $id;";
            artifact.Parameters.AddWithValue("$id", binding.LoraArtifactId!.Trim());
            await using var reader = await artifact.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)
                || !string.Equals(reader.GetString(0), CharacterLoraArtifactStatus.Qualified.ToString(), StringComparison.Ordinal)
                || !string.Equals(reader.GetString(1), binding.LoraArtifactSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("LoRA identity binding requires the exact checksum of a qualified artifact.");
            }
        }
        await using var command = connection.CreateCommand();
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
        command.Parameters.AddWithValue("$artifact", DbValue(binding.LoraArtifactId));
        command.Parameters.AddWithValue("$sha", DbValue(binding.LoraArtifactSha256));
        command.Parameters.AddWithValue("$payload", Serialize(binding));
        command.Parameters.AddWithValue("$created", FormatUtc(binding.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IdentityStrategyBinding>> ListIdentityStrategyBindingsAsync(
        string compiledRequestId, CancellationToken cancellationToken = default)
    {
        Require(compiledRequestId, "Compiled request id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM IdentityStrategyBindings WHERE CompiledRequestId = $id ORDER BY ActorKey;";
        command.Parameters.AddWithValue("$id", compiledRequestId.Trim());
        return await ReadPayloadsAsync<IdentityStrategyBinding>(command, cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; " + SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<CharacterLoraDataset?> GetDatasetAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraDatasets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return DeserializeOrNull<CharacterLoraDataset>(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<CharacterLoraTrainingProfile?> ReadTrainingProfileAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraTrainingProfiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return DeserializeOrNull<CharacterLoraTrainingProfile>(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<CharacterLoraTrainingJob?> ReadTrainingJobAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraTrainingJobs WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return DeserializeOrNull<CharacterLoraTrainingJob>(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<CharacterLoraTrainingAttempt> RequireAttemptAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        Require(id, "LoRA training attempt id");
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraTrainingAttempts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Trim());
        return DeserializeOrNull<CharacterLoraTrainingAttempt>(await command.ExecuteScalarAsync(cancellationToken))
            ?? throw new InvalidOperationException($"LoRA training attempt '{id}' was not found.");
    }

    private static async Task UpdateAttemptAsync(
        SqliteConnection connection,
        CharacterLoraTrainingAttempt attempt,
        CharacterLoraTrainingAttemptStatus expectedStatus,
        long expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CharacterLoraTrainingAttempts
            SET Status = $status, ConcurrencyVersion = $nextVersion,
                ProviderKey = $provider, ProviderRequestId = $providerRequest,
                OutputSha256 = $outputSha, PayloadJson = $payload
            WHERE Id = $id AND Status = $expected AND ConcurrencyVersion = $expectedVersion;
            """;
        command.Parameters.AddWithValue("$status", attempt.Status.ToString());
        command.Parameters.AddWithValue("$nextVersion", attempt.ConcurrencyVersion);
        command.Parameters.AddWithValue("$provider", DbValue(attempt.ProviderKey));
        command.Parameters.AddWithValue("$providerRequest", DbValue(attempt.ProviderRequestId));
        command.Parameters.AddWithValue("$outputSha", DbValue(attempt.OutputSha256));
        command.Parameters.AddWithValue("$payload", Serialize(attempt));
        command.Parameters.AddWithValue("$id", attempt.Id);
        command.Parameters.AddWithValue("$expected", expectedStatus.ToString());
        command.Parameters.AddWithValue("$expectedVersion", expectedConcurrencyVersion);
        EnsureChanged(await command.ExecuteNonQueryAsync(cancellationToken), "LoRA training attempt", attempt.Id);
    }

    private static async Task<CharacterLoraArtifact?> ReadArtifactAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraArtifacts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return DeserializeOrNull<CharacterLoraArtifact>(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task RequireQualifiedRequestCellAsync(
        SqliteConnection connection, IdentityStrategyBinding binding, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Status, p.Enabled, c.Status
            FROM CompiledMediaRequests r
            JOIN MediaCapabilityProfiles p ON p.Id = r.CapabilityProfileId
            JOIN MediaCapabilityCells c ON c.Id = r.CapabilityCellId AND c.CapabilityProfileId = p.Id
            WHERE r.Id = $request AND p.Id = $profile AND c.Id = $cell;
            """;
        command.Parameters.AddWithValue("$request", binding.CompiledRequestId.Trim());
        command.Parameters.AddWithValue("$profile", binding.CapabilityProfileId.Trim());
        command.Parameters.AddWithValue("$cell", binding.CapabilityCellId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !string.Equals(reader.GetString(0), MediaCapabilityProfileStatus.Qualified.ToString(), StringComparison.Ordinal)
            || reader.GetInt32(1) != 1
            || !string.Equals(reader.GetString(2), MediaCapabilityCellStatus.Qualified.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Identity strategy binding requires the exact enabled, qualified capability profile and cell used by the compiled request.");
        }
    }

    private static async Task<CharacterLoraDataset> RequireDraftAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string id, CancellationToken cancellationToken)
    {
        var dataset = await GetDatasetAsync(connection, transaction, id.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"LoRA dataset '{id}' was not found.");
        if (dataset.Status != CharacterLoraDatasetStatus.Draft)
            throw new InvalidOperationException($"LoRA dataset '{id}' is {dataset.Status}; only drafts can be modified.");
        return dataset;
    }

    private static async Task<IReadOnlyList<CharacterLoraDatasetMember>> ListMembersAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string datasetId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT PayloadJson FROM CharacterLoraDatasetMembers WHERE DatasetId = $id ORDER BY Ordinal;";
        command.Parameters.AddWithValue("$id", datasetId);
        return await ReadPayloadsAsync<CharacterLoraDatasetMember>(command, cancellationToken);
    }

    private static async Task RequireApprovedAssetAsync(
        SqliteConnection connection, SqliteTransaction transaction, CharacterLoraDatasetMember member,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Status, Sha256, ProductionApprovalStatus, ApprovedUseScope, ProductionVersion,
                   SourceProvenanceJson, ConsentState, LicenseState
            FROM SceneAssets WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", member.SceneAssetId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !string.Equals(reader.GetString(0), SceneAssetStatus.Complete.ToString(), StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), member.AssetSha256, StringComparison.Ordinal)
            || reader.IsDBNull(2)
            || !string.Equals(reader.GetString(2), SceneAssetProductionApprovalStatus.Approved.ToString(), StringComparison.Ordinal)
            || reader.IsDBNull(3)
            || (((SceneAssetApprovedUseScope)reader.GetInt32(3)) & SceneAssetApprovedUseScope.CharacterLoraTraining) == 0
            || reader.IsDBNull(4)
            || reader.GetInt32(4) != member.SceneAssetVersion
            || reader.IsDBNull(5)
            || string.IsNullOrWhiteSpace(reader.GetString(5))
            || reader.IsDBNull(6)
            || string.Equals(reader.GetString(6), SceneAssetConsentState.Unknown.ToString(), StringComparison.Ordinal)
            || reader.IsDBNull(7)
            || string.Equals(reader.GetString(7), SceneAssetLicenseState.Unknown.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"LoRA dataset member '{member.Id}' requires the exact version and checksum of a complete Scene Asset approved for CharacterLoraTraining.");
        }
    }

    private static async Task<ArtifactLineage> ReadArtifactLineageAsync(
        SqliteConnection connection, SqliteTransaction transaction, string attemptId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                 SELECT a.Status, a.OutputSha256, a.PayloadJson, j.DatasetId, d.CharacterProfileId,
                     d.PayloadJson, j.BaseModelId, j.BaseModelVersion, j.BaseModelSha256
            FROM CharacterLoraTrainingAttempts a
            JOIN CharacterLoraTrainingJobs j ON j.Id = a.TrainingJobId
            JOIN CharacterLoraDatasets d ON d.Id = j.DatasetId
            WHERE a.Id = $id;
            """;
        command.Parameters.AddWithValue("$id", attemptId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"LoRA training attempt '{attemptId}' was not found.");
        var attempt = Deserialize<CharacterLoraTrainingAttempt>(reader.GetString(2));
        var dataset = Deserialize<CharacterLoraDataset>(reader.GetString(5));
        return new ArtifactLineage(
            ParseEnum<CharacterLoraTrainingAttemptStatus>(reader.GetString(0), "training attempt", attemptId),
            reader.IsDBNull(1) ? null : reader.GetString(1), attempt.OutputFileRelativePath,
            reader.GetString(3), reader.GetString(4), dataset.TriggerToken,
            reader.GetString(6), reader.GetString(7), reader.GetString(8));
    }

    private static void ValidateNewDataset(CharacterLoraDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        Require(dataset.Id, "LoRA dataset id");
        Require(dataset.CharacterProfileId, "Character profile id");
        Require(dataset.IdentityPackId, "Identity pack id");
        if (dataset.Version <= 0) throw new InvalidOperationException("LoRA dataset version must be positive.");
        if (dataset.Status != CharacterLoraDatasetStatus.Draft || dataset.ManifestSha256 is not null
            || dataset.FrozenUtc is not null || dataset.FrozenBy is not null)
            throw new InvalidOperationException("A new LoRA dataset must be an unfrozen Draft without a manifest hash.");
        Require(dataset.TriggerToken, "LoRA trigger token");
        Require(dataset.TargetModelFamily, "LoRA target model family");
        RequireJson(dataset.CoveragePlanJson, "LoRA coverage plan");
        RequireJson(dataset.CurationPolicyJson, "LoRA curation policy");
        RequireUtc(dataset.CreatedUtc, "LoRA dataset creation time");
    }

    private static void ValidateNewTrainingProfile(CharacterLoraTrainingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Status != CharacterLoraTrainingProfileStatus.Draft || profile.Enabled
            || profile.QualifiedUtc is not null || !string.IsNullOrWhiteSpace(profile.QualificationEvidenceJson))
            throw new InvalidOperationException("A new LoRA training profile must be a disabled, unqualified Draft.");
        ValidateCompleteTrainingProfile(profile);
    }

    private static void ValidateCompleteTrainingProfile(CharacterLoraTrainingProfile profile)
    {
        Require(profile.Id, "LoRA training profile id");
        Require(profile.Name, "LoRA training profile name");
        if (profile.Version <= 0) throw new InvalidOperationException("LoRA training profile version must be positive.");
        Require(profile.TargetModelFamily, "LoRA training target model family");
        Require(profile.BaseModelId, "LoRA training base model id");
        Require(profile.BaseModelVersion, "LoRA training base model version");
        RequireSha256(profile.BaseModelSha256, "LoRA training base model checksum");
        Require(profile.TrainerId, "LoRA trainer id");
        Require(profile.TrainerVersion, "LoRA trainer version");
        RequireSecretFreeJsonObject(profile.RecipeJson, "LoRA training recipe");
        RequireRecipeFields(profile.RecipeJson);
        RequireSecretFreeJsonObject(profile.EnvironmentRequirementsJson, "LoRA training environment requirements");
        RequireSecretFreeJsonObject(profile.CheckpointCadenceJson, "LoRA training checkpoint cadence");
        RequireSecretFreeJsonObject(profile.SampleCadenceJson, "LoRA training sample cadence");
        RequireNonEmptyObject(profile.EnvironmentRequirementsJson, "LoRA training environment requirements");
        RequirePositiveJsonNumber(profile.CheckpointCadenceJson, "everySteps", "LoRA training checkpoint cadence");
        RequirePositiveJsonNumber(profile.SampleCadenceJson, "everySteps", "LoRA training sample cadence");
        RequireUtc(profile.CreatedUtc, "LoRA training profile creation time");
    }

    private static void ValidateMember(CharacterLoraDatasetMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        Require(member.Id, "LoRA dataset member id");
        Require(member.DatasetId, "LoRA dataset id");
        if (member.Ordinal < 0) throw new InvalidOperationException("LoRA dataset member ordinal cannot be negative.");
        Require(member.SceneAssetId, "LoRA dataset member Scene Asset id");
        if (member.SceneAssetVersion <= 0) throw new InvalidOperationException("LoRA dataset member Scene Asset version must be positive.");
        RequireSha256(member.AssetSha256, "LoRA dataset member asset checksum");
        RequireEnum(member.Role, "LoRA dataset member role");
        RequireEnum(member.Split, "LoRA dataset member split");
        Require(member.Caption, "LoRA dataset member caption");
        if (member.CaptionRevision <= 0) throw new InvalidOperationException("LoRA dataset member caption revision must be positive.");
        RequireJson(member.CoverageJson, "LoRA dataset member coverage");
        Require(member.GenerationAttemptId, "LoRA dataset member generation attempt id");
        RequireEnum(member.CurationStatus, "LoRA dataset member curation status");
        RequireJson(member.CurationFindingsJson, "LoRA dataset member curation findings");
        if (member.CurationStatus != CharacterLoraCurationStatus.Pending)
        {
            Require(member.ReviewedBy, "LoRA dataset member reviewer");
            if (member.ReviewedUtc is null) throw new InvalidOperationException("A curated LoRA member requires a review time.");
            RequireUtc(member.ReviewedUtc.Value, "LoRA dataset member review time");
        }
    }

    private static void ValidateFreezeMembers(IReadOnlyList<CharacterLoraDatasetMember> members)
    {
        if (members.Count == 0) throw new InvalidOperationException("A LoRA dataset cannot be frozen without members.");
        if (members.Any(member => member.CurationStatus != CharacterLoraCurationStatus.Accepted))
            throw new InvalidOperationException("Every frozen LoRA dataset member must be explicitly accepted by curation.");
        if (!members.Any(member => member.Role == CharacterLoraDatasetMemberRole.IdentitySeed))
            throw new InvalidOperationException("A frozen LoRA dataset requires an accepted identity seed member.");
        if (!members.Any(member => member.Split == CharacterLoraDatasetSplit.Train)
            || !members.Any(member => member.Split == CharacterLoraDatasetSplit.Validation))
            throw new InvalidOperationException("A frozen LoRA dataset requires explicit train and validation members.");
    }

    private static void ValidateNewJob(CharacterLoraTrainingJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Require(job.Id, "LoRA training job id");
        Require(job.DatasetId, "LoRA training dataset id");
        Require(job.TrainingProfileId, "LoRA training profile id");
        Require(job.BaseModelId, "LoRA base model id");
        Require(job.BaseModelVersion, "LoRA base model version");
        RequireSha256(job.BaseModelSha256, "LoRA base model checksum");
        Require(job.TrainerId, "LoRA trainer id");
        Require(job.TrainerVersion, "LoRA trainer version");
        RequireSecretFreeJson(job.RecipeJson, "LoRA training recipe");
        RequireSecretFreeJson(job.EnvironmentManifestJson, "LoRA training environment manifest");
        if (job.Status != CharacterLoraTrainingJobStatus.Draft || job.ConcurrencyVersion != 1)
            throw new InvalidOperationException("A new LoRA training job must be Draft at concurrency version 1.");
        RequireUtc(job.CreatedUtc, "LoRA training job creation time");
    }

    private static void RequireExactQualifiedTrainingProfile(
        CharacterLoraTrainingJob job,
        CharacterLoraDataset dataset,
        CharacterLoraTrainingProfile profile)
    {
        if (profile.Status != CharacterLoraTrainingProfileStatus.Qualified || !profile.Enabled
            || profile.QualifiedUtc is null)
            throw new InvalidOperationException($"LoRA training profile '{profile.Id}' is not enabled and qualified.");
        if (!string.Equals(dataset.TargetModelFamily, profile.TargetModelFamily, StringComparison.Ordinal)
            || !string.Equals(job.BaseModelId, profile.BaseModelId, StringComparison.Ordinal)
            || !string.Equals(job.BaseModelVersion, profile.BaseModelVersion, StringComparison.Ordinal)
            || !string.Equals(job.BaseModelSha256, profile.BaseModelSha256, StringComparison.Ordinal)
            || !string.Equals(job.TrainerId, profile.TrainerId, StringComparison.Ordinal)
            || !string.Equals(job.TrainerVersion, profile.TrainerVersion, StringComparison.Ordinal)
            || !JsonEquals(job.RecipeJson, profile.RecipeJson)
            || !JsonEquals(job.EnvironmentManifestJson, profile.EnvironmentRequirementsJson))
        {
            throw new InvalidOperationException(
                $"LoRA training job '{job.Id}' must exactly match qualified training profile '{profile.Id}' version {profile.Version}.");
        }
    }

    private static void ValidateNewAttempt(CharacterLoraTrainingAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        Require(attempt.Id, "LoRA training attempt id");
        Require(attempt.TrainingJobId, "LoRA training job id");
        if (attempt.AttemptNumber <= 0) throw new InvalidOperationException("LoRA training attempt number must be positive.");
        if (attempt.Status != CharacterLoraTrainingAttemptStatus.Pending || attempt.ConcurrencyVersion != 1)
            throw new InvalidOperationException("A new LoRA training attempt must be Pending at concurrency version 1.");
        RequireSecretFreeJson(attempt.RequestSnapshotJson, "LoRA training request snapshot");
        RequireUtc(attempt.CreatedUtc, "LoRA training attempt creation time");
    }

    private static void ValidateNewArtifact(CharacterLoraArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        Require(artifact.Id, "LoRA artifact id");
        Require(artifact.CharacterProfileId, "LoRA artifact character profile id");
        Require(artifact.DatasetId, "LoRA artifact dataset id");
        Require(artifact.TrainingAttemptId, "LoRA artifact training attempt id");
        if (artifact.Version <= 0) throw new InvalidOperationException("LoRA artifact version must be positive.");
        Require(artifact.BaseModelId, "LoRA artifact base model id");
        Require(artifact.BaseModelVersion, "LoRA artifact base model version");
        RequireSha256(artifact.BaseModelSha256, "LoRA artifact base model checksum");
        Require(artifact.TriggerToken, "LoRA artifact trigger token");
        Require(artifact.FileRelativePath, "LoRA artifact file path");
        RequireSha256(artifact.Sha256, "LoRA artifact checksum");
        RequireJson(artifact.TrainingManifestJson, "LoRA artifact training manifest");
        if (artifact.Status != CharacterLoraArtifactStatus.Candidate || artifact.QualifiedUtc is not null)
            throw new InvalidOperationException("A new LoRA artifact must be an unqualified Candidate.");
        RequireUtc(artifact.CreatedUtc, "LoRA artifact creation time");
    }

    private static void ValidateStrategyBinding(IdentityStrategyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Require(binding.Id, "Identity strategy binding id");
        Require(binding.CompiledRequestId, "Identity strategy compiled request id");
        Require(binding.ActorKey, "Identity strategy actor key");
        RequireEnum(binding.StrategyKind, "Identity strategy kind");
        Require(binding.CapabilityProfileId, "Identity strategy capability profile id");
        Require(binding.CapabilityCellId, "Identity strategy capability cell id");
        RequireJson(binding.BindingSnapshotJson, "Identity strategy snapshot");
        var usesReferences = binding.StrategyKind is CharacterIdentityStrategyKind.ReferenceConditioning or CharacterIdentityStrategyKind.Combined;
        var usesLora = binding.StrategyKind is CharacterIdentityStrategyKind.Lora or CharacterIdentityStrategyKind.Combined;
        if (usesReferences) RequireJson(binding.ReferenceBindingsJson!, "Identity strategy reference bindings");
        else if (binding.ReferenceBindingsJson is not null) throw new InvalidOperationException("LoRA-only identity strategy cannot contain reference bindings.");
        if (usesLora)
        {
            Require(binding.LoraArtifactId, "Identity strategy LoRA artifact id");
            RequireSha256(binding.LoraArtifactSha256!, "Identity strategy LoRA artifact checksum");
            if (binding.LoraStrength is null or <= 0) throw new InvalidOperationException("LoRA identity strategy requires an explicit positive strength.");
        }
        else if (binding.LoraArtifactId is not null || binding.LoraArtifactSha256 is not null || binding.LoraStrength is not null)
            throw new InvalidOperationException("Reference-only identity strategy cannot contain a LoRA binding.");
        RequireUtc(binding.CreatedUtc, "Identity strategy binding creation time");
    }

    private static bool IsAllowed(CharacterLoraTrainingJobStatus current, CharacterLoraTrainingJobStatus next) =>
        (current, next) switch
        {
            (CharacterLoraTrainingJobStatus.Draft, CharacterLoraTrainingJobStatus.Ready or CharacterLoraTrainingJobStatus.Cancelled) => true,
            (CharacterLoraTrainingJobStatus.Ready, CharacterLoraTrainingJobStatus.Queued or CharacterLoraTrainingJobStatus.Cancelled) => true,
            (CharacterLoraTrainingJobStatus.Queued, CharacterLoraTrainingJobStatus.Running or CharacterLoraTrainingJobStatus.Failed or CharacterLoraTrainingJobStatus.Cancelled) => true,
            (CharacterLoraTrainingJobStatus.Running, CharacterLoraTrainingJobStatus.Succeeded or CharacterLoraTrainingJobStatus.Failed or CharacterLoraTrainingJobStatus.Cancelled) => true,
            _ => false
        };

    private static bool IsAllowed(CharacterLoraTrainingAttemptStatus current, CharacterLoraTrainingAttemptStatus next) =>
        (current, next) switch
        {
            (CharacterLoraTrainingAttemptStatus.Submitted, CharacterLoraTrainingAttemptStatus.Running
                or CharacterLoraTrainingAttemptStatus.Failed or CharacterLoraTrainingAttemptStatus.Cancelled
                or CharacterLoraTrainingAttemptStatus.Indeterminate) => true,
            (CharacterLoraTrainingAttemptStatus.Running, CharacterLoraTrainingAttemptStatus.Failed
                or CharacterLoraTrainingAttemptStatus.Cancelled or CharacterLoraTrainingAttemptStatus.Indeterminate) => true,
            _ => false
        };

    private static async Task<IReadOnlyList<T>> ReadPayloadsAsync<T>(SqliteCommand command, CancellationToken cancellationToken)
    {
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) values.Add(Deserialize<T>(reader.GetString(0)));
        return values;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, SerializerOptions)
        ?? throw new InvalidOperationException($"Persisted {typeof(T).Name} payload was null.");
    private static T? DeserializeOrNull<T>(object? value) => value is string json ? Deserialize<T>(json) : default;
    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static void RequireSecretFreeJson(string value, string label)
    {
        RequireJson(value, label);
        using var document = JsonDocument.Parse(value);
        InspectForSecrets(document.RootElement, label);
    }

    private static void RequireSecretFreeJsonObject(string value, string label)
    {
        RequireJsonObject(value, label);
        using var document = JsonDocument.Parse(value);
        InspectForSecrets(document.RootElement, label);
    }

    private static void RequireJsonObject(string value, string label)
    {
        RequireJson(value, label);
        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{label} must be a JSON object.");
    }

    private static void RequireRecipeFields(string recipeJson)
    {
        using var document = JsonDocument.Parse(recipeJson);
        var recipe = document.RootElement;
        foreach (var name in new[] { "imageCount", "repeats", "rank", "alpha", "unetLearningRate", "textEncoderLearningRate", "steps", "epochs" })
            RequirePositiveJsonNumber(recipe, name, "LoRA training recipe");
        RequireJsonNumberInRange(recipe, "captionDropout", 0, 1, "LoRA training recipe");
        RequireJsonKind(recipe, "priorPreservation", JsonValueKind.True, JsonValueKind.False, "LoRA training recipe");
        RequireNonEmptyJsonObject(recipe, "coverage", "LoRA training recipe");
        RequireNonEmptyJsonArray(recipe, "resolutionBuckets", "LoRA training recipe");
        RequireNonEmptyJsonString(recipe, "precision", "LoRA training recipe");
    }

    private static void RequirePositiveJsonNumber(string json, string propertyName, string label)
    {
        using var document = JsonDocument.Parse(json);
        RequirePositiveJsonNumber(document.RootElement, propertyName, label);
    }

    private static void RequirePositiveJsonNumber(JsonElement root, string propertyName, string label)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number)
            || number <= 0)
            throw new InvalidOperationException($"{label} requires explicit positive '{propertyName}'.");
    }

    private static void RequireJsonNumberInRange(
        JsonElement root, string propertyName, double minimum, double maximum, string label)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number)
            || number < minimum || number > maximum)
            throw new InvalidOperationException($"{label} requires '{propertyName}' between {minimum} and {maximum}.");
    }

    private static void RequireJsonKind(
        JsonElement root, string propertyName, JsonValueKind first, JsonValueKind second, string label)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || (value.ValueKind != first && value.ValueKind != second))
            throw new InvalidOperationException($"{label} requires explicit Boolean '{propertyName}'.");
    }

    private static void RequireNonEmptyJsonObject(JsonElement root, string propertyName, string label)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Object
            || !value.EnumerateObject().Any())
            throw new InvalidOperationException($"{label} requires non-empty object '{propertyName}'.");
    }

    private static void RequireNonEmptyJsonArray(JsonElement root, string propertyName, string label)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() == 0)
            throw new InvalidOperationException($"{label} requires non-empty array '{propertyName}'.");
    }

    private static void RequireNonEmptyJsonString(JsonElement root, string propertyName, string label)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"{label} requires non-empty string '{propertyName}'.");
    }

    private static void RequireNonEmptyObject(string json, string label)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.EnumerateObject().Any())
            throw new InvalidOperationException($"{label} cannot be empty.");
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
                    throw new InvalidOperationException($"{label} cannot contain secret field '{property.Name}'.");
                InspectForSecrets(property.Value, label);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) InspectForSecrets(item, label);
    }

    private static void RequireJson(string value, string label)
    {
        Require(value, label);
        try { using var _ = JsonDocument.Parse(value); }
        catch (JsonException exception) { throw new InvalidOperationException($"{label} must be valid JSON.", exception); }
    }

    private static bool JsonEquals(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
    }

    private static void RequireSha256(string value, string label)
    {
        Require(value, label);
        if (value.Length != 64 || !value.All(char.IsAsciiHexDigit))
            throw new InvalidOperationException($"{label} must be a 64-character hexadecimal value.");
    }

    private static void RequireEnum<TEnum>(TEnum value, string label) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new InvalidOperationException($"{label} must be explicit.");
    }

    private static TEnum ParseEnum<TEnum>(string value, string label, string id) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new InvalidOperationException($"Invalid {typeof(TEnum).Name} '{value}' for {label} '{id}'.");
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

    private static void EnsureChanged(int count, string label, string id)
    {
        if (count != 1) throw new InvalidOperationException($"{label} '{id}' changed before the operation completed.");
    }

    private sealed record ArtifactLineage(
        CharacterLoraTrainingAttemptStatus AttemptStatus,
        string? OutputSha256,
        string? OutputFileRelativePath,
        string DatasetId,
        string CharacterProfileId,
        string TriggerToken,
        string BaseModelId,
        string BaseModelVersion,
        string BaseModelSha256);

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS CharacterLoraTrainingProfiles (
            Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Version INTEGER NOT NULL CHECK (Version > 0),
            Status TEXT NOT NULL, Enabled INTEGER NOT NULL CHECK (Enabled IN (0, 1)),
            TargetModelFamily TEXT NOT NULL, BaseModelId TEXT NOT NULL,
            BaseModelVersion TEXT NOT NULL, BaseModelSha256 TEXT NOT NULL,
            TrainerId TEXT NOT NULL, TrainerVersion TEXT NOT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL, QualifiedUtc TEXT NULL,
            UNIQUE (Name, Version)
        );
        CREATE INDEX IF NOT EXISTS IX_CharacterLoraTrainingProfiles_Qualified
            ON CharacterLoraTrainingProfiles (TargetModelFamily, Status, Enabled, Version);

        CREATE TABLE IF NOT EXISTS CharacterLoraDatasets (
            Id TEXT PRIMARY KEY, CharacterProfileId TEXT NOT NULL, IdentityPackId TEXT NOT NULL,
            Version INTEGER NOT NULL CHECK (Version > 0), Status TEXT NOT NULL,
            TargetModelFamily TEXT NOT NULL, ManifestSha256 TEXT NULL, SupersedesId TEXT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL, FrozenUtc TEXT NULL,
            FOREIGN KEY (SupersedesId) REFERENCES CharacterLoraDatasets(Id) ON DELETE RESTRICT,
            UNIQUE (CharacterProfileId, TargetModelFamily, Version)
        );
        CREATE INDEX IF NOT EXISTS IX_CharacterLoraDatasets_Character
            ON CharacterLoraDatasets (CharacterProfileId, TargetModelFamily, Status, Version);

        CREATE TABLE IF NOT EXISTS CharacterLoraDatasetMembers (
            Id TEXT PRIMARY KEY, DatasetId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
            SceneAssetId TEXT NOT NULL, SceneAssetVersion INTEGER NOT NULL CHECK (SceneAssetVersion > 0),
            AssetSha256 TEXT NOT NULL, Role TEXT NOT NULL, Split TEXT NOT NULL,
            CurationStatus TEXT NOT NULL, PayloadJson TEXT NOT NULL,
            FOREIGN KEY (DatasetId) REFERENCES CharacterLoraDatasets(Id) ON DELETE RESTRICT,
            FOREIGN KEY (SceneAssetId) REFERENCES SceneAssets(Id) ON DELETE RESTRICT,
            UNIQUE (DatasetId, Ordinal), UNIQUE (DatasetId, SceneAssetId)
        );
        CREATE INDEX IF NOT EXISTS IX_CharacterLoraDatasetMembers_Asset
            ON CharacterLoraDatasetMembers (SceneAssetId);

        CREATE TABLE IF NOT EXISTS CharacterLoraTrainingJobs (
            Id TEXT PRIMARY KEY, DatasetId TEXT NOT NULL, TrainingProfileId TEXT NOT NULL,
            BaseModelId TEXT NOT NULL, BaseModelVersion TEXT NOT NULL, BaseModelSha256 TEXT NOT NULL,
            TrainerId TEXT NOT NULL, TrainerVersion TEXT NOT NULL, Status TEXT NOT NULL,
            ConcurrencyVersion INTEGER NOT NULL CHECK (ConcurrencyVersion > 0),
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (DatasetId) REFERENCES CharacterLoraDatasets(Id) ON DELETE RESTRICT
        );
        CREATE INDEX IF NOT EXISTS IX_CharacterLoraTrainingJobs_Dataset
            ON CharacterLoraTrainingJobs (DatasetId, Status, CreatedUtc);

        CREATE TABLE IF NOT EXISTS CharacterLoraTrainingAttempts (
            Id TEXT PRIMARY KEY, TrainingJobId TEXT NOT NULL,
            AttemptNumber INTEGER NOT NULL CHECK (AttemptNumber > 0), Status TEXT NOT NULL,
            ConcurrencyVersion INTEGER NOT NULL CHECK (ConcurrencyVersion > 0),
            ProviderKey TEXT NULL, ProviderRequestId TEXT NULL, OutputSha256 TEXT NULL,
            PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (TrainingJobId) REFERENCES CharacterLoraTrainingJobs(Id) ON DELETE RESTRICT,
            UNIQUE (TrainingJobId, AttemptNumber), UNIQUE (ProviderKey, ProviderRequestId)
        );
        CREATE INDEX IF NOT EXISTS IX_CharacterLoraTrainingAttempts_Job
            ON CharacterLoraTrainingAttempts (TrainingJobId, AttemptNumber);

        CREATE TABLE IF NOT EXISTS CharacterLoraArtifacts (
            Id TEXT PRIMARY KEY, CharacterProfileId TEXT NOT NULL, DatasetId TEXT NOT NULL,
            TrainingAttemptId TEXT NOT NULL UNIQUE, Version INTEGER NOT NULL CHECK (Version > 0),
            BaseModelId TEXT NOT NULL, BaseModelVersion TEXT NOT NULL, BaseModelSha256 TEXT NOT NULL,
            Status TEXT NOT NULL, Sha256 TEXT NOT NULL, PayloadJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (DatasetId) REFERENCES CharacterLoraDatasets(Id) ON DELETE RESTRICT,
            FOREIGN KEY (TrainingAttemptId) REFERENCES CharacterLoraTrainingAttempts(Id) ON DELETE RESTRICT,
            UNIQUE (CharacterProfileId, BaseModelId, BaseModelVersion, Version)
        );
        CREATE INDEX IF NOT EXISTS IX_CharacterLoraArtifacts_CharacterStatus
            ON CharacterLoraArtifacts (CharacterProfileId, Status, Version);

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
        """;
}
