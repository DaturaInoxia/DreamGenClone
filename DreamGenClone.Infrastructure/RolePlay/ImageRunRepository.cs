using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for image runs and their cells (B-135 B135-014).
///
/// <para>
/// Schema creation is idempotent and reached from the same private ensure <c>OpenAsync</c> calls. Nothing is seeded: a
/// run is evidence produced by executing a suite, and an empty table is the correct state before anything has run.
/// </para>
/// </summary>
public sealed class ImageRunRepository : IImageRunRepository
{
    private const string RunColumns = """
        Id, SuiteId, SuiteVersion, Kind, Status, ImageLayerRequested, CompilerLlmJson,
        Notes, Provenance, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
        """;

    private const string CellColumns = """
        Id, RunId, SuiteId, CellId, Ordinal, Name, CheckpointProfileId, UserDirection, ExpectedPrompt,
        BindingsJson, SeedJson, SettingsJson, GatesJson, CompilerLlmJson, SimilarityTolerance,
        ResolvedCheckpoint, ResolvedProvider, CompiledPrompt, Similarity, PromptLayerJson, RequestLayerJson,
        RequestSnapshotJson, RenderFromPath, Seed, Status, ImageId, ImagePath, GateResultsJson,
        TimingMs, CostUsd, VisualVerdict, VisualNote, FailureMessage, UpdatedUtc
        """;

    private readonly string _connectionString;

