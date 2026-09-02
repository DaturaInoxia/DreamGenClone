using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneBeatCatalogueRepository : ISceneBeatCatalogueRepository
{
    private readonly string _connectionString;

    public SceneBeatCatalogueRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateVersionAsync(
        SceneBeatCatalogue catalogue,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ValidateNewVersion(catalogue, attempt);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (catalogue.Version == 0)
        {
            await using var allocateVersion = connection.CreateCommand();
            allocateVersion.Transaction = (SqliteTransaction)transaction;
            allocateVersion.CommandText = """
                SELECT COALESCE(MAX(Version), 0) + 1
                FROM SceneBeatCatalogues
                WHERE SessionId = $sessionId AND TurnId = $turnId;
                """;
            allocateVersion.Parameters.AddWithValue("$sessionId", catalogue.SessionId.Trim());
            allocateVersion.Parameters.AddWithValue("$turnId", catalogue.TurnId.Trim());
            catalogue.Version = Convert.ToInt32(await allocateVersion.ExecuteScalarAsync(cancellationToken));
        }

        await using (var supersede = connection.CreateCommand())
        {
            supersede.Transaction = (SqliteTransaction)transaction;
            supersede.CommandText = """
                UPDATE SceneBeatCatalogues
                SET Status = 'Superseded', UpdatedUtc = $updatedUtc
                WHERE SessionId = $sessionId AND TurnId = $turnId
                  AND Status NOT IN ('Superseded', 'Cancelled');
                """;
            supersede.Parameters.AddWithValue("$sessionId", catalogue.SessionId.Trim());
            supersede.Parameters.AddWithValue("$turnId", catalogue.TurnId.Trim());
            supersede.Parameters.AddWithValue("$updatedUtc", catalogue.CreatedUtc.ToString("O"));
            await supersede.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertCatalogue = connection.CreateCommand())
        {
            insertCatalogue.Transaction = (SqliteTransaction)transaction;
            insertCatalogue.CommandText = """
                INSERT INTO SceneBeatCatalogues (
                    Id, SessionId, TurnId, Version, Status, CurrentAttemptId, SchemaVersion,
                    PromptContractVersion, InputSnapshotJson, ModelIdentifier, ProviderName,
                    ExecutionSettingsJson, ErrorCode, ErrorMessage, CreatedUtc, StartedUtc,
                    CompletedUtc, UpdatedUtc)
                VALUES (
                    $id, $sessionId, $turnId, $version, $status, $currentAttemptId, $schemaVersion,
                    $promptContractVersion, $inputSnapshotJson, NULL, NULL, $executionSettingsJson,
                    NULL, NULL, $createdUtc, NULL, NULL, $updatedUtc);
                """;
            AddCatalogueParameters(insertCatalogue, catalogue);
            await insertCatalogue.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAttemptAsync(connection, (SqliteTransaction)transaction, attempt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SceneBeatCatalogue?> GetAsync(string catalogueId, CancellationToken cancellationToken = default)
    {
        Require(catalogueId, "Catalogue id");
        await using var connection = await OpenAsync(cancellationToken);
        return await LoadCatalogueAsync(connection, "Id = $value", catalogueId.Trim(), cancellationToken);
    }

    public async Task<int> GetNextVersionAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Session id");
        Require(turnId, "Turn id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(MAX(Version), 0) + 1
            FROM SceneBeatCatalogues
            WHERE SessionId = $sessionId AND TurnId = $turnId;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$turnId", turnId.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<SceneBeatCatalogue?> GetCurrentByTurnAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Session id");
        Require(turnId, "Turn id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateCatalogueSelect(connection);
        command.CommandText += """
                         WHERE SessionId = $sessionId AND TurnId = $turnId
              AND Status NOT IN ('Superseded', 'Cancelled')
            ORDER BY Version DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$turnId", turnId.Trim());
        return await ReadCatalogueAsync(connection, command, cancellationToken);
    }

    public async Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(
        string attemptId,
        CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, OwnerRecordId, AttemptNumber, JobId, Status, SystemPrompt, UserPrompt,
                   RawModelResponse, ReasoningContent, FinishReason, ValidationCode,
                   ValidationDetailsJson, DurationMs, InputCharacters, OutputCharacters,
                   CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM SceneBeatAnalysisAttempts WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", attemptId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    public async Task<bool> TryStartAttemptAsync(
        string catalogueId,
        string attemptId,
        string modelIdentifier,
        string providerName,
        string executionSettingsJson,
        DateTime startedUtc,
        CancellationToken cancellationToken = default)
    {
        Require(modelIdentifier, "Model identifier");
        Require(providerName, "Provider name");
        Require(executionSettingsJson, "Execution settings JSON");
        return await TransitionAsync(
            catalogueId,
            attemptId,
            "Pending",
            "Processing",
            "Queued",
            "Processing",
            startedUtc,
            (catalogue, attempt) =>
            {
                catalogue.CommandText += ", ModelIdentifier = $modelIdentifier, ProviderName = $providerName, ExecutionSettingsJson = $executionSettingsJson, StartedUtc = $startedUtc";
                catalogue.Parameters.AddWithValue("$modelIdentifier", modelIdentifier.Trim());
                catalogue.Parameters.AddWithValue("$providerName", providerName.Trim());
                catalogue.Parameters.AddWithValue("$executionSettingsJson", executionSettingsJson);
                catalogue.Parameters.AddWithValue("$startedUtc", startedUtc.ToString("O"));
                attempt.CommandText += ", StartedUtc = $startedUtc";
                attempt.Parameters.AddWithValue("$startedUtc", startedUtc.ToString("O"));
            },
            cancellationToken);
    }

    public async Task<bool> TryCompleteAttemptAsync(
        string catalogueId,
        SceneBeatAnalysisAttempt attempt,
        IReadOnlyList<SceneBeatCatalogueEntry> entries,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalAttempt(catalogueId, attempt);
        if (entries.Count < 1)
            throw new InvalidOperationException("A completed Beat Catalogue requires at least one entry.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await OwnsProcessingAttemptAsync(connection, (SqliteTransaction)transaction, catalogueId, attempt.Id, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        foreach (var entry in entries)
        {
            ValidateEntry(catalogueId, entry);
            await InsertEntryAsync(connection, (SqliteTransaction)transaction, entry, cancellationToken);
        }

        await using (var updateAttempt = connection.CreateCommand())
        {
            updateAttempt.Transaction = (SqliteTransaction)transaction;
            updateAttempt.CommandText = """
                UPDATE SceneBeatAnalysisAttempts
                SET Status = 'Complete', RawModelResponse = $rawModelResponse,
                    ReasoningContent = $reasoningContent, FinishReason = $finishReason,
                    ValidationCode = NULL, ValidationDetailsJson = $validationDetailsJson,
                    DurationMs = $durationMs, OutputCharacters = $outputCharacters,
                    CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $attemptId AND OwnerRecordId = $catalogueId AND Status = 'Processing';
                """;
            AddTerminalAttemptParameters(updateAttempt, catalogueId, attempt, completedUtc);
            await updateAttempt.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateCatalogue = connection.CreateCommand())
        {
            updateCatalogue.Transaction = (SqliteTransaction)transaction;
            updateCatalogue.CommandText = """
                UPDATE SceneBeatCatalogues
                SET Status = 'Complete', ErrorCode = NULL, ErrorMessage = NULL,
                    CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $catalogueId AND CurrentAttemptId = $attemptId AND Status = 'Processing';
                """;
            updateCatalogue.Parameters.AddWithValue("$catalogueId", catalogueId.Trim());
            updateCatalogue.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
            updateCatalogue.Parameters.AddWithValue("$completedUtc", completedUtc.ToString("O"));
            if (await updateCatalogue.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryFailAttemptAsync(
        string catalogueId,
        SceneBeatAnalysisAttempt attempt,
        string errorCode,
        string errorMessage,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalAttempt(catalogueId, attempt);
        Require(errorCode, "Error code");
        Require(errorMessage, "Error message");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var updateAttempt = connection.CreateCommand())
        {
            updateAttempt.Transaction = (SqliteTransaction)transaction;
            updateAttempt.CommandText = """
                UPDATE SceneBeatAnalysisAttempts
                SET Status = 'Failed', RawModelResponse = $rawModelResponse,
                    ReasoningContent = $reasoningContent, FinishReason = $finishReason,
                    ValidationCode = $validationCode, ValidationDetailsJson = $validationDetailsJson,
                    DurationMs = $durationMs, OutputCharacters = $outputCharacters,
                    CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $attemptId AND OwnerRecordId = $catalogueId
                  AND Status IN ('Queued', 'Processing');
                """;
            AddTerminalAttemptParameters(updateAttempt, catalogueId, attempt, completedUtc);
            if (await updateAttempt.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var updateCatalogue = connection.CreateCommand())
        {
            updateCatalogue.Transaction = (SqliteTransaction)transaction;
            updateCatalogue.CommandText = """
                UPDATE SceneBeatCatalogues
                SET Status = 'Failed', ErrorCode = $errorCode, ErrorMessage = $errorMessage,
                    CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                                WHERE Id = $catalogueId AND CurrentAttemptId = $attemptId
                                    AND Status IN ('Pending', 'Processing');
                """;
            updateCatalogue.Parameters.AddWithValue("$catalogueId", catalogueId.Trim());
            updateCatalogue.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
            updateCatalogue.Parameters.AddWithValue("$errorCode", errorCode.Trim());
            updateCatalogue.Parameters.AddWithValue("$errorMessage", errorMessage.Trim());
            updateCatalogue.Parameters.AddWithValue("$completedUtc", completedUtc.ToString("O"));
            if (await updateCatalogue.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> TryCancelCurrentAsync(
        string catalogueId,
        string attemptId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            catalogueId,
            attemptId,
            null,
            "Cancelled",
            null,
            "Cancelled",
            cancelledUtc,
            (catalogue, attempt) =>
            {
                catalogue.CommandText += ", CompletedUtc = $completedUtc";
                catalogue.Parameters.AddWithValue("$completedUtc", cancelledUtc.ToString("O"));
                attempt.CommandText += ", CompletedUtc = $completedUtc";
                attempt.Parameters.AddWithValue("$completedUtc", cancelledUtc.ToString("O"));
            },
            cancellationToken,
            "Status IN ('Pending', 'Processing')",
            "Status IN ('Queued', 'Processing')");

    private async Task<bool> TransitionAsync(
        string catalogueId,
        string attemptId,
        string? expectedCatalogueStatus,
        string nextCatalogueStatus,
        string? expectedAttemptStatus,
        string nextAttemptStatus,
        DateTime updatedUtc,
        Action<SqliteCommand, SqliteCommand> decorate,
        CancellationToken cancellationToken,
        string? cataloguePredicate = null,
        string? attemptPredicate = null)
    {
        Require(catalogueId, "Catalogue id");
        Require(attemptId, "Attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var updateCatalogue = connection.CreateCommand();
        await using var updateAttempt = connection.CreateCommand();
        updateCatalogue.Transaction = (SqliteTransaction)transaction;
        updateAttempt.Transaction = (SqliteTransaction)transaction;
        var catalogueWhere = cataloguePredicate ?? "Status = $expectedCatalogueStatus";
        var attemptWhere = attemptPredicate ?? "Status = $expectedAttemptStatus";
        updateCatalogue.CommandText = "UPDATE SceneBeatCatalogues SET Status = $nextCatalogueStatus, UpdatedUtc = $updatedUtc";
        updateAttempt.CommandText = "UPDATE SceneBeatAnalysisAttempts SET Status = $nextAttemptStatus, UpdatedUtc = $updatedUtc";
        decorate(updateCatalogue, updateAttempt);
        updateCatalogue.CommandText += $" WHERE Id = $catalogueId AND CurrentAttemptId = $attemptId AND {catalogueWhere};";
        updateAttempt.CommandText += $" WHERE Id = $attemptId AND OwnerRecordId = $catalogueId AND {attemptWhere};";
        foreach (var command in new[] { updateCatalogue, updateAttempt })
        {
            command.Parameters.AddWithValue("$catalogueId", catalogueId.Trim());
            command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
            command.Parameters.AddWithValue("$updatedUtc", updatedUtc.ToString("O"));
        }
        updateCatalogue.Parameters.AddWithValue("$nextCatalogueStatus", nextCatalogueStatus);
        updateAttempt.Parameters.AddWithValue("$nextAttemptStatus", nextAttemptStatus);
        if (expectedCatalogueStatus is not null)
            updateCatalogue.Parameters.AddWithValue("$expectedCatalogueStatus", expectedCatalogueStatus);
        if (expectedAttemptStatus is not null)
            updateAttempt.Parameters.AddWithValue("$expectedAttemptStatus", expectedAttemptStatus);

        if (await updateCatalogue.ExecuteNonQueryAsync(cancellationToken) != 1
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
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneBeatCatalogues (
                Id TEXT PRIMARY KEY, SessionId TEXT NOT NULL, TurnId TEXT NOT NULL,
                Version INTEGER NOT NULL, Status TEXT NOT NULL, CurrentAttemptId TEXT NULL,
                SchemaVersion INTEGER NOT NULL, PromptContractVersion TEXT NOT NULL,
                InputSnapshotJson TEXT NOT NULL, ModelIdentifier TEXT NULL, ProviderName TEXT NULL,
                ExecutionSettingsJson TEXT NOT NULL, ErrorCode TEXT NULL, ErrorMessage TEXT NULL,
                CreatedUtc TEXT NOT NULL, StartedUtc TEXT NULL, CompletedUtc TEXT NULL, UpdatedUtc TEXT NOT NULL,
                UNIQUE (SessionId, TurnId, Version)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneBeatCatalogues_SessionTurn
                ON SceneBeatCatalogues (SessionId, TurnId, Version DESC);
            CREATE TABLE IF NOT EXISTS SceneBeatCatalogueEntries (
                CatalogueId TEXT NOT NULL, BeatId TEXT NOT NULL, BeatOrder INTEGER NOT NULL,
                Label TEXT NOT NULL, BeatSynopsis TEXT NOT NULL, PrimaryLocation TEXT NOT NULL,
                ParticipantSummaryJson TEXT NOT NULL, EvidenceInteractionIdsJson TEXT NOT NULL,
                ContentTagsJson TEXT NOT NULL,
                PRIMARY KEY (CatalogueId, BeatId), UNIQUE (CatalogueId, BeatOrder),
                FOREIGN KEY (CatalogueId) REFERENCES SceneBeatCatalogues(Id)
            );
            CREATE TABLE IF NOT EXISTS SceneBeatAnalysisAttempts (
                Id TEXT PRIMARY KEY, OwnerRecordId TEXT NOT NULL, AttemptNumber INTEGER NOT NULL,
                JobId TEXT NOT NULL, Status TEXT NOT NULL, SystemPrompt TEXT NOT NULL, UserPrompt TEXT NOT NULL,
                RawModelResponse TEXT NULL, ReasoningContent TEXT NULL, FinishReason TEXT NULL,
                ValidationCode TEXT NULL, ValidationDetailsJson TEXT NOT NULL, DurationMs INTEGER NULL,
                InputCharacters INTEGER NOT NULL, OutputCharacters INTEGER NULL, CreatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL, CompletedUtc TEXT NULL, UpdatedUtc TEXT NOT NULL,
                UNIQUE (OwnerRecordId, AttemptNumber), FOREIGN KEY (OwnerRecordId) REFERENCES SceneBeatCatalogues(Id)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneBeatAnalysisAttempts_Owner
                ON SceneBeatAnalysisAttempts (OwnerRecordId, AttemptNumber);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SceneBeatCatalogue?> LoadCatalogueAsync(
        SqliteConnection connection,
        string predicate,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCatalogueSelect(connection);
        command.CommandText += $" WHERE {predicate} LIMIT 1;";
        command.Parameters.AddWithValue("$value", value);
        return await ReadCatalogueAsync(connection, command, cancellationToken);
    }

    private static SqliteCommand CreateCatalogueSelect(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, TurnId, Version, Status, CurrentAttemptId, SchemaVersion,
                   PromptContractVersion, InputSnapshotJson, ModelIdentifier, ProviderName,
                   ExecutionSettingsJson, ErrorCode, ErrorMessage, CreatedUtc, StartedUtc,
                   CompletedUtc, UpdatedUtc
            FROM SceneBeatCatalogues
            """;
        return command;
    }

    private static async Task<SceneBeatCatalogue?> ReadCatalogueAsync(
        SqliteConnection connection,
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        SceneBeatCatalogue catalogue;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            catalogue = new SceneBeatCatalogue
            {
                Id = reader.GetString(0), SessionId = reader.GetString(1), TurnId = reader.GetString(2),
                Version = reader.GetInt32(3), Status = ParseEnum<SceneBeatCatalogueStatus>(reader.GetString(4)),
                CurrentAttemptId = NullableString(reader, 5), SchemaVersion = reader.GetInt32(6),
                PromptContractVersion = reader.GetString(7), InputSnapshotJson = reader.GetString(8),
                ModelIdentifier = NullableString(reader, 9), ProviderName = NullableString(reader, 10),
                ExecutionSettingsJson = reader.GetString(11), ErrorCode = NullableString(reader, 12),
                ErrorMessage = NullableString(reader, 13), CreatedUtc = ParseUtc(reader.GetString(14)),
                StartedUtc = NullableUtc(reader, 15), CompletedUtc = NullableUtc(reader, 16),
                UpdatedUtc = ParseUtc(reader.GetString(17))
            };
        }
        catalogue.Entries = await LoadEntriesAsync(connection, catalogue.Id, cancellationToken);
        return catalogue;
    }

    private static async Task<IReadOnlyList<SceneBeatCatalogueEntry>> LoadEntriesAsync(
        SqliteConnection connection,
        string catalogueId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CatalogueId, BeatId, BeatOrder, Label, BeatSynopsis, PrimaryLocation,
                   ParticipantSummaryJson, EvidenceInteractionIdsJson, ContentTagsJson
            FROM SceneBeatCatalogueEntries WHERE CatalogueId = $catalogueId ORDER BY BeatOrder;
            """;
        command.Parameters.AddWithValue("$catalogueId", catalogueId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<SceneBeatCatalogueEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new SceneBeatCatalogueEntry
            {
                CatalogueId = reader.GetString(0), BeatId = reader.GetString(1), Order = reader.GetInt32(2),
                Label = reader.GetString(3), BeatSynopsis = reader.GetString(4), PrimaryLocation = reader.GetString(5),
                ParticipantSummaryJson = reader.GetString(6), EvidenceInteractionIdsJson = reader.GetString(7),
                ContentTagsJson = reader.GetString(8)
            });
        }
        return entries;
    }

    private static SceneBeatAnalysisAttempt ReadAttempt(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), OwnerRecordId = reader.GetString(1), AttemptNumber = reader.GetInt32(2),
        JobId = reader.GetString(3), Status = ParseEnum<SceneBeatAnalysisAttemptStatus>(reader.GetString(4)),
        SystemPrompt = reader.GetString(5), UserPrompt = reader.GetString(6), RawModelResponse = NullableString(reader, 7),
        ReasoningContent = NullableString(reader, 8), FinishReason = NullableString(reader, 9),
        ValidationCode = NullableString(reader, 10), ValidationDetailsJson = reader.GetString(11),
        DurationMs = reader.IsDBNull(12) ? null : reader.GetInt64(12), InputCharacters = reader.GetInt32(13),
        OutputCharacters = reader.IsDBNull(14) ? null : reader.GetInt32(14), CreatedUtc = ParseUtc(reader.GetString(15)),
        StartedUtc = NullableUtc(reader, 16), CompletedUtc = NullableUtc(reader, 17), UpdatedUtc = ParseUtc(reader.GetString(18))
    };

    private static async Task InsertAttemptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SceneBeatAnalysisAttempts (
                Id, OwnerRecordId, AttemptNumber, JobId, Status, SystemPrompt, UserPrompt,
                RawModelResponse, ReasoningContent, FinishReason, ValidationCode, ValidationDetailsJson,
                DurationMs, InputCharacters, OutputCharacters, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
            VALUES ($id, $ownerRecordId, $attemptNumber, $jobId, $status, $systemPrompt, $userPrompt,
                NULL, NULL, NULL, NULL, $validationDetailsJson, NULL, $inputCharacters, NULL,
                $createdUtc, NULL, NULL, $updatedUtc);
            """;
        command.Parameters.AddWithValue("$id", attempt.Id);
        command.Parameters.AddWithValue("$ownerRecordId", attempt.OwnerRecordId);
        command.Parameters.AddWithValue("$attemptNumber", attempt.AttemptNumber);
        command.Parameters.AddWithValue("$jobId", attempt.JobId);
        command.Parameters.AddWithValue("$status", attempt.Status.ToString());
        command.Parameters.AddWithValue("$systemPrompt", attempt.SystemPrompt);
        command.Parameters.AddWithValue("$userPrompt", attempt.UserPrompt);
        command.Parameters.AddWithValue("$validationDetailsJson", attempt.ValidationDetailsJson);
        command.Parameters.AddWithValue("$inputCharacters", attempt.InputCharacters);
        command.Parameters.AddWithValue("$createdUtc", attempt.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", attempt.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SceneBeatCatalogueEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SceneBeatCatalogueEntries (
                CatalogueId, BeatId, BeatOrder, Label, BeatSynopsis, PrimaryLocation,
                ParticipantSummaryJson, EvidenceInteractionIdsJson, ContentTagsJson)
            VALUES ($catalogueId, $beatId, $order, $label, $beatSynopsis, $primaryLocation,
                $participantSummaryJson, $evidenceInteractionIdsJson, $contentTagsJson);
            """;
        command.Parameters.AddWithValue("$catalogueId", entry.CatalogueId);
        command.Parameters.AddWithValue("$beatId", entry.BeatId);
        command.Parameters.AddWithValue("$order", entry.Order);
        command.Parameters.AddWithValue("$label", entry.Label.Trim());
        command.Parameters.AddWithValue("$beatSynopsis", entry.BeatSynopsis.Trim());
        command.Parameters.AddWithValue("$primaryLocation", entry.PrimaryLocation.Trim());
        command.Parameters.AddWithValue("$participantSummaryJson", entry.ParticipantSummaryJson);
        command.Parameters.AddWithValue("$evidenceInteractionIdsJson", entry.EvidenceInteractionIdsJson);
        command.Parameters.AddWithValue("$contentTagsJson", entry.ContentTagsJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> OwnsProcessingAttemptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string catalogueId,
        string attemptId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM SceneBeatCatalogues c
            JOIN SceneBeatAnalysisAttempts a ON a.Id = c.CurrentAttemptId AND a.OwnerRecordId = c.Id
            WHERE c.Id = $catalogueId AND c.CurrentAttemptId = $attemptId
              AND c.Status = 'Processing' AND a.Status = 'Processing';
            """;
        command.Parameters.AddWithValue("$catalogueId", catalogueId.Trim());
        command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static void AddCatalogueParameters(SqliteCommand command, SceneBeatCatalogue catalogue)
    {
        command.Parameters.AddWithValue("$id", catalogue.Id);
        command.Parameters.AddWithValue("$sessionId", catalogue.SessionId.Trim());
        command.Parameters.AddWithValue("$turnId", catalogue.TurnId.Trim());
        command.Parameters.AddWithValue("$version", catalogue.Version);
        command.Parameters.AddWithValue("$status", catalogue.Status.ToString());
        command.Parameters.AddWithValue("$currentAttemptId", catalogue.CurrentAttemptId!);
        command.Parameters.AddWithValue("$schemaVersion", catalogue.SchemaVersion);
        command.Parameters.AddWithValue("$promptContractVersion", catalogue.PromptContractVersion.Trim());
        command.Parameters.AddWithValue("$inputSnapshotJson", catalogue.InputSnapshotJson);
        command.Parameters.AddWithValue("$executionSettingsJson", catalogue.ExecutionSettingsJson);
        command.Parameters.AddWithValue("$createdUtc", catalogue.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", catalogue.UpdatedUtc.ToString("O"));
    }

    private static void AddTerminalAttemptParameters(
        SqliteCommand command,
        string catalogueId,
        SceneBeatAnalysisAttempt attempt,
        DateTime completedUtc)
    {
        command.Parameters.AddWithValue("$catalogueId", catalogueId.Trim());
        command.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
        command.Parameters.AddWithValue("$rawModelResponse", (object?)attempt.RawModelResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$reasoningContent", (object?)attempt.ReasoningContent ?? DBNull.Value);
        command.Parameters.AddWithValue("$finishReason", (object?)attempt.FinishReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$validationCode", (object?)attempt.ValidationCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$validationDetailsJson", attempt.ValidationDetailsJson);
        command.Parameters.AddWithValue("$durationMs", (object?)attempt.DurationMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputCharacters", (object?)attempt.OutputCharacters ?? DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", completedUtc.ToString("O"));
    }

    private static void ValidateNewVersion(SceneBeatCatalogue catalogue, SceneBeatAnalysisAttempt attempt)
    {
        Require(catalogue.Id, "Catalogue id");
        Require(catalogue.SessionId, "Session id");
        Require(catalogue.TurnId, "Turn id");
        Require(catalogue.PromptContractVersion, "Prompt contract version");
        Require(catalogue.InputSnapshotJson, "Input snapshot JSON");
        Require(catalogue.ExecutionSettingsJson, "Execution settings JSON");
        Require(attempt.JobId, "Job id");
        Require(attempt.SystemPrompt, "System prompt");
        Require(attempt.UserPrompt, "User prompt");
        Require(attempt.ValidationDetailsJson, "Validation details JSON");
        if (catalogue.Version < 0 || catalogue.SchemaVersion < 1)
            throw new InvalidOperationException("Beat Catalogue version cannot be negative and schema version must be positive.");
        if (catalogue.Status != SceneBeatCatalogueStatus.Pending || attempt.Status != SceneBeatAnalysisAttemptStatus.Queued)
            throw new InvalidOperationException("A new Beat Catalogue and attempt must be Pending and Queued.");
        if (!string.Equals(catalogue.CurrentAttemptId, attempt.Id, StringComparison.Ordinal)
            || !string.Equals(attempt.OwnerRecordId, catalogue.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("The new Beat Catalogue must own its current attempt.");
    }

    private static void ValidateTerminalAttempt(string catalogueId, SceneBeatAnalysisAttempt attempt)
    {
        Require(catalogueId, "Catalogue id");
        Require(attempt.Id, "Attempt id");
        Require(attempt.ValidationDetailsJson, "Validation details JSON");
        if (!string.Equals(attempt.OwnerRecordId, catalogueId, StringComparison.Ordinal))
            throw new InvalidOperationException("Beat Catalogue attempt owner does not match the catalogue.");
    }

    private static void ValidateEntry(string catalogueId, SceneBeatCatalogueEntry entry)
    {
        if (!string.Equals(entry.CatalogueId, catalogueId, StringComparison.Ordinal))
            throw new InvalidOperationException("Beat Catalogue entry owner does not match the catalogue.");
        Require(entry.BeatId, "Beat id");
        Require(entry.Label, "Beat label");
        Require(entry.BeatSynopsis, "Beat synopsis");
        Require(entry.PrimaryLocation, "Primary location");
        Require(entry.ParticipantSummaryJson, "Participant summary JSON");
        Require(entry.EvidenceInteractionIdsJson, "Evidence interaction ids JSON");
        Require(entry.ContentTagsJson, "Content tags JSON");
        if (entry.Order < 1) throw new InvalidOperationException("Beat Catalogue entry order must be positive.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
        => Enum.TryParse<T>(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Stored value '{value}' is invalid for {typeof(T).Name}.");

    private static string? NullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime ParseUtc(string value)
        => DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Stored UTC value '{value}' is invalid.");

    private static DateTime? NullableUtc(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : ParseUtc(reader.GetString(ordinal));
}