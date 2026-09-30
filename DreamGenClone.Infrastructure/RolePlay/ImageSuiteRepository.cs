using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for image suites and cells (B-135 B135-010).
///
/// <para>
/// Schema creation is idempotent and reached from the same private ensure <c>OpenAsync</c> calls, so any read has a
/// usable store. Nothing is seeded: a suite is operator-authored configuration, and an empty table is a legitimate
/// state (unlike the compiler profiles, where a missing row must be refused rather than defaulted).
/// </para>
/// </summary>
public sealed class ImageSuiteRepository : IImageSuiteRepository
{
    private const string SuiteColumns = """
        Id, Name, Version, Kind, Status, Description, Provenance, UpdatedUtc
        """;

    private const string CellColumns = """
        Id, SuiteId, Ordinal, Name, CheckpointProfileId, UserDirection, ExpectedPrompt,
        BindingsJson, SeedJson, SettingsJson, CompilerLlmJson, GatesJson, SimilarityTolerance, UpdatedUtc
        """;

    private readonly string _connectionString;

    public ImageSuiteRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ImageSuite>> ListSuitesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SuiteColumns} FROM ImageSuites ORDER BY Name, Version;
            """;
        var results = new List<ImageSuite>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSuite(reader));
        }

        return results;
    }

    public async Task<ImageSuite?> GetSuiteAsync(string suiteId, CancellationToken cancellationToken = default)
    {
        Require(suiteId, "Suite id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SuiteColumns} FROM ImageSuites WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", suiteId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSuite(reader) : null;
    }

    public async Task UpsertSuiteAsync(ImageSuite suite, CancellationToken cancellationToken = default)
    {
        ImageSuiteValidation.Validate(suite);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Matched on (Name, Version): a version bump INSERTS a new row, so a run's recorded version still resolves to
        // the cells that produced it. Overwriting in place would re-label old evidence with new cells.
        command.CommandText = """
            INSERT INTO ImageSuites (Id, Name, Version, Kind, Status, Description, Provenance, UpdatedUtc)
            VALUES ($id, $name, $version, $kind, $status, $description, $provenance, $updatedUtc)
            ON CONFLICT(Name, Version) DO UPDATE SET
                Kind = excluded.Kind,
                Status = excluded.Status,
                Description = excluded.Description,
                Provenance = excluded.Provenance,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", suite.Id.Trim());
        command.Parameters.AddWithValue("$name", suite.Name.Trim());
        command.Parameters.AddWithValue("$version", suite.Version);
        command.Parameters.AddWithValue("$kind", (int)suite.Kind);
        command.Parameters.AddWithValue("$status", (int)suite.Status);
        command.Parameters.AddWithValue("$description", suite.Description);
        command.Parameters.AddWithValue("$provenance", suite.Provenance);
        command.Parameters.AddWithValue("$updatedUtc", suite.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ImageSuiteCell>> ListCellsAsync(string suiteId, CancellationToken cancellationToken = default)
    {
        Require(suiteId, "Suite id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {CellColumns} FROM ImageSuiteCells WHERE SuiteId = $suiteId ORDER BY Ordinal;
            """;
        command.Parameters.AddWithValue("$suiteId", suiteId.Trim());
        var results = new List<ImageSuiteCell>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadCell(reader));
        }

        return results;
    }

    public async Task UpsertCellAsync(ImageSuiteCell cell, CancellationToken cancellationToken = default)
    {
        ImageSuiteValidation.Validate(cell);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImageSuiteCells (
                Id, SuiteId, Ordinal, Name, CheckpointProfileId, UserDirection, ExpectedPrompt,
                BindingsJson, SeedJson, SettingsJson, CompilerLlmJson, GatesJson, SimilarityTolerance, UpdatedUtc)
            VALUES (
                $id, $suiteId, $ordinal, $name, $checkpointProfileId, $userDirection, $expectedPrompt,
                $bindings, $seed, $settings, $compilerLlm, $gates, $tolerance, $updatedUtc)
            ON CONFLICT(SuiteId, Ordinal) DO UPDATE SET
                Name = excluded.Name,
                CheckpointProfileId = excluded.CheckpointProfileId,
                UserDirection = excluded.UserDirection,
                ExpectedPrompt = excluded.ExpectedPrompt,
                BindingsJson = excluded.BindingsJson,
                SeedJson = excluded.SeedJson,
                SettingsJson = excluded.SettingsJson,
                CompilerLlmJson = excluded.CompilerLlmJson,
                GatesJson = excluded.GatesJson,
                SimilarityTolerance = excluded.SimilarityTolerance,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", cell.Id.Trim());
        command.Parameters.AddWithValue("$suiteId", cell.SuiteId.Trim());
        command.Parameters.AddWithValue("$ordinal", cell.Ordinal);
        command.Parameters.AddWithValue("$name", cell.Name.Trim());
        command.Parameters.AddWithValue("$checkpointProfileId", (object?)cell.CheckpointProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$userDirection", cell.UserDirection);
        command.Parameters.AddWithValue("$expectedPrompt", cell.ExpectedPrompt);
        command.Parameters.AddWithValue("$bindings", cell.BindingsJson);
        command.Parameters.AddWithValue("$seed", cell.SeedJson);
        command.Parameters.AddWithValue("$settings", cell.SettingsJson);
        command.Parameters.AddWithValue("$compilerLlm", cell.CompilerLlmJson);
        command.Parameters.AddWithValue("$gates", cell.GatesJson);
        command.Parameters.AddWithValue("$tolerance", (object?)cell.SimilarityTolerance ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", cell.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteCellAsync(string cellId, CancellationToken cancellationToken = default)
    {
        Require(cellId, "Cell id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ImageSuiteCells WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", cellId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Require(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{what} is required.");
        }
    }

    private static ImageSuite ReadSuite(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        Version = reader.GetInt32(reader.GetOrdinal("Version")),
        Kind = (ImageSuiteKind)reader.GetInt32(reader.GetOrdinal("Kind")),
        Status = (ImageSuiteStatus)reader.GetInt32(reader.GetOrdinal("Status")),
        Description = reader.GetString(reader.GetOrdinal("Description")),
        Provenance = reader.GetString(reader.GetOrdinal("Provenance")),
        UpdatedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedUtc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

    private static ImageSuiteCell ReadCell(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        SuiteId = reader.GetString(reader.GetOrdinal("SuiteId")),
        Ordinal = reader.GetInt32(reader.GetOrdinal("Ordinal")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        CheckpointProfileId = reader.IsDBNull(reader.GetOrdinal("CheckpointProfileId")) ? null : reader.GetString(reader.GetOrdinal("CheckpointProfileId")),
        UserDirection = reader.GetString(reader.GetOrdinal("UserDirection")),
        ExpectedPrompt = reader.GetString(reader.GetOrdinal("ExpectedPrompt")),
        BindingsJson = reader.GetString(reader.GetOrdinal("BindingsJson")),
        SeedJson = reader.GetString(reader.GetOrdinal("SeedJson")),
        SettingsJson = reader.GetString(reader.GetOrdinal("SettingsJson")),
        CompilerLlmJson = reader.GetString(reader.GetOrdinal("CompilerLlmJson")),
        GatesJson = reader.GetString(reader.GetOrdinal("GatesJson")),
        SimilarityTolerance = reader.IsDBNull(reader.GetOrdinal("SimilarityTolerance")) ? null : reader.GetDouble(reader.GetOrdinal("SimilarityTolerance")),
        UpdatedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedUtc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

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
            CREATE TABLE IF NOT EXISTS ImageSuites (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Version INTEGER NOT NULL,
                Kind INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Provenance TEXT NOT NULL DEFAULT '',
                UpdatedUtc TEXT NOT NULL
            );
            -- (Name, Version) is the natural key: a new version must INSERT, never overwrite the run-recorded one.
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ImageSuites_NameVersion ON ImageSuites (Name COLLATE NOCASE, Version);

            CREATE TABLE IF NOT EXISTS ImageSuiteCells (
                Id TEXT PRIMARY KEY,
                SuiteId TEXT NOT NULL,
                Ordinal INTEGER NOT NULL,
                Name TEXT NOT NULL,
                CheckpointProfileId TEXT NULL,
                UserDirection TEXT NOT NULL DEFAULT '',
                ExpectedPrompt TEXT NOT NULL DEFAULT '',
                BindingsJson TEXT NOT NULL DEFAULT '[]',
                SeedJson TEXT NOT NULL DEFAULT '{}',
                SettingsJson TEXT NOT NULL DEFAULT '{}',
                CompilerLlmJson TEXT NOT NULL DEFAULT '{}',
                GatesJson TEXT NOT NULL DEFAULT '[]',
                SimilarityTolerance REAL NULL,
                UpdatedUtc TEXT NOT NULL
            );
            -- Ordinal is the cell's identity within a suite version.
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ImageSuiteCells_SuiteOrdinal ON ImageSuiteCells (SuiteId, Ordinal);
            CREATE INDEX IF NOT EXISTS IX_ImageSuiteCells_Suite ON ImageSuiteCells (SuiteId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
