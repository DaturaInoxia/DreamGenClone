using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>SQLite persistence for character identity builds and their per-step records.</summary>
public sealed class CharacterIdentityBuildRepository : ICharacterIdentityBuildRepository
{
    private readonly string _connectionString;

    public CharacterIdentityBuildRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
    }

    public async Task UpsertBuildAsync(CharacterIdentityBuild build, CancellationToken cancellationToken = default)
    {
        Require(build.Id, "Build id");
        Require(build.CharacterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityBuilds (Id, CharacterProfileId, BatchId, FrontContainerAssetId, CanonicalFrontAssetId, CurrentStep, Status, CreatedUtc, UpdatedUtc)
            VALUES ($id, $characterProfileId, $batchId, $frontContainerAssetId, $canonicalFrontAssetId, $currentStep, $status, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                CharacterProfileId = excluded.CharacterProfileId,
                BatchId = excluded.BatchId,
                FrontContainerAssetId = excluded.FrontContainerAssetId,
                CanonicalFrontAssetId = excluded.CanonicalFrontAssetId,
                CurrentStep = excluded.CurrentStep,
                Status = excluded.Status,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", build.Id.Trim());
        command.Parameters.AddWithValue("$characterProfileId", build.CharacterProfileId.Trim());
        command.Parameters.AddWithValue("$batchId", (object?)build.BatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$frontContainerAssetId", (object?)build.FrontContainerAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$canonicalFrontAssetId", (object?)build.CanonicalFrontAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$currentStep", (int)build.CurrentStep);
        command.Parameters.AddWithValue("$status", (int)build.Status);
        command.Parameters.AddWithValue("$createdUtc", build.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedUtc", build.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CharacterIdentityBuild?> GetBuildAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Build id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CharacterProfileId, BatchId, FrontContainerAssetId, CurrentStep, Status, CreatedUtc, UpdatedUtc, CanonicalFrontAssetId
            FROM CharacterIdentityBuilds WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBuild(reader) : null;
    }

    public async Task<IReadOnlyList<CharacterIdentityBuild>> ListBuildsAsync(string characterProfileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CharacterProfileId, BatchId, FrontContainerAssetId, CurrentStep, Status, CreatedUtc, UpdatedUtc, CanonicalFrontAssetId
            FROM CharacterIdentityBuilds WHERE CharacterProfileId = $characterProfileId
            ORDER BY CreatedUtc DESC;
            """;
        command.Parameters.AddWithValue("$characterProfileId", characterProfileId.Trim());
        var results = new List<CharacterIdentityBuild>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadBuild(reader));
        }

        return results;
    }

    public async Task UpsertStepAsync(CharacterIdentityBuildStepRecord step, CancellationToken cancellationToken = default)
    {
        Require(step.Id, "Step id");
        Require(step.BuildId, "Build id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityBuildSteps (
                Id, BuildId, Step, Status, InputArtifactId, OutputArtifactId, ResolvedPromptText,
                ResolvedModelId, FailureReason, MirrorDerived, ManualOverrideApplied, ManualOverrideReason,
                ManualOverrideAuthor, ManualOverrideUtc, MeasurementJson, RawToolOutput,
                CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $buildId, $step, $status, $inputArtifactId, $outputArtifactId, $resolvedPromptText,
                $resolvedModelId, $failureReason, $mirrorDerived, $manualOverrideApplied, $manualOverrideReason,
                $manualOverrideAuthor, $manualOverrideUtc, $measurementJson, $rawToolOutput,
                $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status,
                InputArtifactId = excluded.InputArtifactId,
                OutputArtifactId = excluded.OutputArtifactId,
                ResolvedPromptText = excluded.ResolvedPromptText,
                ResolvedModelId = excluded.ResolvedModelId,
                FailureReason = excluded.FailureReason,
                MirrorDerived = excluded.MirrorDerived,
                ManualOverrideApplied = excluded.ManualOverrideApplied,
                ManualOverrideReason = excluded.ManualOverrideReason,
                ManualOverrideAuthor = excluded.ManualOverrideAuthor,
                ManualOverrideUtc = excluded.ManualOverrideUtc,
                MeasurementJson = excluded.MeasurementJson,
                RawToolOutput = excluded.RawToolOutput,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", step.Id.Trim());
        command.Parameters.AddWithValue("$buildId", step.BuildId.Trim());
        command.Parameters.AddWithValue("$step", (int)step.Step);
        command.Parameters.AddWithValue("$status", (int)step.Status);
        command.Parameters.AddWithValue("$inputArtifactId", (object?)step.InputArtifactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputArtifactId", (object?)step.OutputArtifactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedPromptText", (object?)step.ResolvedPromptText ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedModelId", (object?)step.ResolvedModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$failureReason", (object?)step.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$mirrorDerived", step.MirrorDerived ? 1 : 0);
        command.Parameters.AddWithValue("$manualOverrideApplied", step.ManualOverrideApplied ? 1 : 0);
        command.Parameters.AddWithValue("$manualOverrideReason", (object?)step.ManualOverrideReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualOverrideAuthor", (object?)step.ManualOverrideAuthor ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualOverrideUtc", step.ManualOverrideUtc is null
            ? DBNull.Value
            : step.ManualOverrideUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$measurementJson", (object?)step.MeasurementJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$rawToolOutput", (object?)step.RawToolOutput ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", step.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedUtc", step.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CharacterIdentityBuildStepRecord?> GetStepAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Step id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BuildId, Step, Status, InputArtifactId, OutputArtifactId, ResolvedPromptText,
                   ResolvedModelId, FailureReason, MirrorDerived, ManualOverrideApplied, ManualOverrideReason,
                   ManualOverrideAuthor, ManualOverrideUtc, MeasurementJson, RawToolOutput,
                   CreatedUtc, UpdatedUtc
            FROM CharacterIdentityBuildSteps WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadStep(reader) : null;
    }

    public async Task<IReadOnlyList<CharacterIdentityBuildStepRecord>> ListStepsAsync(string buildId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BuildId, Step, Status, InputArtifactId, OutputArtifactId, ResolvedPromptText,
                   ResolvedModelId, FailureReason, MirrorDerived, ManualOverrideApplied, ManualOverrideReason,
                   ManualOverrideAuthor, ManualOverrideUtc, MeasurementJson, RawToolOutput,
                   CreatedUtc, UpdatedUtc
            FROM CharacterIdentityBuildSteps WHERE BuildId = $buildId ORDER BY Step;
            """;
        command.Parameters.AddWithValue("$buildId", buildId.Trim());
        var results = new List<CharacterIdentityBuildStepRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStep(reader));
        }

        return results;
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
            CREATE TABLE IF NOT EXISTS CharacterIdentityBuilds (
                Id TEXT PRIMARY KEY,
                CharacterProfileId TEXT NOT NULL,
                BatchId TEXT NULL,
                FrontContainerAssetId TEXT NULL,
                CanonicalFrontAssetId TEXT NULL,
                CurrentStep INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityBuilds_Character
                ON CharacterIdentityBuilds (CharacterProfileId);

            CREATE TABLE IF NOT EXISTS CharacterIdentityBuildSteps (
                Id TEXT PRIMARY KEY,
                BuildId TEXT NOT NULL,
                Step INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                InputArtifactId TEXT NULL,
                OutputArtifactId TEXT NULL,
                ResolvedPromptText TEXT NULL,
                ResolvedModelId TEXT NULL,
                FailureReason TEXT NULL,
                MirrorDerived INTEGER NOT NULL DEFAULT 0,
                ManualOverrideApplied INTEGER NOT NULL DEFAULT 0,
                ManualOverrideReason TEXT NULL,
                ManualOverrideAuthor TEXT NULL,
                ManualOverrideUtc TEXT NULL,
                MeasurementJson TEXT NULL,
                RawToolOutput TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityBuildSteps_Build
                ON CharacterIdentityBuildSteps (BuildId, Step);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        var buildColumns = await QueryColumnsAsync(connection, "CharacterIdentityBuilds", cancellationToken);
        if (!buildColumns.Contains("FrontContainerAssetId"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE CharacterIdentityBuilds ADD COLUMN FrontContainerAssetId TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!buildColumns.Contains("CanonicalFrontAssetId"))
        {
            await using var alterCanonical = connection.CreateCommand();
            alterCanonical.CommandText = "ALTER TABLE CharacterIdentityBuilds ADD COLUMN CanonicalFrontAssetId TEXT NULL;";
            await alterCanonical.ExecuteNonQueryAsync(cancellationToken);
        }

        var stepColumns = await QueryColumnsAsync(connection, "CharacterIdentityBuildSteps", cancellationToken);
        await AddStepColumnIfMissingAsync(connection, stepColumns, "ManualOverrideAuthor", "TEXT NULL", cancellationToken);
        await AddStepColumnIfMissingAsync(connection, stepColumns, "ManualOverrideUtc", "TEXT NULL", cancellationToken);
        await AddStepColumnIfMissingAsync(connection, stepColumns, "MeasurementJson", "TEXT NULL", cancellationToken);
        await AddStepColumnIfMissingAsync(connection, stepColumns, "RawToolOutput", "TEXT NULL", cancellationToken);
    }

    private static async Task AddStepColumnIfMissingAsync(
        SqliteConnection connection,
        HashSet<string> existingColumns,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        if (existingColumns.Contains(column))
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE CharacterIdentityBuildSteps ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CharacterIdentityBuild ReadBuild(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        CharacterProfileId = reader.GetString(1),
        BatchId = reader.IsDBNull(2) ? null : reader.GetString(2),
        FrontContainerAssetId = reader.IsDBNull(3) ? null : reader.GetString(3),
    CanonicalFrontAssetId = reader.IsDBNull(8) ? null : reader.GetString(8),
        CurrentStep = (CharacterIdentityBuildStep)reader.GetInt32(4),
        Status = (CharacterIdentityBuildStatus)reader.GetInt32(5),
        CreatedUtc = ParseUtc(reader.GetString(6)),
        UpdatedUtc = ParseUtc(reader.GetString(7))
    };

    private static CharacterIdentityBuildStepRecord ReadStep(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        BuildId = reader.GetString(1),
        Step = (CharacterIdentityBuildStep)reader.GetInt32(2),
        Status = (CharacterIdentityBuildStepStatus)reader.GetInt32(3),
        InputArtifactId = reader.IsDBNull(4) ? null : reader.GetString(4),
        OutputArtifactId = reader.IsDBNull(5) ? null : reader.GetString(5),
        ResolvedPromptText = reader.IsDBNull(6) ? null : reader.GetString(6),
        ResolvedModelId = reader.IsDBNull(7) ? null : reader.GetString(7),
        FailureReason = reader.IsDBNull(8) ? null : reader.GetString(8),
        MirrorDerived = reader.GetInt32(9) != 0,
        ManualOverrideApplied = reader.GetInt32(10) != 0,
        ManualOverrideReason = reader.IsDBNull(11) ? null : reader.GetString(11),
        ManualOverrideAuthor = reader.IsDBNull(12) ? null : reader.GetString(12),
        ManualOverrideUtc = reader.IsDBNull(13) ? null : ParseUtc(reader.GetString(13)),
        MeasurementJson = reader.IsDBNull(14) ? null : reader.GetString(14),
        RawToolOutput = reader.IsDBNull(15) ? null : reader.GetString(15),
        CreatedUtc = ParseUtc(reader.GetString(16)),
        UpdatedUtc = ParseUtc(reader.GetString(17))
    };

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static async Task<HashSet<string>> QueryColumnsAsync(
        SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid UTC value '{value}'.");
}
