using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneMomentEnrichmentRepository : ISceneMomentEnrichmentRepository
{
    private readonly string _connectionString;

    public SceneMomentEnrichmentRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateRevisionAsync(
        SceneMomentEnrichment enrichment,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ValidateNewRevision(enrichment, attempt);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        if (enrichment.Revision == 0)
        {
            await using var allocate = connection.CreateCommand();
            allocate.Transaction = transaction;
            allocate.CommandText = """
                SELECT COALESCE(MAX(Revision), 0) + 1 FROM SceneMomentEnrichments
                WHERE MomentSetId = $momentSetId AND MomentId = $momentId;
                """;
            allocate.Parameters.AddWithValue("$momentSetId", enrichment.MomentSetId.Trim());
            allocate.Parameters.AddWithValue("$momentId", enrichment.MomentId.Trim());
            enrichment.Revision = Convert.ToInt32(await allocate.ExecuteScalarAsync(cancellationToken));
        }

        await using (var supersedeEnrichments = connection.CreateCommand())
        {
            supersedeEnrichments.Transaction = transaction;
            supersedeEnrichments.CommandText = """
                UPDATE SceneMomentEnrichments SET Status = 'Superseded', UpdatedUtc = $updatedUtc
                WHERE MomentSetId = $momentSetId AND MomentId = $momentId AND Status <> 'Superseded';
                """;
            supersedeEnrichments.Parameters.AddWithValue("$momentSetId", enrichment.MomentSetId.Trim());
            supersedeEnrichments.Parameters.AddWithValue("$momentId", enrichment.MomentId.Trim());
            supersedeEnrichments.Parameters.AddWithValue("$updatedUtc", FormatUtc(enrichment.CreatedUtc));
            await supersedeEnrichments.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var supersedeAttempts = connection.CreateCommand())
        {
            supersedeAttempts.Transaction = transaction;
            supersedeAttempts.CommandText = """
                UPDATE SceneMomentEnrichmentAttempts SET Status = 'Superseded', UpdatedUtc = $updatedUtc
                WHERE OwnerRecordId IN (
                    SELECT Id FROM SceneMomentEnrichments
                    WHERE MomentSetId = $momentSetId AND MomentId = $momentId AND Status = 'Superseded')
                  AND Status IN ('Queued', 'Processing');
                """;
            supersedeAttempts.Parameters.AddWithValue("$momentSetId", enrichment.MomentSetId.Trim());
            supersedeAttempts.Parameters.AddWithValue("$momentId", enrichment.MomentId.Trim());
            supersedeAttempts.Parameters.AddWithValue("$updatedUtc", FormatUtc(enrichment.CreatedUtc));
            await supersedeAttempts.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertEnrichment = connection.CreateCommand())
        {
            insertEnrichment.Transaction = transaction;
            insertEnrichment.CommandText = """
                INSERT INTO SceneMomentEnrichments (
                    Id, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                    MomentSetId, MomentSetVersion, MomentId, Revision, Status, CurrentAttemptId,
                    SchemaVersion, PromptContractVersion, MomentSnapshotJson, TurnEvidenceSnapshotJson,
                    FrozenStateContractJson, InstantaneousSoundEventsJson, VideoKeyStateJson,
                    ModelIdentifier, ProviderName, ExecutionSettingsJson, ErrorCode, ErrorMessage,
                    CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
                VALUES (
                    $id, $catalogueId, $beatId, $planId, $planVersion,
                    $momentSetId, $momentSetVersion, $momentId, $revision, 'Pending', $attemptId,
                    $schemaVersion, $promptContractVersion, $momentSnapshot, $evidenceSnapshot,
                    NULL, NULL, NULL, NULL, NULL, $settings, NULL, NULL,
                    $createdUtc, NULL, NULL, $createdUtc);
                """;
            insertEnrichment.Parameters.AddWithValue("$id", enrichment.Id.Trim());
            insertEnrichment.Parameters.AddWithValue("$catalogueId", enrichment.CatalogueId.Trim());
            insertEnrichment.Parameters.AddWithValue("$beatId", enrichment.BeatId.Trim());
            insertEnrichment.Parameters.AddWithValue("$planId", enrichment.BeatProductionPlanId.Trim());
            insertEnrichment.Parameters.AddWithValue("$planVersion", enrichment.BeatProductionPlanVersion);
            insertEnrichment.Parameters.AddWithValue("$momentSetId", enrichment.MomentSetId.Trim());
            insertEnrichment.Parameters.AddWithValue("$momentSetVersion", enrichment.MomentSetVersion);
            insertEnrichment.Parameters.AddWithValue("$momentId", enrichment.MomentId.Trim());
            insertEnrichment.Parameters.AddWithValue("$revision", enrichment.Revision);
            insertEnrichment.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
            insertEnrichment.Parameters.AddWithValue("$schemaVersion", enrichment.SchemaVersion);
            insertEnrichment.Parameters.AddWithValue("$promptContractVersion", enrichment.PromptContractVersion.Trim());
            insertEnrichment.Parameters.AddWithValue("$momentSnapshot", enrichment.MomentSnapshotJson);
            insertEnrichment.Parameters.AddWithValue("$evidenceSnapshot", enrichment.TurnEvidenceSnapshotJson);
            insertEnrichment.Parameters.AddWithValue("$settings", enrichment.ExecutionSettingsJson);
            insertEnrichment.Parameters.AddWithValue("$createdUtc", FormatUtc(enrichment.CreatedUtc));
            await insertEnrichment.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAttemptAsync(connection, transaction, attempt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SceneMomentEnrichment?> GetAsync(
        string enrichmentId,
        CancellationToken cancellationToken = default)
    {
        Require(enrichmentId, "Moment Enrichment id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateEnrichmentSelect(connection);
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", enrichmentId.Trim());
        return await ReadEnrichmentAsync(command, cancellationToken);
    }

    public async Task<SceneMomentEnrichment?> GetCurrentAsync(
        string momentSetId,
        string momentId,
        CancellationToken cancellationToken = default)
    {
        Require(momentSetId, "Moment Set id");
        Require(momentId, "Moment id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateEnrichmentSelect(connection);
        command.CommandText += """
             WHERE MomentSetId = $momentSetId AND MomentId = $momentId
                             AND Status <> 'Superseded'
             ORDER BY Revision DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$momentSetId", momentSetId.Trim());
        command.Parameters.AddWithValue("$momentId", momentId.Trim());
        return await ReadEnrichmentAsync(command, cancellationToken);
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
        string enrichmentId,
        string attemptId,
        string modelIdentifier,
        string providerName,
        DateTime startedUtc,
        CancellationToken cancellationToken = default)
    {
        Require(modelIdentifier, "Model identifier");
        Require(providerName, "Provider name");
        return TransitionAsync(
            enrichmentId,
            attemptId,
            "Processing",
            "Processing",
            startedUtc,
            "Status = 'Pending'",
            "Status = 'Queued'",
            (enrichment, attempt) =>
            {
                enrichment.CommandText += ", ModelIdentifier = $modelIdentifier, ProviderName = $providerName, StartedUtc = $startedUtc";
                enrichment.Parameters.AddWithValue("$modelIdentifier", modelIdentifier.Trim());
                enrichment.Parameters.AddWithValue("$providerName", providerName.Trim());
                enrichment.Parameters.AddWithValue("$startedUtc", FormatUtc(startedUtc));
                attempt.CommandText += ", StartedUtc = $startedUtc";
                attempt.Parameters.AddWithValue("$startedUtc", FormatUtc(startedUtc));
            },
            cancellationToken);
    }

    public async Task<bool> TryCompleteAttemptAsync(
        string enrichmentId,
        SceneBeatAnalysisAttempt attempt,
        SceneMomentEnrichmentData data,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalAttempt(enrichmentId, attempt);
        ValidateData(data);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        if (!await OwnsAsync(connection, transaction, enrichmentId, attempt.Id, "Processing", "Processing", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using (var updateAttempt = connection.CreateCommand())
        {
            updateAttempt.Transaction = transaction;
            updateAttempt.CommandText = """
                UPDATE SceneMomentEnrichmentAttempts SET Status = 'Complete', RawModelResponse = $raw,
                    ReasoningContent = $reasoning, FinishReason = $finishReason, ValidationCode = NULL,
                    ValidationDetailsJson = $validationDetails, DurationMs = $durationMs,
                    OutputCharacters = $outputCharacters, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $attemptId AND OwnerRecordId = $enrichmentId AND Status = 'Processing';
                """;
            AddAttemptResultParameters(updateAttempt, enrichmentId, attempt, completedUtc);
            if (await updateAttempt.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var updateEnrichment = connection.CreateCommand())
        {
            updateEnrichment.Transaction = transaction;
            updateEnrichment.CommandText = """
                UPDATE SceneMomentEnrichments SET Status = 'Complete',
                    FrozenStateContractJson = $frozenState, InstantaneousSoundEventsJson = $soundEvents,
                    VideoKeyStateJson = $videoKeyState, ErrorCode = NULL, ErrorMessage = NULL,
                    CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $enrichmentId AND CurrentAttemptId = $attemptId AND Status = 'Processing';
                """;
            updateEnrichment.Parameters.AddWithValue("$enrichmentId", enrichmentId.Trim());
            updateEnrichment.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
            updateEnrichment.Parameters.AddWithValue("$frozenState", data.FrozenStateContractJson);
            updateEnrichment.Parameters.AddWithValue("$soundEvents", data.InstantaneousSoundEventsJson);
            updateEnrichment.Parameters.AddWithValue("$videoKeyState", data.VideoKeyStateJson);
            updateEnrichment.Parameters.AddWithValue("$completedUtc", FormatUtc(completedUtc));
            if (await updateEnrichment.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryFailAttemptAsync(
        string enrichmentId,
        SceneBeatAnalysisAttempt attempt,
        string errorCode,
        string errorMessage,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalAttempt(enrichmentId, attempt);
        Require(errorCode, "Error code");
        Require(errorMessage, "Error message");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        if (!await OwnsAsync(
                connection, transaction, enrichmentId, attempt.Id,
                "Pending', 'Processing", "Queued', 'Processing", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updateAttempt = connection.CreateCommand();
        updateAttempt.Transaction = transaction;
        updateAttempt.CommandText = """
            UPDATE SceneMomentEnrichmentAttempts SET Status = 'Failed', RawModelResponse = $raw,
                ReasoningContent = $reasoning, FinishReason = $finishReason, ValidationCode = $validationCode,
                ValidationDetailsJson = $validationDetails, DurationMs = $durationMs,
                OutputCharacters = $outputCharacters, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
            WHERE Id = $attemptId AND OwnerRecordId = $enrichmentId AND Status IN ('Queued', 'Processing');
            """;
        AddAttemptResultParameters(updateAttempt, enrichmentId, attempt, completedUtc);
        if (await updateAttempt.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updateEnrichment = connection.CreateCommand();
        updateEnrichment.Transaction = transaction;
        updateEnrichment.CommandText = """
            UPDATE SceneMomentEnrichments SET Status = 'Failed', ErrorCode = $errorCode,
                ErrorMessage = $errorMessage, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
            WHERE Id = $enrichmentId AND CurrentAttemptId = $attemptId
              AND Status IN ('Pending', 'Processing');
            """;
        updateEnrichment.Parameters.AddWithValue("$enrichmentId", enrichmentId.Trim());
        updateEnrichment.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
        updateEnrichment.Parameters.AddWithValue("$errorCode", errorCode.Trim());
        updateEnrichment.Parameters.AddWithValue("$errorMessage", errorMessage.Trim());
        updateEnrichment.Parameters.AddWithValue("$completedUtc", FormatUtc(completedUtc));
        if (await updateEnrichment.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> TryCancelCurrentAsync(
        string enrichmentId,
        string attemptId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            enrichmentId,
            attemptId,
            "Cancelled",
            "Cancelled",
            cancelledUtc,
            "Status IN ('Pending', 'Processing')",
            "Status IN ('Queued', 'Processing')",
            (enrichment, attempt) =>
            {
                enrichment.CommandText += ", CompletedUtc = $completedUtc";
                attempt.CommandText += ", CompletedUtc = $completedUtc";
                enrichment.Parameters.AddWithValue("$completedUtc", FormatUtc(cancelledUtc));
                attempt.Parameters.AddWithValue("$completedUtc", FormatUtc(cancelledUtc));
            },
            cancellationToken);

    private async Task<bool> TransitionAsync(
        string enrichmentId,
        string attemptId,
        string nextEnrichmentStatus,
        string nextAttemptStatus,
        DateTime updatedUtc,
        string enrichmentPredicate,
        string attemptPredicate,
        Action<SqliteCommand, SqliteCommand> decorate,
        CancellationToken cancellationToken)
    {
        Require(enrichmentId, "Moment Enrichment id");
        Require(attemptId, "Attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var updateEnrichment = connection.CreateCommand();
        await using var updateAttempt = connection.CreateCommand();
        updateEnrichment.Transaction = transaction;
        updateAttempt.Transaction = transaction;
        updateEnrichment.CommandText = "UPDATE SceneMomentEnrichments SET Status = $nextStatus, UpdatedUtc = $updatedUtc";
        updateAttempt.CommandText = "UPDATE SceneMomentEnrichmentAttempts SET Status = $nextAttemptStatus, UpdatedUtc = $updatedUtc";
        decorate(updateEnrichment, updateAttempt);
        updateEnrichment.CommandText += $" WHERE Id = $enrichmentId AND CurrentAttemptId = $attemptId AND {enrichmentPredicate};";
        updateAttempt.CommandText += $" WHERE Id = $attemptId AND OwnerRecordId = $enrichmentId AND {attemptPredicate};";
        foreach (var command in new[] { updateEnrichment, updateAttempt })
        {
            command.Parameters.AddWithValue("$enrichmentId", enrichmentId.Trim());
            command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
            command.Parameters.AddWithValue("$updatedUtc", FormatUtc(updatedUtc));
        }
        updateEnrichment.Parameters.AddWithValue("$nextStatus", nextEnrichmentStatus);
        updateAttempt.Parameters.AddWithValue("$nextAttemptStatus", nextAttemptStatus);

        if (await updateEnrichment.ExecuteNonQueryAsync(cancellationToken) != 1
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

    private static SqliteCommand CreateEnrichmentSelect(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                   MomentSetId, MomentSetVersion, MomentId, Revision, Status, CurrentAttemptId,
                   SchemaVersion, PromptContractVersion, MomentSnapshotJson, TurnEvidenceSnapshotJson,
                   FrozenStateContractJson, InstantaneousSoundEventsJson, VideoKeyStateJson,
                   ModelIdentifier, ProviderName, ExecutionSettingsJson, ErrorCode, ErrorMessage,
                   CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM SceneMomentEnrichments
            """;
        return command;
    }

    private static async Task<SceneMomentEnrichment?> ReadEnrichmentAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new SceneMomentEnrichment
        {
            Id = reader.GetString(0),
            CatalogueId = reader.GetString(1),
            BeatId = reader.GetString(2),
            BeatProductionPlanId = reader.GetString(3),
            BeatProductionPlanVersion = reader.GetInt32(4),
            MomentSetId = reader.GetString(5),
            MomentSetVersion = reader.GetInt32(6),
            MomentId = reader.GetString(7),
            Revision = reader.GetInt32(8),
            Status = Enum.Parse<SceneBeatCatalogueStatus>(reader.GetString(9)),
            CurrentAttemptId = reader.IsDBNull(10) ? null : reader.GetString(10),
            SchemaVersion = reader.GetInt32(11),
            PromptContractVersion = reader.GetString(12),
            MomentSnapshotJson = reader.GetString(13),
            TurnEvidenceSnapshotJson = reader.GetString(14),
            FrozenStateContractJson = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
            InstantaneousSoundEventsJson = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
            VideoKeyStateJson = reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
            ModelIdentifier = reader.IsDBNull(18) ? null : reader.GetString(18),
            ProviderName = reader.IsDBNull(19) ? null : reader.GetString(19),
            ExecutionSettingsJson = reader.GetString(20),
            ErrorCode = reader.IsDBNull(21) ? null : reader.GetString(21),
            ErrorMessage = reader.IsDBNull(22) ? null : reader.GetString(22),
            CreatedUtc = ParseUtc(reader.GetString(23)),
            StartedUtc = reader.IsDBNull(24) ? null : ParseUtc(reader.GetString(24)),
            CompletedUtc = reader.IsDBNull(25) ? null : ParseUtc(reader.GetString(25)),
            UpdatedUtc = ParseUtc(reader.GetString(26))
        };
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
            INSERT INTO SceneMomentEnrichmentAttempts (
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
        string enrichmentId,
        string attemptId,
        string enrichmentStatuses,
        string attemptStatuses,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT COUNT(*) FROM SceneMomentEnrichments e
            JOIN SceneMomentEnrichmentAttempts a ON a.Id = e.CurrentAttemptId AND a.OwnerRecordId = e.Id
            WHERE e.Id = $enrichmentId AND e.CurrentAttemptId = $attemptId
              AND e.Status IN ('{enrichmentStatuses}') AND a.Status IN ('{attemptStatuses}');
            """;
        command.Parameters.AddWithValue("$enrichmentId", enrichmentId.Trim());
        command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static void AddAttemptResultParameters(
        SqliteCommand command,
        string enrichmentId,
        SceneBeatAnalysisAttempt attempt,
        DateTime completedUtc)
    {
        command.Parameters.AddWithValue("$enrichmentId", enrichmentId.Trim());
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

    private static SceneBeatAnalysisAttempt ReadAttempt(SqliteDataReader reader)
        => new()
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

    private static void ValidateNewRevision(
        SceneMomentEnrichment enrichment,
        SceneBeatAnalysisAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        ArgumentNullException.ThrowIfNull(attempt);
        Require(enrichment.Id, "Moment Enrichment id");
        Require(enrichment.CatalogueId, "Catalogue id");
        Require(enrichment.BeatId, "Beat id");
        Require(enrichment.BeatProductionPlanId, "Beat Production Plan id");
        Require(enrichment.MomentSetId, "Moment Set id");
        Require(enrichment.MomentId, "Moment id");
        Require(enrichment.PromptContractVersion, "Prompt contract version");
        Require(enrichment.MomentSnapshotJson, "Moment snapshot JSON");
        Require(enrichment.TurnEvidenceSnapshotJson, "Turn evidence snapshot JSON");
        Require(enrichment.ExecutionSettingsJson, "Execution settings JSON");
        if (enrichment.BeatProductionPlanVersion < 1 || enrichment.MomentSetVersion < 1
            || enrichment.SchemaVersion < 1 || enrichment.Revision < 0)
            throw new InvalidOperationException("Lineage/schema versions must be positive and revision cannot be negative.");
        if (enrichment.Status != SceneBeatCatalogueStatus.Pending || enrichment.CurrentAttemptId != attempt.Id)
            throw new InvalidOperationException("A new Moment Enrichment must be pending and owned by its initial attempt.");
        if (attempt.OwnerRecordId != enrichment.Id || attempt.Status != SceneBeatAnalysisAttemptStatus.Queued)
            throw new InvalidOperationException("The initial Moment Enrichment attempt does not own the enrichment.");
        if (attempt.AttemptNumber < 1)
            throw new InvalidOperationException("Attempt number must be positive.");
        Require(attempt.JobId, "Attempt job id");
        Require(attempt.SystemPrompt, "Attempt system prompt");
        Require(attempt.UserPrompt, "Attempt user prompt");
        Require(attempt.ValidationDetailsJson, "Attempt validation details JSON");
    }

    private static void ValidateTerminalAttempt(string enrichmentId, SceneBeatAnalysisAttempt attempt)
    {
        Require(enrichmentId, "Moment Enrichment id");
        ArgumentNullException.ThrowIfNull(attempt);
        Require(attempt.Id, "Attempt id");
        if (!string.Equals(enrichmentId.Trim(), attempt.OwnerRecordId, StringComparison.Ordinal))
            throw new InvalidOperationException("The attempt does not own the Moment Enrichment.");
        Require(attempt.ValidationDetailsJson, "Attempt validation details JSON");
    }

    private static void ValidateData(SceneMomentEnrichmentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Require(data.FrozenStateContractJson, "Frozen state contract JSON");
        Require(data.InstantaneousSoundEventsJson, "Instantaneous sound events JSON");
        Require(data.VideoKeyStateJson, "Video key state JSON");
    }

    private static string FormatUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value.ToString("O", CultureInfo.InvariantCulture)
            : throw new InvalidOperationException("Persistence timestamps must be UTC.");

    private static DateTime ParseUtc(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
    }

    private const string AttemptSelect = """
        SELECT Id, OwnerRecordId, AttemptNumber, JobId, Status, SystemPrompt, UserPrompt,
               RawModelResponse, ReasoningContent, FinishReason, ValidationCode, ValidationDetailsJson,
               DurationMs, InputCharacters, OutputCharacters, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
        FROM SceneMomentEnrichmentAttempts
        """;

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SceneMomentEnrichments (
            Id TEXT PRIMARY KEY,
            CatalogueId TEXT NOT NULL,
            BeatId TEXT NOT NULL,
            BeatProductionPlanId TEXT NOT NULL,
            BeatProductionPlanVersion INTEGER NOT NULL,
            MomentSetId TEXT NOT NULL,
            MomentSetVersion INTEGER NOT NULL,
            MomentId TEXT NOT NULL,
            Revision INTEGER NOT NULL,
            Status TEXT NOT NULL,
            CurrentAttemptId TEXT NULL,
            SchemaVersion INTEGER NOT NULL,
            PromptContractVersion TEXT NOT NULL,
            MomentSnapshotJson TEXT NOT NULL,
            TurnEvidenceSnapshotJson TEXT NOT NULL,
            FrozenStateContractJson TEXT NULL,
            InstantaneousSoundEventsJson TEXT NULL,
            VideoKeyStateJson TEXT NULL,
            ModelIdentifier TEXT NULL,
            ProviderName TEXT NULL,
            ExecutionSettingsJson TEXT NOT NULL,
            ErrorCode TEXT NULL,
            ErrorMessage TEXT NULL,
            CreatedUtc TEXT NOT NULL,
            StartedUtc TEXT NULL,
            CompletedUtc TEXT NULL,
            UpdatedUtc TEXT NOT NULL,
            UNIQUE (MomentSetId, MomentId, Revision)
        );
        CREATE INDEX IF NOT EXISTS IX_SceneMomentEnrichments_Current
            ON SceneMomentEnrichments (MomentSetId, MomentId, Revision DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_SceneMomentEnrichments_Current
            ON SceneMomentEnrichments (MomentSetId, MomentId)
            WHERE Status NOT IN ('Superseded', 'Cancelled');

        CREATE TABLE IF NOT EXISTS SceneMomentEnrichmentAttempts (
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
            UNIQUE (OwnerRecordId, AttemptNumber),
            FOREIGN KEY (OwnerRecordId) REFERENCES SceneMomentEnrichments(Id)
        );
        """;
}