    public ImageRunRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
    }

    // ---- runs ---------------------------------------------------------------------------------------------

    public async Task InsertRunAsync(ImageRun run, CancellationToken cancellationToken = default)
    {
        ImageRunValidation.Validate(run);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImageRuns (
                Id, SuiteId, SuiteVersion, Kind, Status, ImageLayerRequested, CompilerLlmJson,
                Notes, Provenance, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
            VALUES (
                $id, $suiteId, $suiteVersion, $kind, $status, $imageLayer, $compilerLlm,
                $notes, $provenance, $createdUtc, $startedUtc, $completedUtc, $updatedUtc);
            """;
        BindRun(command, run);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateRunAsync(ImageRun run, CancellationToken cancellationToken = default)
    {
        ImageRunValidation.Validate(run);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // SuiteId / SuiteVersion / Kind are absent from the SET list on purpose: they identify which cells this run
        // executed, and changing them would re-label old evidence as belonging to a different suite.
        command.CommandText = """
            UPDATE ImageRuns SET
                Status = $status,
                ImageLayerRequested = $imageLayer,
                CompilerLlmJson = $compilerLlm,
                Notes = $notes,
                Provenance = $provenance,
                StartedUtc = $startedUtc,
                CompletedUtc = $completedUtc,
                UpdatedUtc = $updatedUtc
            WHERE Id = $id;
            """;
        BindRun(command, run);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException($"Run '{run.Id}' does not exist, so it cannot be updated.");
        }
    }

    public async Task<ImageRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        Require(runId, "Run id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {RunColumns} FROM ImageRuns WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", runId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    public async Task<IReadOnlyList<ImageRun>> ListRunsAsync(string? suiteId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var scoped = !string.IsNullOrWhiteSpace(suiteId);
        command.CommandText = scoped
            ? $"SELECT {RunColumns} FROM ImageRuns WHERE SuiteId = $suiteId ORDER BY CreatedUtc DESC;"
            : $"SELECT {RunColumns} FROM ImageRuns ORDER BY CreatedUtc DESC;";
        if (scoped)
        {
            command.Parameters.AddWithValue("$suiteId", suiteId!.Trim());
        }

        var results = new List<ImageRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRun(reader));
        }

        return results;
    }

    public async Task DeleteRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        Require(runId, "Run id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var cells = connection.CreateCommand())
        {
            cells.Transaction = (SqliteTransaction)transaction;
            cells.CommandText = "DELETE FROM ImageRunCells WHERE RunId = $id;";
            cells.Parameters.AddWithValue("$id", runId.Trim());
            await cells.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var run = connection.CreateCommand())
        {
            run.Transaction = (SqliteTransaction)transaction;
            run.CommandText = "DELETE FROM ImageRuns WHERE Id = $id;";
            run.Parameters.AddWithValue("$id", runId.Trim());
            await run.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // ---- cells --------------------------------------------------------------------------------------------

    public async Task UpsertCellAsync(ImageRunCell cell, CancellationToken cancellationToken = default)
    {
        ImageRunValidation.ValidateCell(cell);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImageRunCells (
                Id, RunId, SuiteId, CellId, Ordinal, Name, CheckpointProfileId, UserDirection, ExpectedPrompt,
                BindingsJson, SeedJson, SettingsJson, GatesJson, CompilerLlmJson, SimilarityTolerance,
                ResolvedCheckpoint, ResolvedProvider, CompiledPrompt, Similarity, PromptLayerJson, RequestLayerJson,
                RequestSnapshotJson, RenderFromPath, Seed, Status, ImageId, ImagePath, GateResultsJson,
                TimingMs, CostUsd, VisualVerdict, VisualNote, FailureMessage, UpdatedUtc)
            VALUES (
                $id, $runId, $suiteId, $cellId, $ordinal, $name, $checkpointProfileId, $userDirection, $expectedPrompt,
                $bindings, $seedJson, $settings, $gates, $compilerLlm, $tolerance,
                $resolvedCheckpoint, $resolvedProvider, $compiledPrompt, $similarity, $promptLayer, $requestLayer,
                $requestSnapshot, $renderFromPath, $seed, $status, $imageId, $imagePath, $gateResults,
                $timingMs, $costUsd, $visualVerdict, $visualNote, $failureMessage, $updatedUtc)
            ON CONFLICT(RunId, Ordinal) DO UPDATE SET
                Name = excluded.Name,
                CheckpointProfileId = excluded.CheckpointProfileId,
                UserDirection = excluded.UserDirection,
                ExpectedPrompt = excluded.ExpectedPrompt,
                BindingsJson = excluded.BindingsJson,
                SeedJson = excluded.SeedJson,
                SettingsJson = excluded.SettingsJson,
                GatesJson = excluded.GatesJson,
                CompilerLlmJson = excluded.CompilerLlmJson,
                SimilarityTolerance = excluded.SimilarityTolerance,
                ResolvedCheckpoint = excluded.ResolvedCheckpoint,
                ResolvedProvider = excluded.ResolvedProvider,
                CompiledPrompt = excluded.CompiledPrompt,
                Similarity = excluded.Similarity,
                PromptLayerJson = excluded.PromptLayerJson,
                RequestLayerJson = excluded.RequestLayerJson,
                RequestSnapshotJson = excluded.RequestSnapshotJson,
                RenderFromPath = excluded.RenderFromPath,
                Seed = excluded.Seed,
                Status = excluded.Status,
                ImageId = excluded.ImageId,
                ImagePath = excluded.ImagePath,
                GateResultsJson = excluded.GateResultsJson,
                TimingMs = excluded.TimingMs,
                CostUsd = excluded.CostUsd,
                VisualVerdict = excluded.VisualVerdict,
                VisualNote = excluded.VisualNote,
                FailureMessage = excluded.FailureMessage,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", cell.Id.Trim());
        command.Parameters.AddWithValue("$runId", cell.RunId.Trim());
        command.Parameters.AddWithValue("$suiteId", cell.SuiteId.Trim());
        command.Parameters.AddWithValue("$cellId", cell.CellId.Trim());
        command.Parameters.AddWithValue("$ordinal", cell.Ordinal);
        command.Parameters.AddWithValue("$name", cell.Name.Trim());
        command.Parameters.AddWithValue("$checkpointProfileId", (object?)cell.CheckpointProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$userDirection", cell.UserDirection);
        command.Parameters.AddWithValue("$expectedPrompt", cell.ExpectedPrompt);
        command.Parameters.AddWithValue("$bindings", cell.BindingsJson);
        command.Parameters.AddWithValue("$seedJson", cell.SeedJson);
        command.Parameters.AddWithValue("$settings", cell.SettingsJson);
        command.Parameters.AddWithValue("$gates", cell.GatesJson);
        command.Parameters.AddWithValue("$compilerLlm", cell.CompilerLlmJson);
        command.Parameters.AddWithValue("$tolerance", (object?)cell.SimilarityTolerance ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedCheckpoint", cell.ResolvedCheckpoint);
        command.Parameters.AddWithValue("$resolvedProvider", cell.ResolvedProvider);
        command.Parameters.AddWithValue("$compiledPrompt", cell.CompiledPrompt);
        command.Parameters.AddWithValue("$similarity", (object?)cell.Similarity ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptLayer", cell.PromptLayerJson);
        command.Parameters.AddWithValue("$requestLayer", cell.RequestLayerJson);
        command.Parameters.AddWithValue("$requestSnapshot", cell.RequestSnapshotJson);
        command.Parameters.AddWithValue("$renderFromPath", (int)cell.RenderFromPath);
        command.Parameters.AddWithValue("$seed", (object?)cell.Seed ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)cell.Status);
        command.Parameters.AddWithValue("$imageId", (object?)cell.ImageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$imagePath", (object?)cell.ImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$gateResults", cell.GateResultsJson);
        command.Parameters.AddWithValue("$timingMs", (object?)cell.TimingMs ?? DBNull.Value);
        // Stored as text: money is decimal in the domain and REAL in SQLite would silently round it.
        command.Parameters.AddWithValue("$costUsd", cell.CostUsd is { } cost ? cost.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$visualVerdict", (int)cell.VisualVerdict);
        command.Parameters.AddWithValue("$visualNote", cell.VisualNote);
        command.Parameters.AddWithValue("$failureMessage", cell.FailureMessage);
        command.Parameters.AddWithValue("$updatedUtc", cell.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ImageRunCell?> GetCellAsync(string cellId, CancellationToken cancellationToken = default)
    {
        Require(cellId, "Run cell id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {CellColumns} FROM ImageRunCells WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", cellId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCell(reader) : null;
    }

    public async Task<IReadOnlyList<ImageRunCell>> ListCellsAsync(string runId, CancellationToken cancellationToken = default)
    {
        Require(runId, "Run id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {CellColumns} FROM ImageRunCells WHERE RunId = $runId ORDER BY Ordinal;";
        command.Parameters.AddWithValue("$runId", runId.Trim());
        var results = new List<ImageRunCell>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadCell(reader));
        }

        return results;
    }

    // ---- plumbing -----------------------------------------------------------------------------------------

    private static void BindRun(SqliteCommand command, ImageRun run)
    {
        command.Parameters.AddWithValue("$id", run.Id.Trim());
        command.Parameters.AddWithValue("$suiteId", run.SuiteId.Trim());
        command.Parameters.AddWithValue("$suiteVersion", run.SuiteVersion);
        command.Parameters.AddWithValue("$kind", (int)run.Kind);
        command.Parameters.AddWithValue("$status", (int)run.Status);
        command.Parameters.AddWithValue("$imageLayer", run.ImageLayerRequested ? 1 : 0);
        command.Parameters.AddWithValue("$compilerLlm", run.CompilerLlmJson);
        command.Parameters.AddWithValue("$notes", run.Notes);
        command.Parameters.AddWithValue("$provenance", run.Provenance);
        command.Parameters.AddWithValue("$createdUtc", run.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$startedUtc", run.StartedUtc is { } started ? started.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", run.CompletedUtc is { } completed ? completed.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", run.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static void Require(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{what} is required.");
        }
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static ImageRun ReadRun(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        SuiteId = reader.GetString(reader.GetOrdinal("SuiteId")),
        SuiteVersion = reader.GetInt32(reader.GetOrdinal("SuiteVersion")),
        Kind = (ImageSuiteKind)reader.GetInt32(reader.GetOrdinal("Kind")),
        Status = (ImageRunStatus)reader.GetInt32(reader.GetOrdinal("Status")),
        ImageLayerRequested = reader.GetInt32(reader.GetOrdinal("ImageLayerRequested")) != 0,
        CompilerLlmJson = reader.GetString(reader.GetOrdinal("CompilerLlmJson")),
        Notes = reader.GetString(reader.GetOrdinal("Notes")),
        Provenance = reader.GetString(reader.GetOrdinal("Provenance")),
        CreatedUtc = ParseUtc(reader.GetString(reader.GetOrdinal("CreatedUtc"))),
        StartedUtc = reader.IsDBNull(reader.GetOrdinal("StartedUtc")) ? null : ParseUtc(reader.GetString(reader.GetOrdinal("StartedUtc"))),
        CompletedUtc = reader.IsDBNull(reader.GetOrdinal("CompletedUtc")) ? null : ParseUtc(reader.GetString(reader.GetOrdinal("CompletedUtc"))),
        UpdatedUtc = ParseUtc(reader.GetString(reader.GetOrdinal("UpdatedUtc")))
    };

    private static ImageRunCell ReadCell(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        RunId = reader.GetString(reader.GetOrdinal("RunId")),
        SuiteId = reader.GetString(reader.GetOrdinal("SuiteId")),
        CellId = reader.GetString(reader.GetOrdinal("CellId")),
        Ordinal = reader.GetInt32(reader.GetOrdinal("Ordinal")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        CheckpointProfileId = reader.IsDBNull(reader.GetOrdinal("CheckpointProfileId")) ? null : reader.GetString(reader.GetOrdinal("CheckpointProfileId")),
        UserDirection = reader.GetString(reader.GetOrdinal("UserDirection")),
        ExpectedPrompt = reader.GetString(reader.GetOrdinal("ExpectedPrompt")),
        BindingsJson = reader.GetString(reader.GetOrdinal("BindingsJson")),
        SeedJson = reader.GetString(reader.GetOrdinal("SeedJson")),
        SettingsJson = reader.GetString(reader.GetOrdinal("SettingsJson")),
        GatesJson = reader.GetString(reader.GetOrdinal("GatesJson")),
        CompilerLlmJson = reader.GetString(reader.GetOrdinal("CompilerLlmJson")),
        SimilarityTolerance = reader.IsDBNull(reader.GetOrdinal("SimilarityTolerance")) ? null : reader.GetDouble(reader.GetOrdinal("SimilarityTolerance")),
        ResolvedCheckpoint = reader.GetString(reader.GetOrdinal("ResolvedCheckpoint")),
        ResolvedProvider = reader.GetString(reader.GetOrdinal("ResolvedProvider")),
        CompiledPrompt = reader.GetString(reader.GetOrdinal("CompiledPrompt")),
        Similarity = reader.IsDBNull(reader.GetOrdinal("Similarity")) ? null : reader.GetDouble(reader.GetOrdinal("Similarity")),
        PromptLayerJson = reader.GetString(reader.GetOrdinal("PromptLayerJson")),
        RequestLayerJson = reader.GetString(reader.GetOrdinal("RequestLayerJson")),
        RequestSnapshotJson = reader.GetString(reader.GetOrdinal("RequestSnapshotJson")),
        RenderFromPath = (ImageRenderSource)reader.GetInt32(reader.GetOrdinal("RenderFromPath")),
        Seed = reader.IsDBNull(reader.GetOrdinal("Seed")) ? null : reader.GetInt64(reader.GetOrdinal("Seed")),
        Status = (ImageRunCellStatus)reader.GetInt32(reader.GetOrdinal("Status")),
        ImageId = reader.IsDBNull(reader.GetOrdinal("ImageId")) ? null : reader.GetString(reader.GetOrdinal("ImageId")),
        ImagePath = reader.IsDBNull(reader.GetOrdinal("ImagePath")) ? null : reader.GetString(reader.GetOrdinal("ImagePath")),
        GateResultsJson = reader.GetString(reader.GetOrdinal("GateResultsJson")),
        TimingMs = reader.IsDBNull(reader.GetOrdinal("TimingMs")) ? null : reader.GetInt64(reader.GetOrdinal("TimingMs")),
        CostUsd = reader.IsDBNull(reader.GetOrdinal("CostUsd")) ? null : decimal.Parse(reader.GetString(reader.GetOrdinal("CostUsd")), CultureInfo.InvariantCulture),
        VisualVerdict = (ImageVisualVerdict)reader.GetInt32(reader.GetOrdinal("VisualVerdict")),
        VisualNote = reader.GetString(reader.GetOrdinal("VisualNote")),
        FailureMessage = reader.GetString(reader.GetOrdinal("FailureMessage")),
        UpdatedUtc = ParseUtc(reader.GetString(reader.GetOrdinal("UpdatedUtc")))
    };

    /// <summary>
    /// Opens the connection, applying the schema on the way. Both tables are created idempotently and the uniqueness
    /// of a cell's position within a run is enforced by the database rather than by a check the writer could forget.
    /// </summary>
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ImageRuns (
                Id TEXT NOT NULL PRIMARY KEY,
                SuiteId TEXT NOT NULL,
                SuiteVersion INTEGER NOT NULL,
                Kind INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                ImageLayerRequested INTEGER NOT NULL DEFAULT 0,
                CompilerLlmJson TEXT NOT NULL DEFAULT '{}',
                Notes TEXT NOT NULL DEFAULT '',
                Provenance TEXT NOT NULL DEFAULT '',
                CreatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL,
                CompletedUtc TEXT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ImageRuns_SuiteId ON ImageRuns (SuiteId, CreatedUtc DESC);

            CREATE TABLE IF NOT EXISTS ImageRunCells (
                Id TEXT NOT NULL PRIMARY KEY,
                RunId TEXT NOT NULL,
                SuiteId TEXT NOT NULL,
                CellId TEXT NOT NULL,
                Ordinal INTEGER NOT NULL,
                Name TEXT NOT NULL DEFAULT '',
                CheckpointProfileId TEXT NULL,
                UserDirection TEXT NOT NULL DEFAULT '',
                ExpectedPrompt TEXT NOT NULL DEFAULT '',
                BindingsJson TEXT NOT NULL DEFAULT '[]',
                SeedJson TEXT NOT NULL DEFAULT '{}',
                SettingsJson TEXT NOT NULL DEFAULT '{}',
                GatesJson TEXT NOT NULL DEFAULT '[]',
                CompilerLlmJson TEXT NOT NULL DEFAULT '{}',
                SimilarityTolerance REAL NULL,
                ResolvedCheckpoint TEXT NOT NULL DEFAULT '',
                ResolvedProvider TEXT NOT NULL DEFAULT '',
                CompiledPrompt TEXT NOT NULL DEFAULT '',
                Similarity REAL NULL,
                PromptLayerJson TEXT NOT NULL DEFAULT '[]',
                RequestLayerJson TEXT NOT NULL DEFAULT '[]',
                RequestSnapshotJson TEXT NOT NULL DEFAULT '{}',
                RenderFromPath INTEGER NOT NULL,
                Seed INTEGER NULL,
                Status INTEGER NOT NULL,
                ImageId TEXT NULL,
                ImagePath TEXT NULL,
                GateResultsJson TEXT NOT NULL DEFAULT '[]',
                TimingMs INTEGER NULL,
                CostUsd TEXT NULL,
                VisualVerdict INTEGER NOT NULL,
                VisualNote TEXT NOT NULL DEFAULT '',
                FailureMessage TEXT NOT NULL DEFAULT '',
                UpdatedUtc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_ImageRunCells_RunOrdinal ON ImageRunCells (RunId, Ordinal);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }
}
