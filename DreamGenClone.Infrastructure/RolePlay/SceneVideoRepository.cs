using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite-backed store for composed clips (B-152). The schema is created idempotently on every open, so the table
/// exists before the first read without an ordering requirement on startup - the same posture as the scene-LoRA
/// catalog.
///
/// <para>
/// Statuses are stored as INTEGER and every enum is validated on read, so a row written by a newer build fails
/// loudly instead of being read as a different state. Timestamps are stored as round-trip strings.
/// </para>
/// </summary>
public sealed class SceneVideoRepository : ISceneVideoRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SceneVideos (
            Id TEXT PRIMARY KEY,
            Title TEXT NOT NULL,
            SessionId TEXT NULL,
            InteractionId TEXT NULL,
            Status INTEGER NOT NULL,
            OriginKind INTEGER NOT NULL,
            OriginImageId TEXT NULL,
            OriginCoveragePlanId TEXT NULL,
            FileRelativePath TEXT NULL,
            NormalizedFileRelativePath TEXT NULL,
            PromptSnapshot TEXT NOT NULL,
            PromptManuallyEdited INTEGER NOT NULL,
            CompilerKey TEXT NULL,
            CompilerVersion TEXT NULL,
            SettingsJson TEXT NOT NULL,
            ReferencesJson TEXT NULL,
            LoraStackJson TEXT NULL,
            ModelIdentifier TEXT NULL,
            RequestedModelId TEXT NULL,
            ProviderName TEXT NULL,
            Seed INTEGER NOT NULL,
            Width INTEGER NOT NULL,
            Height INTEGER NOT NULL,
            Length INTEGER NOT NULL,
            Steps INTEGER NOT NULL,
            Fps INTEGER NOT NULL,
            RefImageSize TEXT NOT NULL,
            JobId TEXT NULL,
            ErrorMessage TEXT NULL,
            VideoStreamPresent INTEGER NULL,
            AudioStreamPresent INTEGER NULL,
            MeasuredLoudnessLufs REAL NULL,
            LoudnessTargetLufs REAL NULL,
            MeasuredDurationSeconds REAL NULL,
            VerificationNotes TEXT NULL,
            SourceVideoId TEXT NULL,
            ContinuationKind INTEGER NULL,
            GuideFrameIndex INTEGER NULL,
            SourceFrameRelativePath TEXT NULL,
            SourceFrameSha256 TEXT NULL,
            DriftMetricsJson TEXT NULL,
            CreatedUtc TEXT NOT NULL,
            StartedUtc TEXT NULL,
            CompletedUtc TEXT NULL,
            UpdatedUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_SceneVideos_Recent ON SceneVideos (CreatedUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_SceneVideos_Session ON SceneVideos (SessionId, CreatedUtc DESC);
        """;

    /// <summary>
    /// The index over the B-156 link column. It is deliberately kept out of <see cref="SchemaSql"/>: on a database
    /// created before B-156 the column only exists after <see cref="EnsureContinuationColumnsAsync"/> has run, and a
    /// <c>CREATE INDEX</c> naming a missing column fails the whole schema batch.
    /// </summary>
    private const string ContinuationIndexSql =
        "CREATE INDEX IF NOT EXISTS IX_SceneVideos_SourceVideo ON SceneVideos (SourceVideoId);";

    /// <summary>
    /// The B-156 continuation columns, added to databases that were created before the feature existed.
    /// <c>CREATE TABLE IF NOT EXISTS</c> does not add columns to an existing table, so each one is checked and
    /// added individually - the same additive-migration idiom the main persistence schema uses.
    /// </summary>
    private static readonly (string Column, string Definition)[] ContinuationColumns =
    [
        ("SourceVideoId", "TEXT NULL"),
        ("ContinuationKind", "INTEGER NULL"),
        ("GuideFrameIndex", "INTEGER NULL"),
        ("SourceFrameRelativePath", "TEXT NULL"),
        ("SourceFrameSha256", "TEXT NULL"),
        ("DriftMetricsJson", "TEXT NULL")
    ];

    private readonly string _connectionString;

    public SceneVideoRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var _ = await OpenAsync(cancellationToken);
    }

    public async Task InsertAsync(SceneVideoRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneVideos (
                Id, Title, SessionId, InteractionId, Status, OriginKind, OriginImageId, OriginCoveragePlanId,
                FileRelativePath, NormalizedFileRelativePath, PromptSnapshot, PromptManuallyEdited,
                CompilerKey, CompilerVersion, SettingsJson, ReferencesJson, LoraStackJson,
                ModelIdentifier, RequestedModelId, ProviderName, Seed, Width, Height, Length, Steps, Fps,
                RefImageSize, JobId, ErrorMessage,
                VideoStreamPresent, AudioStreamPresent, MeasuredLoudnessLufs, LoudnessTargetLufs,
                MeasuredDurationSeconds, VerificationNotes,
                SourceVideoId, ContinuationKind, GuideFrameIndex,
                SourceFrameRelativePath, SourceFrameSha256, DriftMetricsJson,
                CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
            VALUES (
                $id, $title, $sessionId, $interactionId, $status, $originKind, $originImageId, $originCoveragePlanId,
                $fileRelativePath, $normalizedFileRelativePath, $promptSnapshot, $promptManuallyEdited,
                $compilerKey, $compilerVersion, $settingsJson, $referencesJson, $loraStackJson,
                $modelIdentifier, $requestedModelId, $providerName, $seed, $width, $height, $length, $steps, $fps,
                $refImageSize, $jobId, $errorMessage,
                $videoStreamPresent, $audioStreamPresent, $measuredLoudnessLufs, $loudnessTargetLufs,
                $measuredDurationSeconds, $verificationNotes,
                $sourceVideoId, $continuationKind, $guideFrameIndex,
                $sourceFrameRelativePath, $sourceFrameSha256, $driftMetricsJson,
                $createdUtc, $startedUtc, $completedUtc, $updatedUtc);
            """;
        AddRecordParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneVideoRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<bool> TryClaimAsync(string id, DateTime startedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneVideos
            SET Status = $rendering, StartedUtc = $startedUtc, UpdatedUtc = $startedUtc
            WHERE Id = $id AND Status = $pending;
            """;
        command.Parameters.AddWithValue("$rendering", (int)SceneVideoStatus.Rendering);
        command.Parameters.AddWithValue("$pending", (int)SceneVideoStatus.Pending);
        command.Parameters.AddWithValue("$startedUtc", ToText(startedUtc));
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryCompleteAsync(SceneVideoRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneVideos
            SET Status = $status, FileRelativePath = $fileRelativePath,
                NormalizedFileRelativePath = $normalizedFileRelativePath,
                ModelIdentifier = $modelIdentifier, ProviderName = $providerName,
                VideoStreamPresent = $videoStreamPresent, AudioStreamPresent = $audioStreamPresent,
                MeasuredLoudnessLufs = $measuredLoudnessLufs, LoudnessTargetLufs = $loudnessTargetLufs,
                MeasuredDurationSeconds = $measuredDurationSeconds, VerificationNotes = $verificationNotes,
                DriftMetricsJson = $driftMetricsJson,
                ErrorMessage = NULL, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
            WHERE Id = $id AND Status = $rendering;
            """;
        command.Parameters.AddWithValue("$status", (int)SceneVideoStatus.Complete);
        command.Parameters.AddWithValue("$fileRelativePath", (object?)record.FileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$normalizedFileRelativePath", (object?)record.NormalizedFileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelIdentifier", (object?)record.ModelIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerName", (object?)record.ProviderName ?? DBNull.Value);
        command.Parameters.AddWithValue("$videoStreamPresent", (object?)(record.VideoStreamPresent is null ? null : record.VideoStreamPresent.Value ? 1 : 0) ?? DBNull.Value);
        command.Parameters.AddWithValue("$audioStreamPresent", (object?)(record.AudioStreamPresent is null ? null : record.AudioStreamPresent.Value ? 1 : 0) ?? DBNull.Value);
        command.Parameters.AddWithValue("$measuredLoudnessLufs", (object?)record.MeasuredLoudnessLufs ?? DBNull.Value);
        command.Parameters.AddWithValue("$loudnessTargetLufs", (object?)record.LoudnessTargetLufs ?? DBNull.Value);
        command.Parameters.AddWithValue("$measuredDurationSeconds", (object?)record.MeasuredDurationSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$verificationNotes", (object?)record.VerificationNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("$driftMetricsJson", (object?)record.DriftMetricsJson ?? DBNull.Value);
        var completedUtc = record.CompletedUtc ?? DateTime.UtcNow;
        command.Parameters.AddWithValue("$completedUtc", ToText(completedUtc));
        // The WHERE clause guards on the claimed status, so this parameter must be bound here too. Omitting it made
        // every successful render throw at the completion step and fail a clip that was already on disk.
        // The WHERE clause guards on the claimed status, so this parameter must be bound here too. Omitting it made
        // every successful render throw at the completion step and fail a clip that was already on disk.
        command.Parameters.AddWithValue("$rendering", (int)SceneVideoStatus.Rendering);
        command.Parameters.AddWithValue("$id", record.Id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryFailAsync(SceneVideoRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneVideos
            SET Status = $status, ErrorMessage = $errorMessage, CompletedUtc = $completedUtc, UpdatedUtc = $completedUtc
            WHERE Id = $id AND Status IN ($pending, $rendering);
            """;
        command.Parameters.AddWithValue("$status", (int)SceneVideoStatus.Failed);
        command.Parameters.AddWithValue("$errorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
        var completedUtc = record.CompletedUtc ?? DateTime.UtcNow;
        command.Parameters.AddWithValue("$completedUtc", ToText(completedUtc));
        command.Parameters.AddWithValue("$pending", (int)SceneVideoStatus.Pending);
        command.Parameters.AddWithValue("$rendering", (int)SceneVideoStatus.Rendering);
        command.Parameters.AddWithValue("$id", record.Id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryCancelAsync(string id, DateTime cancelledUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneVideos
            SET Status = $cancelled, CompletedUtc = $cancelledUtc, UpdatedUtc = $cancelledUtc
            WHERE Id = $id AND Status IN ($pending, $rendering);
            """;
        command.Parameters.AddWithValue("$cancelled", (int)SceneVideoStatus.Cancelled);
        command.Parameters.AddWithValue("$cancelledUtc", ToText(cancelledUtc));
        command.Parameters.AddWithValue("$pending", (int)SceneVideoStatus.Pending);
        command.Parameters.AddWithValue("$rendering", (int)SceneVideoStatus.Rendering);
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<SceneVideoRecord>> ListRecentAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} ORDER BY CreatedUtc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);

        var rows = new List<SceneVideoRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async Task<IReadOnlyList<SceneVideoRecord>> ListBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE SessionId = $sessionId ORDER BY CreatedUtc DESC;";
        command.Parameters.AddWithValue("$sessionId", sessionId);

        var rows = new List<SceneVideoRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async Task<IReadOnlyList<SceneVideoRecord>> ListContinuationsAsync(
        string sourceVideoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceVideoId))
        {
            throw new InvalidOperationException(
                "A lineage lookup needs the source composition id; an empty id would list unrelated clips.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE SourceVideoId = $sourceVideoId ORDER BY CreatedUtc DESC;";
        command.Parameters.AddWithValue("$sourceVideoId", sourceVideoId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<SceneVideoRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SceneVideos WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectColumns = """
        SELECT Id, Title, SessionId, InteractionId, Status, OriginKind, OriginImageId, OriginCoveragePlanId,
               FileRelativePath, NormalizedFileRelativePath, PromptSnapshot, PromptManuallyEdited,
               CompilerKey, CompilerVersion, SettingsJson, ReferencesJson, LoraStackJson,
               ModelIdentifier, RequestedModelId, ProviderName, Seed, Width, Height, Length, Steps, Fps,
               RefImageSize, JobId, ErrorMessage,
               VideoStreamPresent, AudioStreamPresent, MeasuredLoudnessLufs, LoudnessTargetLufs,
               MeasuredDurationSeconds, VerificationNotes,
               SourceVideoId, ContinuationKind, GuideFrameIndex,
               SourceFrameRelativePath, SourceFrameSha256, DriftMetricsJson,
               CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
        FROM SceneVideos
        """;

    private static void AddRecordParameters(SqliteCommand command, SceneVideoRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$sessionId", (object?)record.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$interactionId", (object?)record.InteractionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)record.Status);
        command.Parameters.AddWithValue("$originKind", (int)record.OriginKind);
        command.Parameters.AddWithValue("$originImageId", (object?)record.OriginImageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$originCoveragePlanId", (object?)record.OriginCoveragePlanId ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileRelativePath", (object?)record.FileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$normalizedFileRelativePath", (object?)record.NormalizedFileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptSnapshot", record.PromptSnapshot);
        command.Parameters.AddWithValue("$promptManuallyEdited", record.PromptManuallyEdited ? 1 : 0);
        command.Parameters.AddWithValue("$compilerKey", (object?)record.CompilerKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$compilerVersion", (object?)record.CompilerVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$settingsJson", record.SettingsJson);
        command.Parameters.AddWithValue("$referencesJson", (object?)record.ReferencesJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$loraStackJson", (object?)record.LoraStackJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelIdentifier", (object?)record.ModelIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$requestedModelId", (object?)record.RequestedModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerName", (object?)record.ProviderName ?? DBNull.Value);
        command.Parameters.AddWithValue("$seed", record.Seed);
        command.Parameters.AddWithValue("$width", record.Width);
        command.Parameters.AddWithValue("$height", record.Height);
        command.Parameters.AddWithValue("$length", record.Length);
        command.Parameters.AddWithValue("$steps", record.Steps);
        command.Parameters.AddWithValue("$fps", record.Fps);
        command.Parameters.AddWithValue("$refImageSize", record.RefImageSize);
        command.Parameters.AddWithValue("$jobId", (object?)record.JobId ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$videoStreamPresent", (object?)ToNullableInt(record.VideoStreamPresent) ?? DBNull.Value);
        command.Parameters.AddWithValue("$audioStreamPresent", (object?)ToNullableInt(record.AudioStreamPresent) ?? DBNull.Value);
        command.Parameters.AddWithValue("$measuredLoudnessLufs", (object?)record.MeasuredLoudnessLufs ?? DBNull.Value);
        command.Parameters.AddWithValue("$loudnessTargetLufs", (object?)record.LoudnessTargetLufs ?? DBNull.Value);
        command.Parameters.AddWithValue("$measuredDurationSeconds", (object?)record.MeasuredDurationSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$verificationNotes", (object?)record.VerificationNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", ToText(record.CreatedUtc));
        command.Parameters.AddWithValue("$startedUtc", (object?)(record.StartedUtc is { } started ? ToText(started) : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", (object?)(record.CompletedUtc is { } completed ? ToText(completed) : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", ToText(record.UpdatedUtc));
        command.Parameters.AddWithValue("$sourceVideoId", (object?)record.SourceVideoId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$continuationKind",
            record.ContinuationKind is { } kind ? (object)(int)kind : DBNull.Value);
        command.Parameters.AddWithValue("$guideFrameIndex", (object?)record.GuideFrameIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceFrameRelativePath", (object?)record.SourceFrameRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceFrameSha256", (object?)record.SourceFrameSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$driftMetricsJson", (object?)record.DriftMetricsJson ?? DBNull.Value);
    }

    private static SceneVideoRecord Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Title = reader.GetString(1),
        SessionId = reader.IsDBNull(2) ? null : reader.GetString(2),
        InteractionId = reader.IsDBNull(3) ? null : reader.GetString(3),
        Status = ReadEnum<SceneVideoStatus>(reader, 4, "Status"),
        OriginKind = ReadEnum<SceneVideoOriginKind>(reader, 5, "OriginKind"),
        OriginImageId = reader.IsDBNull(6) ? null : reader.GetString(6),
        OriginCoveragePlanId = reader.IsDBNull(7) ? null : reader.GetString(7),
        FileRelativePath = reader.IsDBNull(8) ? null : reader.GetString(8),
        NormalizedFileRelativePath = reader.IsDBNull(9) ? null : reader.GetString(9),
        PromptSnapshot = reader.GetString(10),
        PromptManuallyEdited = reader.GetInt32(11) == 1,
        CompilerKey = reader.IsDBNull(12) ? null : reader.GetString(12),
        CompilerVersion = reader.IsDBNull(13) ? null : reader.GetString(13),
        SettingsJson = reader.GetString(14),
        ReferencesJson = reader.IsDBNull(15) ? null : reader.GetString(15),
        LoraStackJson = reader.IsDBNull(16) ? null : reader.GetString(16),
        ModelIdentifier = reader.IsDBNull(17) ? null : reader.GetString(17),
        RequestedModelId = reader.IsDBNull(18) ? null : reader.GetString(18),
        ProviderName = reader.IsDBNull(19) ? null : reader.GetString(19),
        Seed = reader.GetInt64(20),
        Width = reader.GetInt32(21),
        Height = reader.GetInt32(22),
        Length = reader.GetInt32(23),
        Steps = reader.GetInt32(24),
        Fps = reader.GetInt32(25),
        RefImageSize = reader.GetString(26),
        JobId = reader.IsDBNull(27) ? null : reader.GetString(27),
        ErrorMessage = reader.IsDBNull(28) ? null : reader.GetString(28),
        VideoStreamPresent = reader.IsDBNull(29) ? null : reader.GetInt32(29) == 1,
        AudioStreamPresent = reader.IsDBNull(30) ? null : reader.GetInt32(30) == 1,
        MeasuredLoudnessLufs = reader.IsDBNull(31) ? null : reader.GetDouble(31),
        LoudnessTargetLufs = reader.IsDBNull(32) ? null : reader.GetDouble(32),
        MeasuredDurationSeconds = reader.IsDBNull(33) ? null : reader.GetDouble(33),
        VerificationNotes = reader.IsDBNull(34) ? null : reader.GetString(34),
        SourceVideoId = reader.IsDBNull(35) ? null : reader.GetString(35),
        ContinuationKind = reader.IsDBNull(36)
            ? null
            : ReadEnum<SceneVideoContinuationKind>(reader, 36, "ContinuationKind"),
        GuideFrameIndex = reader.IsDBNull(37) ? null : reader.GetInt32(37),
        SourceFrameRelativePath = reader.IsDBNull(38) ? null : reader.GetString(38),
        SourceFrameSha256 = reader.IsDBNull(39) ? null : reader.GetString(39),
        DriftMetricsJson = reader.IsDBNull(40) ? null : reader.GetString(40),
        CreatedUtc = DateTime.Parse(reader.GetString(41), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        StartedUtc = reader.IsDBNull(42) ? null : DateTime.Parse(reader.GetString(42), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        CompletedUtc = reader.IsDBNull(43) ? null : DateTime.Parse(reader.GetString(43), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        UpdatedUtc = DateTime.Parse(reader.GetString(44), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

    private static TEnum ReadEnum<TEnum>(SqliteDataReader reader, int ordinal, string column)
        where TEnum : struct, Enum
    {
        var value = reader.GetInt32(ordinal);
        if (!Enum.IsDefined(typeof(TEnum), value))
        {
            throw new InvalidOperationException(
                $"SceneVideos row '{reader.GetString(0)}' has {column} value {value}, which is not a known value. "
                + "Fix the row rather than letting the composer render a state it cannot represent.");
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }

    private static int? ToNullableInt(bool? value) => value switch
    {
        true => 1,
        false => 0,
        null => null
    };

    private static string ToText(DateTime value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON; " + SchemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureContinuationColumnsAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureContinuationColumnsAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (column, definition) in ContinuationColumns)
        {
            await using var check = connection.CreateCommand();
            check.CommandText =
                $"SELECT COUNT(*) FROM pragma_table_info('SceneVideos') WHERE name = '{column}'";
            if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                continue;
            }

            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE SceneVideos ADD COLUMN {column} {definition}";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var index = connection.CreateCommand();
        index.CommandText = ContinuationIndexSql;
        await index.ExecuteNonQueryAsync(cancellationToken);
    }
}
