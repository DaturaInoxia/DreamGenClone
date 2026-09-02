using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneMomentSetRepository : ISceneMomentSetRepository
{
    private readonly string _connectionString;

    public SceneMomentSetRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateVersionAsync(
        SceneMomentSet momentSet,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ValidateNewVersion(momentSet, attempt);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (momentSet.Version == 0)
        {
            await using var allocate = connection.CreateCommand();
            allocate.Transaction = (SqliteTransaction)transaction;
            allocate.CommandText = "SELECT COALESCE(MAX(Version), 0) + 1 FROM SceneMomentSets WHERE BeatProductionPlanId = $planId;";
            allocate.Parameters.AddWithValue("$planId", momentSet.BeatProductionPlanId.Trim());
            momentSet.Version = Convert.ToInt32(await allocate.ExecuteScalarAsync(cancellationToken));
        }

        await using (var supersedeSets = connection.CreateCommand())
        {
            supersedeSets.Transaction = (SqliteTransaction)transaction;
            supersedeSets.CommandText = """
                UPDATE SceneMomentSets SET Status = 'Superseded', UpdatedUtc = $updatedUtc
                WHERE BeatProductionPlanId = $planId AND Status NOT IN ('Superseded', 'Cancelled');
                """;
            supersedeSets.Parameters.AddWithValue("$planId", momentSet.BeatProductionPlanId.Trim());
            supersedeSets.Parameters.AddWithValue("$updatedUtc", FormatUtc(momentSet.CreatedUtc));
            await supersedeSets.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var supersedeAttempts = connection.CreateCommand())
        {
            supersedeAttempts.Transaction = (SqliteTransaction)transaction;
            supersedeAttempts.CommandText = """
                UPDATE SceneMomentDiscoveryAttempts SET Status = 'Superseded', UpdatedUtc = $updatedUtc
                WHERE OwnerRecordId IN (
                    SELECT Id FROM SceneMomentSets
                    WHERE BeatProductionPlanId = $planId AND Status = 'Superseded')
                  AND Status IN ('Queued', 'Processing');
                """;
            supersedeAttempts.Parameters.AddWithValue("$planId", momentSet.BeatProductionPlanId.Trim());
            supersedeAttempts.Parameters.AddWithValue("$updatedUtc", FormatUtc(momentSet.CreatedUtc));
            await supersedeAttempts.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertSet = connection.CreateCommand())
        {
            insertSet.Transaction = (SqliteTransaction)transaction;
            insertSet.CommandText = """
                INSERT INTO SceneMomentSets (
                    Id, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                    Version, Status, CurrentAttemptId, RecommendedMomentId, SchemaVersion,
                    PromptContractVersion, BeatSnapshotJson, TurnEvidenceSnapshotJson,
                    ModelIdentifier, ProviderName, ExecutionSettingsJson, ErrorCode, ErrorMessage,
                    CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
                VALUES (
                    $id, $catalogueId, $beatId, $planId, $planVersion,
                    $version, 'Pending', $attemptId, NULL, $schemaVersion,
                    $promptContractVersion, $beatSnapshot, $evidenceSnapshot,
                    NULL, NULL, $settings, NULL, NULL,
                    $createdUtc, NULL, NULL, $createdUtc);
                """;
            insertSet.Parameters.AddWithValue("$id", momentSet.Id.Trim());
            insertSet.Parameters.AddWithValue("$catalogueId", momentSet.CatalogueId.Trim());
            insertSet.Parameters.AddWithValue("$beatId", momentSet.BeatId.Trim());
            insertSet.Parameters.AddWithValue("$planId", momentSet.BeatProductionPlanId.Trim());
            insertSet.Parameters.AddWithValue("$planVersion", momentSet.BeatProductionPlanVersion);
            insertSet.Parameters.AddWithValue("$version", momentSet.Version);
            insertSet.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
            insertSet.Parameters.AddWithValue("$schemaVersion", momentSet.SchemaVersion);
            insertSet.Parameters.AddWithValue("$promptContractVersion", momentSet.PromptContractVersion.Trim());
            insertSet.Parameters.AddWithValue("$beatSnapshot", momentSet.BeatSnapshotJson);
            insertSet.Parameters.AddWithValue("$evidenceSnapshot", momentSet.TurnEvidenceSnapshotJson);
            insertSet.Parameters.AddWithValue("$settings", momentSet.ExecutionSettingsJson);
            insertSet.Parameters.AddWithValue("$createdUtc", FormatUtc(momentSet.CreatedUtc));
            await insertSet.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAttemptAsync(connection, (SqliteTransaction)transaction, attempt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SceneMomentSet?> GetAsync(
        string momentSetId,
        CancellationToken cancellationToken = default)
    {
        Require(momentSetId, "Moment Set id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateSetSelect(connection);
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", momentSetId.Trim());
        return await ReadSetAsync(connection, command, cancellationToken);
    }

    public async Task<SceneMomentSet?> GetCurrentAsync(
        string beatProductionPlanId,
        CancellationToken cancellationToken = default)
    {
        Require(beatProductionPlanId, "Beat Production Plan id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateSetSelect(connection);
        command.CommandText += """
             WHERE BeatProductionPlanId = $planId AND Status NOT IN ('Superseded', 'Cancelled')
             ORDER BY Version DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$planId", beatProductionPlanId.Trim());
        return await ReadSetAsync(connection, command, cancellationToken);
    }

    public async Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = AttemptSelect + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", attemptId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    public Task<bool> TryStartAttemptAsync(
        string momentSetId,
        string attemptId,
        string modelIdentifier,
        string providerName,
        DateTime startedUtc,
        CancellationToken cancellationToken = default)
    {
        Require(modelIdentifier, "Model identifier");
        Require(providerName, "Provider name");
        return TransitionAsync(
            momentSetId, attemptId, "Processing", "Processing", startedUtc,
            "Status = 'Pending'", "Status = 'Queued'",
            (set, attempt) =>
            {
                set.CommandText += ", ModelIdentifier = $model, ProviderName = $provider, StartedUtc = $startedUtc";
                set.Parameters.AddWithValue("$model", modelIdentifier.Trim());
                set.Parameters.AddWithValue("$provider", providerName.Trim());
                set.Parameters.AddWithValue("$startedUtc", FormatUtc(startedUtc));
                attempt.CommandText += ", StartedUtc = $startedUtc";
                attempt.Parameters.AddWithValue("$startedUtc", FormatUtc(startedUtc));
            }, cancellationToken);
    }

    public async Task<bool> TryCompleteAttemptAsync(
        string momentSetId,
        SceneBeatAnalysisAttempt attempt,
        SceneMomentSetData data,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalAttempt(momentSetId, attempt);
        ValidateData(momentSetId, data);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await OwnsAsync(connection, (SqliteTransaction)transaction, momentSetId, attempt.Id, "Processing", "Processing", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        foreach (var moment in data.Moments)
        {
            await using var insertMoment = connection.CreateCommand();
            insertMoment.Transaction = (SqliteTransaction)transaction;
            insertMoment.CommandText = """
                INSERT INTO SceneMoments (
                    MomentSetId, MomentId, ItemOrder, Label, TemporalAnchor, FrozenState,
                    VisibleAction, ParticipantSummaryJson, CompositionRationale,
                    ProductionRolesJson, EvidenceInteractionIdsJson)
                VALUES ($setId, $momentId, $order, $label, $anchor, $state,
                    $action, $participants, $rationale, $roles, $evidence);
                """;
            insertMoment.Parameters.AddWithValue("$setId", momentSetId.Trim());
            insertMoment.Parameters.AddWithValue("$momentId", moment.MomentId.Trim());
            insertMoment.Parameters.AddWithValue("$order", moment.Order);
            insertMoment.Parameters.AddWithValue("$label", moment.Label.Trim());
            insertMoment.Parameters.AddWithValue("$anchor", moment.TemporalAnchor.Trim());
            insertMoment.Parameters.AddWithValue("$state", moment.FrozenState.Trim());
            insertMoment.Parameters.AddWithValue("$action", moment.VisibleAction.Trim());
            insertMoment.Parameters.AddWithValue("$participants", moment.ParticipantSummaryJson);
            insertMoment.Parameters.AddWithValue("$rationale", moment.CompositionRationale.Trim());
            insertMoment.Parameters.AddWithValue("$roles", moment.ProductionRolesJson);
            insertMoment.Parameters.AddWithValue("$evidence", moment.EvidenceInteractionIdsJson);
            await insertMoment.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateAttempt = connection.CreateCommand())
        {
            updateAttempt.Transaction = (SqliteTransaction)transaction;
            updateAttempt.CommandText = """
                UPDATE SceneMomentDiscoveryAttempts SET Status = 'Complete', RawModelResponse = $raw,
                    ReasoningContent = $reasoning, FinishReason = $finishReason, ValidationCode = NULL,
                    ValidationDetailsJson = $validationDetails, DurationMs = $durationMs,
                    OutputCharacters = $outputCharacters, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $attemptId AND OwnerRecordId = $setId AND Status = 'Processing';
                """;
            AddAttemptResultParameters(updateAttempt, momentSetId, attempt, completedUtc);
            if (await updateAttempt.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var updateSet = connection.CreateCommand())
        {
            updateSet.Transaction = (SqliteTransaction)transaction;
            updateSet.CommandText = """
                UPDATE SceneMomentSets SET Status = 'Complete', RecommendedMomentId = $recommendedId,
                    ErrorCode = NULL, ErrorMessage = NULL, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $setId AND CurrentAttemptId = $attemptId AND Status = 'Processing';
                """;
            updateSet.Parameters.AddWithValue("$setId", momentSetId.Trim());
            updateSet.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
            updateSet.Parameters.AddWithValue("$recommendedId", data.RecommendedMomentId.Trim());
            updateSet.Parameters.AddWithValue("$completedUtc", FormatUtc(completedUtc));
            if (await updateSet.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryFailAttemptAsync(
        string momentSetId,
        SceneBeatAnalysisAttempt attempt,
        string errorCode,
        string errorMessage,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalAttempt(momentSetId, attempt);
        Require(errorCode, "Error code");
        Require(errorMessage, "Error message");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await OwnsAsync(connection, (SqliteTransaction)transaction, momentSetId, attempt.Id, "Pending', 'Processing", "Queued', 'Processing", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updateAttempt = connection.CreateCommand();
        updateAttempt.Transaction = (SqliteTransaction)transaction;
        updateAttempt.CommandText = """
            UPDATE SceneMomentDiscoveryAttempts SET Status = 'Failed', RawModelResponse = $raw,
                ReasoningContent = $reasoning, FinishReason = $finishReason, ValidationCode = $validationCode,
                ValidationDetailsJson = $validationDetails, DurationMs = $durationMs,
                OutputCharacters = $outputCharacters, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
            WHERE Id = $attemptId AND OwnerRecordId = $setId AND Status IN ('Queued', 'Processing');
            """;
        AddAttemptResultParameters(updateAttempt, momentSetId, attempt, completedUtc);
        if (await updateAttempt.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updateSet = connection.CreateCommand();
        updateSet.Transaction = (SqliteTransaction)transaction;
        updateSet.CommandText = """
            UPDATE SceneMomentSets SET Status = 'Failed', ErrorCode = $errorCode,
                ErrorMessage = $errorMessage, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
            WHERE Id = $setId AND CurrentAttemptId = $attemptId AND Status IN ('Pending', 'Processing');
            """;
        updateSet.Parameters.AddWithValue("$setId", momentSetId.Trim());
        updateSet.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
        updateSet.Parameters.AddWithValue("$errorCode", errorCode.Trim());
        updateSet.Parameters.AddWithValue("$errorMessage", errorMessage.Trim());
        updateSet.Parameters.AddWithValue("$completedUtc", FormatUtc(completedUtc));
        if (await updateSet.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> TryCancelCurrentAsync(
        string momentSetId,
        string attemptId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            momentSetId, attemptId, "Cancelled", "Cancelled", cancelledUtc,
            "Status IN ('Pending', 'Processing')", "Status IN ('Queued', 'Processing')",
            (set, attempt) =>
            {
                set.CommandText += ", CompletedUtc = $completedUtc";
                attempt.CommandText += ", CompletedUtc = $completedUtc";
                set.Parameters.AddWithValue("$completedUtc", FormatUtc(cancelledUtc));
                attempt.Parameters.AddWithValue("$completedUtc", FormatUtc(cancelledUtc));
            }, cancellationToken);

    private async Task<bool> TransitionAsync(
        string momentSetId,
        string attemptId,
        string nextSetStatus,
        string nextAttemptStatus,
        DateTime updatedUtc,
        string setPredicate,
        string attemptPredicate,
        Action<SqliteCommand, SqliteCommand> decorate,
        CancellationToken cancellationToken)
    {
        Require(momentSetId, "Moment Set id");
        Require(attemptId, "Attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var updateSet = connection.CreateCommand();
        await using var updateAttempt = connection.CreateCommand();
        updateSet.Transaction = (SqliteTransaction)transaction;
        updateAttempt.Transaction = (SqliteTransaction)transaction;
        updateSet.CommandText = "UPDATE SceneMomentSets SET Status = $nextStatus, UpdatedUtc = $updatedUtc";
        updateAttempt.CommandText = "UPDATE SceneMomentDiscoveryAttempts SET Status = $nextAttemptStatus, UpdatedUtc = $updatedUtc";
        decorate(updateSet, updateAttempt);
        updateSet.CommandText += $" WHERE Id = $setId AND CurrentAttemptId = $attemptId AND {setPredicate};";
        updateAttempt.CommandText += $" WHERE Id = $attemptId AND OwnerRecordId = $setId AND {attemptPredicate};";
        foreach (var command in new[] { updateSet, updateAttempt })
        {
            command.Parameters.AddWithValue("$setId", momentSetId.Trim());
            command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
            command.Parameters.AddWithValue("$updatedUtc", FormatUtc(updatedUtc));
        }
        updateSet.Parameters.AddWithValue("$nextStatus", nextSetStatus);
        updateAttempt.Parameters.AddWithValue("$nextAttemptStatus", nextAttemptStatus);
        if (await updateSet.ExecuteNonQueryAsync(cancellationToken) != 1
            || await updateAttempt.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static SqliteCommand CreateSetSelect(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                   Version, Status, CurrentAttemptId, RecommendedMomentId, SchemaVersion,
                   PromptContractVersion, BeatSnapshotJson, TurnEvidenceSnapshotJson,
                   ModelIdentifier, ProviderName, ExecutionSettingsJson, ErrorCode, ErrorMessage,
                   CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM SceneMomentSets
            """;
        return command;
    }

    private static async Task<SceneMomentSet?> ReadSetAsync(
        SqliteConnection connection,
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var set = new SceneMomentSet
        {
            Id = reader.GetString(0),
            CatalogueId = reader.GetString(1),
            BeatId = reader.GetString(2),
            BeatProductionPlanId = reader.GetString(3),
            BeatProductionPlanVersion = reader.GetInt32(4),
            Version = reader.GetInt32(5),
            Status = Enum.Parse<SceneBeatCatalogueStatus>(reader.GetString(6)),
            CurrentAttemptId = reader.IsDBNull(7) ? null : reader.GetString(7),
            RecommendedMomentId = reader.IsDBNull(8) ? null : reader.GetString(8),
            SchemaVersion = reader.GetInt32(9),
            PromptContractVersion = reader.GetString(10),
            BeatSnapshotJson = reader.GetString(11),
            TurnEvidenceSnapshotJson = reader.GetString(12),
            ModelIdentifier = reader.IsDBNull(13) ? null : reader.GetString(13),
            ProviderName = reader.IsDBNull(14) ? null : reader.GetString(14),
            ExecutionSettingsJson = reader.GetString(15),
            ErrorCode = reader.IsDBNull(16) ? null : reader.GetString(16),
            ErrorMessage = reader.IsDBNull(17) ? null : reader.GetString(17),
            CreatedUtc = ParseUtc(reader.GetString(18)),
            StartedUtc = reader.IsDBNull(19) ? null : ParseUtc(reader.GetString(19)),
            CompletedUtc = reader.IsDBNull(20) ? null : ParseUtc(reader.GetString(20)),
            UpdatedUtc = ParseUtc(reader.GetString(21))
        };
        await reader.DisposeAsync();
        set.Moments = await LoadMomentsAsync(connection, set.Id, cancellationToken);
        return set;
    }

    private static async Task<IReadOnlyList<SceneMoment>> LoadMomentsAsync(
        SqliteConnection connection,
        string momentSetId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MomentSetId, MomentId, ItemOrder, Label, TemporalAnchor, FrozenState,
                   VisibleAction, ParticipantSummaryJson, CompositionRationale,
                   ProductionRolesJson, EvidenceInteractionIdsJson
            FROM SceneMoments WHERE MomentSetId = $setId ORDER BY ItemOrder;
            """;
        command.Parameters.AddWithValue("$setId", momentSetId);
        var moments = new List<SceneMoment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            moments.Add(new SceneMoment
            {
                MomentSetId = reader.GetString(0),
                MomentId = reader.GetString(1),
                Order = reader.GetInt32(2),
                Label = reader.GetString(3),
                TemporalAnchor = reader.GetString(4),
                FrozenState = reader.GetString(5),
                VisibleAction = reader.GetString(6),
                ParticipantSummaryJson = reader.GetString(7),
                CompositionRationale = reader.GetString(8),
                ProductionRolesJson = reader.GetString(9),
                EvidenceInteractionIdsJson = reader.GetString(10)
            });
        }
        return moments;
    }

    private static async Task InsertAttemptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SceneMomentDiscoveryAttempts (
                Id, OwnerRecordId, AttemptNumber, JobId, Status, SystemPrompt, UserPrompt,
                RawModelResponse, ReasoningContent, FinishReason, ValidationCode, ValidationDetailsJson,
                DurationMs, InputCharacters, OutputCharacters, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
            VALUES ($id, $ownerId, $attemptNumber, $jobId, 'Queued', $systemPrompt, $userPrompt,
                NULL, NULL, NULL, NULL, $validationDetails, NULL, $inputCharacters, NULL,
                $createdUtc, NULL, NULL, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", attempt.Id.Trim());
        command.Parameters.AddWithValue("$ownerId", attempt.OwnerRecordId.Trim());
        command.Parameters.AddWithValue("$attemptNumber", attempt.AttemptNumber);
        command.Parameters.AddWithValue("$jobId", attempt.JobId.Trim());
        command.Parameters.AddWithValue("$systemPrompt", attempt.SystemPrompt);
        command.Parameters.AddWithValue("$userPrompt", attempt.UserPrompt);
        command.Parameters.AddWithValue("$validationDetails", attempt.ValidationDetailsJson);
        command.Parameters.AddWithValue("$inputCharacters", attempt.InputCharacters);
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(attempt.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> OwnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string momentSetId,
        string attemptId,
        string setStatuses,
        string attemptStatuses,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT COUNT(*) FROM SceneMomentSets s
            JOIN SceneMomentDiscoveryAttempts a ON a.Id = s.CurrentAttemptId AND a.OwnerRecordId = s.Id
            WHERE s.Id = $setId AND s.CurrentAttemptId = $attemptId
              AND s.Status IN ('{setStatuses}') AND a.Status IN ('{attemptStatuses}');
            """;
        command.Parameters.AddWithValue("$setId", momentSetId.Trim());
        command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static void AddAttemptResultParameters(
        SqliteCommand command,
        string momentSetId,
        SceneBeatAnalysisAttempt attempt,
        DateTime completedUtc)
    {
        command.Parameters.AddWithValue("$setId", momentSetId.Trim());
        command.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
        command.Parameters.AddWithValue("$raw", (object?)attempt.RawModelResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$reasoning", (object?)attempt.ReasoningContent ?? DBNull.Value);
        command.Parameters.AddWithValue("$finishReason", (object?)attempt.FinishReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$validationCode", (object?)attempt.ValidationCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$validationDetails", attempt.ValidationDetailsJson);
        command.Parameters.AddWithValue("$durationMs", (object?)attempt.DurationMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputCharacters", (object?)attempt.OutputCharacters ?? DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", FormatUtc(completedUtc));
    }

    private static SceneBeatAnalysisAttempt ReadAttempt(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        OwnerRecordId = reader.GetString(1),
        AttemptNumber = reader.GetInt32(2),
        JobId = reader.GetString(3),
        Status = Enum.Parse<SceneBeatAnalysisAttemptStatus>(reader.GetString(4)),
        SystemPrompt = reader.GetString(5),
        UserPrompt = reader.GetString(6),
        RawModelResponse = reader.IsDBNull(7) ? null : reader.GetString(7),
        ReasoningContent = reader.IsDBNull(8) ? null : reader.GetString(8),
        FinishReason = reader.IsDBNull(9) ? null : reader.GetString(9),
        ValidationCode = reader.IsDBNull(10) ? null : reader.GetString(10),
        ValidationDetailsJson = reader.GetString(11),
        DurationMs = reader.IsDBNull(12) ? null : reader.GetInt64(12),
        InputCharacters = reader.GetInt32(13),
        OutputCharacters = reader.IsDBNull(14) ? null : reader.GetInt32(14),
        CreatedUtc = ParseUtc(reader.GetString(15)),
        StartedUtc = reader.IsDBNull(16) ? null : ParseUtc(reader.GetString(16)),
        CompletedUtc = reader.IsDBNull(17) ? null : ParseUtc(reader.GetString(17)),
        UpdatedUtc = ParseUtc(reader.GetString(18))
    };

    private static void ValidateNewVersion(SceneMomentSet momentSet, SceneBeatAnalysisAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(momentSet);
        ArgumentNullException.ThrowIfNull(attempt);
        Require(momentSet.Id, "Moment Set id");
        Require(momentSet.CatalogueId, "Catalogue id");
        Require(momentSet.BeatId, "Beat id");
        Require(momentSet.BeatProductionPlanId, "Beat Production Plan id");
        if (momentSet.BeatProductionPlanVersion <= 0) throw new InvalidOperationException("Beat Production Plan version must be positive.");
        Require(momentSet.PromptContractVersion, "Prompt contract version");
        Require(momentSet.BeatSnapshotJson, "Beat snapshot");
        Require(momentSet.TurnEvidenceSnapshotJson, "Turn evidence snapshot");
        Require(momentSet.ExecutionSettingsJson, "Execution settings");
        Require(attempt.Id, "Attempt id");
        if (!string.Equals(attempt.OwnerRecordId, momentSet.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment discovery attempt owner does not match Moment Set.");
        if (!string.Equals(momentSet.CurrentAttemptId, attempt.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment Set current attempt does not match the new attempt.");
        Require(attempt.JobId, "Attempt job id");
    }

    private static void ValidateTerminalAttempt(string momentSetId, SceneBeatAnalysisAttempt attempt)
    {
        Require(momentSetId, "Moment Set id");
        ArgumentNullException.ThrowIfNull(attempt);
        Require(attempt.Id, "Attempt id");
        if (!string.Equals(attempt.OwnerRecordId, momentSetId, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment discovery attempt owner does not match Moment Set.");
    }

    private static void ValidateData(string momentSetId, SceneMomentSetData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Require(data.RecommendedMomentId, "Recommended Moment id");
        if (data.Moments.Count is < 2 or > 4)
            throw new InvalidOperationException("Completed Moment Sets require 2 to 4 Moments.");
        if (!data.Moments.Select(moment => moment.Order).SequenceEqual(Enumerable.Range(1, data.Moments.Count)))
            throw new InvalidOperationException("Moment order must be contiguous and positive.");
        if (data.Moments.Select(moment => moment.MomentId).Distinct(StringComparer.Ordinal).Count() != data.Moments.Count)
            throw new InvalidOperationException("Moment ids must be unique within a Moment Set.");
        if (data.Moments.Count(moment => string.Equals(moment.MomentId, data.RecommendedMomentId, StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException("Recommended Moment id must identify exactly one Moment.");
        foreach (var moment in data.Moments)
        {
            if (!string.Equals(moment.MomentSetId, momentSetId, StringComparison.Ordinal))
                throw new InvalidOperationException("Moment owner does not match Moment Set.");
            Require(moment.MomentId, "Moment id");
            Require(moment.Label, "Moment label");
            Require(moment.TemporalAnchor, "Moment temporal anchor");
            Require(moment.FrozenState, "Moment frozen state");
            Require(moment.VisibleAction, "Moment visible action");
            Require(moment.ParticipantSummaryJson, "Moment participants");
            Require(moment.CompositionRationale, "Moment composition rationale");
            Require(moment.ProductionRolesJson, "Moment production roles");
            Require(moment.EvidenceInteractionIdsJson, "Moment evidence");
        }
    }

    private static string FormatUtc(DateTime value) => value.ToUniversalTime().ToString("O");
    private static DateTime ParseUtc(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{name} is required.");
    }

    private const string AttemptSelect = """
        SELECT Id, OwnerRecordId, AttemptNumber, JobId, Status, SystemPrompt, UserPrompt,
               RawModelResponse, ReasoningContent, FinishReason, ValidationCode, ValidationDetailsJson,
               DurationMs, InputCharacters, OutputCharacters, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
        FROM SceneMomentDiscoveryAttempts
        """;

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SceneMomentSets (
            Id TEXT PRIMARY KEY,
            CatalogueId TEXT NOT NULL,
            BeatId TEXT NOT NULL,
            BeatProductionPlanId TEXT NOT NULL,
            BeatProductionPlanVersion INTEGER NOT NULL,
            Version INTEGER NOT NULL,
            Status TEXT NOT NULL,
            CurrentAttemptId TEXT NULL,
            RecommendedMomentId TEXT NULL,
            SchemaVersion INTEGER NOT NULL,
            PromptContractVersion TEXT NOT NULL,
            BeatSnapshotJson TEXT NOT NULL,
            TurnEvidenceSnapshotJson TEXT NOT NULL,
            ModelIdentifier TEXT NULL,
            ProviderName TEXT NULL,
            ExecutionSettingsJson TEXT NOT NULL,
            ErrorCode TEXT NULL,
            ErrorMessage TEXT NULL,
            CreatedUtc TEXT NOT NULL,
            StartedUtc TEXT NULL,
            CompletedUtc TEXT NULL,
            UpdatedUtc TEXT NOT NULL,
            UNIQUE(BeatProductionPlanId, Version)
        );
        CREATE INDEX IF NOT EXISTS IX_SceneMomentSets_Current
            ON SceneMomentSets(BeatProductionPlanId, Status, Version DESC);

        CREATE TABLE IF NOT EXISTS SceneMoments (
            MomentSetId TEXT NOT NULL,
            MomentId TEXT NOT NULL,
            ItemOrder INTEGER NOT NULL,
            Label TEXT NOT NULL,
            TemporalAnchor TEXT NOT NULL,
            FrozenState TEXT NOT NULL,
            VisibleAction TEXT NOT NULL,
            ParticipantSummaryJson TEXT NOT NULL,
            CompositionRationale TEXT NOT NULL,
            ProductionRolesJson TEXT NOT NULL,
            EvidenceInteractionIdsJson TEXT NOT NULL,
            PRIMARY KEY(MomentSetId, MomentId),
            UNIQUE(MomentSetId, ItemOrder),
            FOREIGN KEY(MomentSetId) REFERENCES SceneMomentSets(Id)
        );

        CREATE TABLE IF NOT EXISTS SceneMomentDiscoveryAttempts (
            Id TEXT PRIMARY KEY,
            OwnerRecordId TEXT NOT NULL,
            AttemptNumber INTEGER NOT NULL,
            JobId TEXT NOT NULL,
            Status TEXT NOT NULL,
            SystemPrompt TEXT NOT NULL,
            UserPrompt TEXT NOT NULL,
            RawModelResponse TEXT NULL,
            ReasoningContent TEXT NULL,
            FinishReason TEXT NULL,
            ValidationCode TEXT NULL,
            ValidationDetailsJson TEXT NOT NULL,
            DurationMs INTEGER NULL,
            InputCharacters INTEGER NOT NULL,
            OutputCharacters INTEGER NULL,
            CreatedUtc TEXT NOT NULL,
            StartedUtc TEXT NULL,
            CompletedUtc TEXT NULL,
            UpdatedUtc TEXT NOT NULL,
            UNIQUE(OwnerRecordId, AttemptNumber),
            FOREIGN KEY(OwnerRecordId) REFERENCES SceneMomentSets(Id)
        );
        """;
}