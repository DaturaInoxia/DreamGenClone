using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneBeatProductionPlanRepository : ISceneBeatProductionPlanRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public SceneBeatProductionPlanRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateVersionAsync(
        SceneBeatProductionPlan plan,
        SceneBeatAnalysisAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ValidateNewVersion(plan, attempt);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (plan.Version == 0)
        {
            await using var allocate = connection.CreateCommand();
            allocate.Transaction = (SqliteTransaction)transaction;
            allocate.CommandText = """
                SELECT COALESCE(MAX(Version), 0) + 1 FROM SceneBeatProductionPlans
                WHERE CatalogueId = $catalogueId AND BeatId = $beatId;
                """;
            allocate.Parameters.AddWithValue("$catalogueId", plan.CatalogueId.Trim());
            allocate.Parameters.AddWithValue("$beatId", plan.BeatId.Trim());
            plan.Version = Convert.ToInt32(await allocate.ExecuteScalarAsync(cancellationToken));
        }

        await using (var supersedePlans = connection.CreateCommand())
        {
            supersedePlans.Transaction = (SqliteTransaction)transaction;
            supersedePlans.CommandText = """
                UPDATE SceneBeatProductionPlans SET Status = 'Superseded', UpdatedUtc = $updatedUtc
                WHERE CatalogueId = $catalogueId AND BeatId = $beatId
                  AND Status NOT IN ('Superseded', 'Cancelled');
                """;
            supersedePlans.Parameters.AddWithValue("$catalogueId", plan.CatalogueId.Trim());
            supersedePlans.Parameters.AddWithValue("$beatId", plan.BeatId.Trim());
            supersedePlans.Parameters.AddWithValue("$updatedUtc", FormatUtc(plan.CreatedUtc));
            await supersedePlans.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var supersedeAttempts = connection.CreateCommand())
        {
            supersedeAttempts.Transaction = (SqliteTransaction)transaction;
            supersedeAttempts.CommandText = """
                UPDATE SceneBeatProductionAttempts SET Status = 'Superseded', UpdatedUtc = $updatedUtc
                WHERE OwnerRecordId IN (
                    SELECT Id FROM SceneBeatProductionPlans
                    WHERE CatalogueId = $catalogueId AND BeatId = $beatId AND Status = 'Superseded')
                  AND Status IN ('Queued', 'Processing');
                """;
            supersedeAttempts.Parameters.AddWithValue("$catalogueId", plan.CatalogueId.Trim());
            supersedeAttempts.Parameters.AddWithValue("$beatId", plan.BeatId.Trim());
            supersedeAttempts.Parameters.AddWithValue("$updatedUtc", FormatUtc(plan.CreatedUtc));
            await supersedeAttempts.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertPlan = connection.CreateCommand())
        {
            insertPlan.Transaction = (SqliteTransaction)transaction;
            insertPlan.CommandText = """
                INSERT INTO SceneBeatProductionPlans (
                    Id, CatalogueId, BeatId, CatalogueVersion, Version, Status, CurrentAttemptId,
                    SchemaVersion, PromptContractVersion, SourceSnapshotJson, NarrativeArcJson,
                    TimelineJson, NarrationCuesJson, DialogueCuesJson, AmbiencePlanJson,
                    SoundEventCuesJson, MusicPlanJson, ActionArcJson, StartContinuityJson,
                    EndContinuityJson, TypedReferencesJson, VideoCoveragePlansJson,
                    ModelIdentifier, ProviderName, ExecutionSettingsJson, ErrorCode, ErrorMessage,
                    CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
                VALUES (
                    $id, $catalogueId, $beatId, $catalogueVersion, $version, 'Pending', $attemptId,
                    $schemaVersion, $promptContractVersion, $sourceSnapshotJson, '', '', '', '', '',
                    '', '', '', '', '', '', '', NULL, NULL, $executionSettingsJson, NULL, NULL,
                    $createdUtc, NULL, NULL, $createdUtc);
                """;
            insertPlan.Parameters.AddWithValue("$id", plan.Id.Trim());
            insertPlan.Parameters.AddWithValue("$catalogueId", plan.CatalogueId.Trim());
            insertPlan.Parameters.AddWithValue("$beatId", plan.BeatId.Trim());
            insertPlan.Parameters.AddWithValue("$catalogueVersion", plan.CatalogueVersion);
            insertPlan.Parameters.AddWithValue("$version", plan.Version);
            insertPlan.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
            insertPlan.Parameters.AddWithValue("$schemaVersion", plan.SchemaVersion);
            insertPlan.Parameters.AddWithValue("$promptContractVersion", plan.PromptContractVersion.Trim());
            insertPlan.Parameters.AddWithValue("$sourceSnapshotJson", plan.SourceSnapshotJson);
            insertPlan.Parameters.AddWithValue("$executionSettingsJson", plan.ExecutionSettingsJson);
            insertPlan.Parameters.AddWithValue("$createdUtc", FormatUtc(plan.CreatedUtc));
            await insertPlan.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAttemptAsync(connection, (SqliteTransaction)transaction, attempt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SceneBeatProductionPlan?> GetAsync(string planId, CancellationToken cancellationToken = default)
    {
        Require(planId, "Beat Production Plan id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreatePlanSelect(connection);
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", planId.Trim());
        return await ReadPlanAsync(connection, command, cancellationToken);
    }

    public async Task<SceneBeatProductionPlan?> GetCurrentAsync(
        string catalogueId,
        string beatId,
        CancellationToken cancellationToken = default)
    {
        Require(catalogueId, "Catalogue id");
        Require(beatId, "Beat id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreatePlanSelect(connection);
        command.CommandText += """
             WHERE CatalogueId = $catalogueId AND BeatId = $beatId
               AND Status NOT IN ('Superseded', 'Cancelled')
             ORDER BY Version DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$catalogueId", catalogueId.Trim());
        command.Parameters.AddWithValue("$beatId", beatId.Trim());
        return await ReadPlanAsync(connection, command, cancellationToken);
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
        string planId,
        string attemptId,
        string modelIdentifier,
        string providerName,
        DateTime startedUtc,
        CancellationToken cancellationToken = default)
    {
        Require(modelIdentifier, "Model identifier");
        Require(providerName, "Provider name");
        return TransitionAsync(
            planId,
            attemptId,
            "Processing",
            "Processing",
            startedUtc,
            "Status = 'Pending'",
            "Status = 'Queued'",
            (plan, attempt) =>
            {
                plan.CommandText += ", ModelIdentifier = $modelIdentifier, ProviderName = $providerName, StartedUtc = $startedUtc";
                plan.Parameters.AddWithValue("$modelIdentifier", modelIdentifier.Trim());
                plan.Parameters.AddWithValue("$providerName", providerName.Trim());
                plan.Parameters.AddWithValue("$startedUtc", FormatUtc(startedUtc));
                attempt.CommandText += ", StartedUtc = $startedUtc";
                attempt.Parameters.AddWithValue("$startedUtc", FormatUtc(startedUtc));
            },
            cancellationToken);
    }

    public async Task<bool> TryCompleteAttemptAsync(
        string planId,
        SceneBeatAnalysisAttempt attempt,
        SceneBeatProductionPlanData data,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalAttempt(planId, attempt);
        ArgumentNullException.ThrowIfNull(data);
        ValidateData(planId, data);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await OwnsAsync(connection, (SqliteTransaction)transaction, planId, attempt.Id, "Processing", "Processing", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        foreach (var cue in data.DialogueCues)
            await InsertProjectionAsync(connection, (SqliteTransaction)transaction, "SceneBeatDialogueCues", cue.Id, planId, cue.Order, cue.Kind.ToString(), cue, cancellationToken);
        foreach (var cue in data.SoundCues)
            await InsertProjectionAsync(connection, (SqliteTransaction)transaction, "SceneBeatSoundCues", cue.Id, planId, cue.Order, cue.Kind.ToString(), cue, cancellationToken);
        for (var index = 0; index < data.VideoCoveragePlans.Count; index++)
        {
            var coverage = data.VideoCoveragePlans[index];
            await InsertProjectionAsync(connection, (SqliteTransaction)transaction, "SceneVideoCoveragePlans", coverage.Id, planId, index + 1, coverage.CoverageKind.ToString(), coverage, cancellationToken);
        }

        await using (var updateAttempt = connection.CreateCommand())
        {
            updateAttempt.Transaction = (SqliteTransaction)transaction;
            updateAttempt.CommandText = """
                UPDATE SceneBeatProductionAttempts SET Status = 'Complete', RawModelResponse = $raw,
                    ReasoningContent = $reasoning, FinishReason = $finishReason, ValidationCode = NULL,
                    ValidationDetailsJson = $validationDetails, DurationMs = $durationMs,
                    OutputCharacters = $outputCharacters, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $attemptId AND OwnerRecordId = $planId AND Status = 'Processing';
                """;
            AddAttemptResultParameters(updateAttempt, planId, attempt, completedUtc);
            if (await updateAttempt.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await using (var updatePlan = connection.CreateCommand())
        {
            updatePlan.Transaction = (SqliteTransaction)transaction;
            updatePlan.CommandText = """
                UPDATE SceneBeatProductionPlans SET Status = 'Complete', NarrativeArcJson = $narrativeArc,
                    TimelineJson = $timeline, NarrationCuesJson = $narration, DialogueCuesJson = $dialogue,
                    AmbiencePlanJson = $ambience, SoundEventCuesJson = $soundEvents, MusicPlanJson = $music,
                    ActionArcJson = $actionArc, StartContinuityJson = $startContinuity,
                    EndContinuityJson = $endContinuity, TypedReferencesJson = $references,
                    VideoCoveragePlansJson = $videoCoverage, ErrorCode = NULL, ErrorMessage = NULL,
                    CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
                WHERE Id = $planId AND CurrentAttemptId = $attemptId AND Status = 'Processing';
                """;
            AddDataParameters(updatePlan, planId, attempt.Id, data, completedUtc);
            if (await updatePlan.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryFailAttemptAsync(
        string planId,
        SceneBeatAnalysisAttempt attempt,
        string errorCode,
        string errorMessage,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalAttempt(planId, attempt);
        Require(errorCode, "Error code");
        Require(errorMessage, "Error message");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await OwnsAsync(connection, (SqliteTransaction)transaction, planId, attempt.Id, "Pending', 'Processing", "Queued', 'Processing", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updateAttempt = connection.CreateCommand();
        updateAttempt.Transaction = (SqliteTransaction)transaction;
        updateAttempt.CommandText = """
            UPDATE SceneBeatProductionAttempts SET Status = 'Failed', RawModelResponse = $raw,
                ReasoningContent = $reasoning, FinishReason = $finishReason, ValidationCode = $validationCode,
                ValidationDetailsJson = $validationDetails, DurationMs = $durationMs,
                OutputCharacters = $outputCharacters, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
            WHERE Id = $attemptId AND OwnerRecordId = $planId AND Status IN ('Queued', 'Processing');
            """;
        AddAttemptResultParameters(updateAttempt, planId, attempt, completedUtc);
        if (await updateAttempt.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updatePlan = connection.CreateCommand();
        updatePlan.Transaction = (SqliteTransaction)transaction;
        updatePlan.CommandText = """
            UPDATE SceneBeatProductionPlans SET Status = 'Failed', ErrorCode = $errorCode,
                ErrorMessage = $errorMessage, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
            WHERE Id = $planId AND CurrentAttemptId = $attemptId AND Status IN ('Pending', 'Processing');
            """;
        updatePlan.Parameters.AddWithValue("$planId", planId.Trim());
        updatePlan.Parameters.AddWithValue("$attemptId", attempt.Id.Trim());
        updatePlan.Parameters.AddWithValue("$errorCode", errorCode.Trim());
        updatePlan.Parameters.AddWithValue("$errorMessage", errorMessage.Trim());
        updatePlan.Parameters.AddWithValue("$completedUtc", FormatUtc(completedUtc));
        if (await updatePlan.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> TryCancelCurrentAsync(
        string planId,
        string attemptId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            planId,
            attemptId,
            "Cancelled",
            "Cancelled",
            cancelledUtc,
            "Status IN ('Pending', 'Processing')",
            "Status IN ('Queued', 'Processing')",
            (plan, attempt) =>
            {
                plan.CommandText += ", CompletedUtc = $completedUtc";
                attempt.CommandText += ", CompletedUtc = $completedUtc";
                plan.Parameters.AddWithValue("$completedUtc", FormatUtc(cancelledUtc));
                attempt.Parameters.AddWithValue("$completedUtc", FormatUtc(cancelledUtc));
            },
            cancellationToken);

    private async Task<bool> TransitionAsync(
        string planId,
        string attemptId,
        string nextPlanStatus,
        string nextAttemptStatus,
        DateTime updatedUtc,
        string planPredicate,
        string attemptPredicate,
        Action<SqliteCommand, SqliteCommand> decorate,
        CancellationToken cancellationToken)
    {
        Require(planId, "Beat Production Plan id");
        Require(attemptId, "Attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var updatePlan = connection.CreateCommand();
        await using var updateAttempt = connection.CreateCommand();
        updatePlan.Transaction = (SqliteTransaction)transaction;
        updateAttempt.Transaction = (SqliteTransaction)transaction;
        updatePlan.CommandText = "UPDATE SceneBeatProductionPlans SET Status = $nextStatus, UpdatedUtc = $updatedUtc";
        updateAttempt.CommandText = "UPDATE SceneBeatProductionAttempts SET Status = $nextAttemptStatus, UpdatedUtc = $updatedUtc";
        decorate(updatePlan, updateAttempt);
        updatePlan.CommandText += $" WHERE Id = $planId AND CurrentAttemptId = $attemptId AND {planPredicate};";
        updateAttempt.CommandText += $" WHERE Id = $attemptId AND OwnerRecordId = $planId AND {attemptPredicate};";
        foreach (var command in new[] { updatePlan, updateAttempt })
        {
            command.Parameters.AddWithValue("$planId", planId.Trim());
            command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
            command.Parameters.AddWithValue("$updatedUtc", FormatUtc(updatedUtc));
        }
        updatePlan.Parameters.AddWithValue("$nextStatus", nextPlanStatus);
        updateAttempt.Parameters.AddWithValue("$nextAttemptStatus", nextAttemptStatus);
        if (await updatePlan.ExecuteNonQueryAsync(cancellationToken) != 1
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

    private static SqliteCommand CreatePlanSelect(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CatalogueId, BeatId, CatalogueVersion, Version, Status, CurrentAttemptId,
                   SchemaVersion, PromptContractVersion, SourceSnapshotJson, NarrativeArcJson,
                   TimelineJson, NarrationCuesJson, DialogueCuesJson, AmbiencePlanJson,
                   SoundEventCuesJson, MusicPlanJson, ActionArcJson, StartContinuityJson,
                   EndContinuityJson, TypedReferencesJson, VideoCoveragePlansJson,
                   ModelIdentifier, ProviderName, ExecutionSettingsJson, ErrorCode, ErrorMessage,
                   CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM SceneBeatProductionPlans
            """;
        return command;
    }

    private static async Task<SceneBeatProductionPlan?> ReadPlanAsync(
        SqliteConnection connection,
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var plan = new SceneBeatProductionPlan
        {
            Id = reader.GetString(0),
            CatalogueId = reader.GetString(1),
            BeatId = reader.GetString(2),
            CatalogueVersion = reader.GetInt32(3),
            Version = reader.GetInt32(4),
            Status = Enum.Parse<SceneBeatCatalogueStatus>(reader.GetString(5)),
            CurrentAttemptId = reader.IsDBNull(6) ? null : reader.GetString(6),
            SchemaVersion = reader.GetInt32(7),
            PromptContractVersion = reader.GetString(8),
            SourceSnapshotJson = reader.GetString(9),
            NarrativeArcJson = reader.GetString(10),
            TimelineJson = reader.GetString(11),
            NarrationCuesJson = reader.GetString(12),
            DialogueCuesJson = reader.GetString(13),
            AmbiencePlanJson = reader.GetString(14),
            SoundEventCuesJson = reader.GetString(15),
            MusicPlanJson = reader.GetString(16),
            ActionArcJson = reader.GetString(17),
            StartContinuityJson = reader.GetString(18),
            EndContinuityJson = reader.GetString(19),
            TypedReferencesJson = reader.GetString(20),
            VideoCoveragePlansJson = reader.GetString(21),
            ModelIdentifier = reader.IsDBNull(22) ? null : reader.GetString(22),
            ProviderName = reader.IsDBNull(23) ? null : reader.GetString(23),
            ExecutionSettingsJson = reader.GetString(24),
            ErrorCode = reader.IsDBNull(25) ? null : reader.GetString(25),
            ErrorMessage = reader.IsDBNull(26) ? null : reader.GetString(26),
            CreatedUtc = ParseUtc(reader.GetString(27)),
            StartedUtc = reader.IsDBNull(28) ? null : ParseUtc(reader.GetString(28)),
            CompletedUtc = reader.IsDBNull(29) ? null : ParseUtc(reader.GetString(29)),
            UpdatedUtc = ParseUtc(reader.GetString(30))
        };
        await reader.DisposeAsync();
        plan.DialogueCues = await LoadProjectionAsync<SceneBeatDialogueCue>(connection, "SceneBeatDialogueCues", plan.Id, cancellationToken);
        plan.SoundCues = await LoadProjectionAsync<SceneBeatSoundCue>(connection, "SceneBeatSoundCues", plan.Id, cancellationToken);
        plan.VideoCoveragePlans = await LoadProjectionAsync<SceneVideoCoveragePlan>(connection, "SceneVideoCoveragePlans", plan.Id, cancellationToken);
        return plan;
    }

    private static async Task<IReadOnlyList<T>> LoadProjectionAsync<T>(
        SqliteConnection connection,
        string table,
        string planId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT PayloadJson FROM {table} WHERE BeatProductionPlanId = $planId ORDER BY ItemOrder;";
        command.Parameters.AddWithValue("$planId", planId);
        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions)
                ?? throw new InvalidOperationException($"Invalid {typeof(T).Name} projection for Beat Production Plan '{planId}'."));
        return results;
    }

    private static async Task InsertProjectionAsync<T>(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string id,
        string planId,
        int order,
        string kind,
        T value,
        CancellationToken cancellationToken)
    {
        Require(id, "Projection id");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {table} (Id, BeatProductionPlanId, ItemOrder, Kind, PayloadJson) VALUES ($id, $planId, $order, $kind, $payload);";
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$planId", planId.Trim());
        command.Parameters.AddWithValue("$order", order);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(value, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            INSERT INTO SceneBeatProductionAttempts (
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
        string planId,
        string attemptId,
        string planStatuses,
        string attemptStatuses,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT COUNT(*) FROM SceneBeatProductionPlans p
            JOIN SceneBeatProductionAttempts a ON a.Id = p.CurrentAttemptId AND a.OwnerRecordId = p.Id
            WHERE p.Id = $planId AND p.CurrentAttemptId = $attemptId
              AND p.Status IN ('{planStatuses}') AND a.Status IN ('{attemptStatuses}');
            """;
        command.Parameters.AddWithValue("$planId", planId.Trim());
        command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private const string AttemptSelect = """
        SELECT Id, OwnerRecordId, AttemptNumber, JobId, Status, SystemPrompt, UserPrompt,
               RawModelResponse, ReasoningContent, FinishReason, ValidationCode, ValidationDetailsJson,
               DurationMs, InputCharacters, OutputCharacters, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
        FROM SceneBeatProductionAttempts
        """;

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

    private static void AddAttemptResultParameters(
        SqliteCommand command,
        string planId,
        SceneBeatAnalysisAttempt attempt,
        DateTime completedUtc)
    {
        command.Parameters.AddWithValue("$planId", planId.Trim());
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

    private static void AddDataParameters(
        SqliteCommand command,
        string planId,
        string attemptId,
        SceneBeatProductionPlanData data,
        DateTime completedUtc)
    {
        command.Parameters.AddWithValue("$planId", planId.Trim());
        command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
        command.Parameters.AddWithValue("$narrativeArc", data.NarrativeArcJson);
        command.Parameters.AddWithValue("$timeline", data.TimelineJson);
        command.Parameters.AddWithValue("$narration", data.NarrationCuesJson);
        command.Parameters.AddWithValue("$dialogue", data.DialogueCuesJson);
        command.Parameters.AddWithValue("$ambience", data.AmbiencePlanJson);
        command.Parameters.AddWithValue("$soundEvents", data.SoundEventCuesJson);
        command.Parameters.AddWithValue("$music", data.MusicPlanJson);
        command.Parameters.AddWithValue("$actionArc", data.ActionArcJson);
        command.Parameters.AddWithValue("$startContinuity", data.StartContinuityJson);
        command.Parameters.AddWithValue("$endContinuity", data.EndContinuityJson);
        command.Parameters.AddWithValue("$references", data.TypedReferencesJson);
        command.Parameters.AddWithValue("$videoCoverage", data.VideoCoveragePlansJson);
        command.Parameters.AddWithValue("$completedUtc", FormatUtc(completedUtc));
    }

    private static void ValidateNewVersion(SceneBeatProductionPlan plan, SceneBeatAnalysisAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(attempt);
        Require(plan.Id, "Beat Production Plan id");
        Require(plan.CatalogueId, "Catalogue id");
        Require(plan.BeatId, "Beat id");
        Require(plan.PromptContractVersion, "Prompt contract version");
        Require(plan.SourceSnapshotJson, "Source snapshot JSON");
        Require(plan.ExecutionSettingsJson, "Execution settings JSON");
        if (plan.CatalogueVersion < 1 || plan.Version < 0 || plan.SchemaVersion < 1)
            throw new InvalidOperationException("Catalogue/schema versions must be positive and plan version cannot be negative.");
        if (plan.Status != SceneBeatCatalogueStatus.Pending || plan.CurrentAttemptId != attempt.Id)
            throw new InvalidOperationException("A new Beat Production Plan must be pending and owned by its initial attempt.");
        if (attempt.OwnerRecordId != plan.Id || attempt.Status != SceneBeatAnalysisAttemptStatus.Queued)
            throw new InvalidOperationException("The initial Beat Production attempt does not own the plan.");
        Require(attempt.JobId, "Attempt job id");
        Require(attempt.SystemPrompt, "Attempt system prompt");
        Require(attempt.UserPrompt, "Attempt user prompt");
        Require(attempt.ValidationDetailsJson, "Attempt validation details JSON");
    }

    private static void ValidateTerminalAttempt(string planId, SceneBeatAnalysisAttempt attempt)
    {
        Require(planId, "Beat Production Plan id");
        ArgumentNullException.ThrowIfNull(attempt);
        if (!string.Equals(planId.Trim(), attempt.OwnerRecordId, StringComparison.Ordinal))
            throw new InvalidOperationException("The attempt does not own the Beat Production Plan.");
        Require(attempt.ValidationDetailsJson, "Attempt validation details JSON");
    }

    private static void ValidateData(string planId, SceneBeatProductionPlanData data)
    {
        foreach (var json in new[]
        {
            data.NarrativeArcJson, data.TimelineJson, data.NarrationCuesJson, data.DialogueCuesJson,
            data.AmbiencePlanJson, data.SoundEventCuesJson, data.MusicPlanJson, data.ActionArcJson,
            data.StartContinuityJson, data.EndContinuityJson, data.TypedReferencesJson, data.VideoCoveragePlansJson
        }) Require(json, "Beat Production Plan JSON projection");
        if (data.DialogueCues.Any(cue => cue.BeatProductionPlanId != planId)
            || data.SoundCues.Any(cue => cue.BeatProductionPlanId != planId)
            || data.VideoCoveragePlans.Any(coverage => coverage.BeatProductionPlanId != planId))
            throw new InvalidOperationException("Beat Production Plan projections must belong to the completed plan.");
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
    }

    private static string FormatUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value.ToString("O", CultureInfo.InvariantCulture)
            : throw new InvalidOperationException("Persistence timestamps must be UTC.");

    private static DateTime ParseUtc(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SceneBeatProductionPlans (
            Id TEXT PRIMARY KEY, CatalogueId TEXT NOT NULL, BeatId TEXT NOT NULL,
            CatalogueVersion INTEGER NOT NULL, Version INTEGER NOT NULL, Status TEXT NOT NULL,
            CurrentAttemptId TEXT NULL, SchemaVersion INTEGER NOT NULL, PromptContractVersion TEXT NOT NULL,
            SourceSnapshotJson TEXT NOT NULL, NarrativeArcJson TEXT NOT NULL, TimelineJson TEXT NOT NULL,
            NarrationCuesJson TEXT NOT NULL, DialogueCuesJson TEXT NOT NULL, AmbiencePlanJson TEXT NOT NULL,
            SoundEventCuesJson TEXT NOT NULL, MusicPlanJson TEXT NOT NULL, ActionArcJson TEXT NOT NULL,
            StartContinuityJson TEXT NOT NULL, EndContinuityJson TEXT NOT NULL,
            TypedReferencesJson TEXT NOT NULL, VideoCoveragePlansJson TEXT NOT NULL,
            ModelIdentifier TEXT NULL, ProviderName TEXT NULL, ExecutionSettingsJson TEXT NOT NULL,
            ErrorCode TEXT NULL, ErrorMessage TEXT NULL, CreatedUtc TEXT NOT NULL, StartedUtc TEXT NULL,
            CompletedUtc TEXT NULL, UpdatedUtc TEXT NOT NULL,
            UNIQUE (CatalogueId, BeatId, Version)
        );
        CREATE INDEX IF NOT EXISTS IX_SceneBeatProductionPlans_Parent
            ON SceneBeatProductionPlans (CatalogueId, BeatId, Version DESC);
        CREATE TABLE IF NOT EXISTS SceneBeatProductionAttempts (
            Id TEXT PRIMARY KEY, OwnerRecordId TEXT NOT NULL, AttemptNumber INTEGER NOT NULL,
            JobId TEXT NOT NULL, Status TEXT NOT NULL, SystemPrompt TEXT NOT NULL, UserPrompt TEXT NOT NULL,
            RawModelResponse TEXT NULL, ReasoningContent TEXT NULL, FinishReason TEXT NULL,
            ValidationCode TEXT NULL, ValidationDetailsJson TEXT NOT NULL, DurationMs INTEGER NULL,
            InputCharacters INTEGER NOT NULL, OutputCharacters INTEGER NULL, CreatedUtc TEXT NOT NULL,
            StartedUtc TEXT NULL, CompletedUtc TEXT NULL, UpdatedUtc TEXT NOT NULL,
            UNIQUE (OwnerRecordId, AttemptNumber)
        );
        CREATE TABLE IF NOT EXISTS SceneBeatDialogueCues (
            Id TEXT PRIMARY KEY, BeatProductionPlanId TEXT NOT NULL, ItemOrder INTEGER NOT NULL,
            Kind TEXT NOT NULL, PayloadJson TEXT NOT NULL, UNIQUE (BeatProductionPlanId, ItemOrder)
        );
        CREATE TABLE IF NOT EXISTS SceneBeatSoundCues (
            Id TEXT PRIMARY KEY, BeatProductionPlanId TEXT NOT NULL, ItemOrder INTEGER NOT NULL,
            Kind TEXT NOT NULL, PayloadJson TEXT NOT NULL, UNIQUE (BeatProductionPlanId, ItemOrder)
        );
        CREATE TABLE IF NOT EXISTS SceneVideoCoveragePlans (
            Id TEXT PRIMARY KEY, BeatProductionPlanId TEXT NOT NULL, ItemOrder INTEGER NOT NULL,
            Kind TEXT NOT NULL, PayloadJson TEXT NOT NULL, UNIQUE (BeatProductionPlanId, ItemOrder)
        );
        """;
}