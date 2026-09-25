using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for the pose-preset library. Self-contained schema creation mirrors the other
/// scene-image repositories. Additive and idempotent.
/// </summary>
public sealed class PosePresetRepository : IPosePresetRepository
{
    private readonly string _connectionString;

    public PosePresetRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<PosePreset>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await QueryAsync(connection, "1 = 1", null, cancellationToken);
    }

    public async Task<PosePreset?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Pose preset id");
        await using var connection = await OpenAsync(cancellationToken);
        var results = await QueryAsync(connection, "Id = $id", new Dictionary<string, object?> { ["$id"] = id.Trim() }, cancellationToken);
        return results.FirstOrDefault();
    }

    public async Task<IReadOnlyList<PosePreset>> SearchAsync(
        string? keyword,
        string? category = null,
        string? libraryId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);

        var clauses = new List<string>();
        var parameters = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // One keyword matches any of the three human-readable columns, so the operator does not have to
            // know which one a pose was filed under.
            clauses.Add("(Name LIKE $keyword ESCAPE '\\' OR Keywords LIKE $keyword ESCAPE '\\' OR Category LIKE $keyword ESCAPE '\\')");
            parameters["$keyword"] = $"%{EscapeLike(keyword.Trim())}%";
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            clauses.Add("Category = $category");
            parameters["$category"] = category.Trim();
        }
        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            clauses.Add("LibraryId = $libraryId");
            parameters["$libraryId"] = libraryId.Trim();
        }

        var where = clauses.Count == 0 ? "1 = 1" : string.Join(" AND ", clauses);
        return await QueryAsync(connection, where, parameters, cancellationToken);
    }

    public async Task<IReadOnlyList<PoseLibrary>> ListLibrariesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectLibrarySql} ORDER BY IsSystem DESC, Name;";

        var results = new List<PoseLibrary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadLibrary(reader));
        }

        return results;
    }

    public async Task<PoseLibrary?> GetLibraryAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Pose library id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectLibrarySql} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLibrary(reader) : null;
    }

    public async Task UpsertLibraryAsync(PoseLibrary library, CancellationToken cancellationToken = default)
    {
        Require(library.Id, "Pose library id");
        Require(library.Name, "Pose library name");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PoseLibraries (Id, Name, Description, IsSystem, CreatedUtc)
            VALUES ($id, $name, $description, $isSystem, $createdUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                IsSystem = excluded.IsSystem;
            """;
        command.Parameters.AddWithValue("$id", library.Id.Trim());
        command.Parameters.AddWithValue("$name", library.Name.Trim());
        command.Parameters.AddWithValue("$description", library.Description);
        command.Parameters.AddWithValue("$isSystem", library.IsSystem ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", library.CreatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(PosePreset preset, CancellationToken cancellationToken = default)
    {
        Validate(preset);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PosePresets (Id, Name, Category, LibraryId, Keywords, KeypointsJson, SkeletonPngPath, ThumbnailPath, KnownGood, ProvenanceJson, CreatedUtc)
            VALUES ($id, $name, $category, $libraryId, $keywords, $keypoints, $skeleton, $thumbnail, $knownGood, $provenance, $createdUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Category = excluded.Category,
                LibraryId = excluded.LibraryId,
                Keywords = excluded.Keywords,
                KeypointsJson = excluded.KeypointsJson,
                SkeletonPngPath = excluded.SkeletonPngPath,
                ThumbnailPath = excluded.ThumbnailPath,
                KnownGood = excluded.KnownGood,
                ProvenanceJson = excluded.ProvenanceJson;
            """;
        command.Parameters.AddWithValue("$id", preset.Id.Trim());
        command.Parameters.AddWithValue("$name", preset.Name.Trim());
        command.Parameters.AddWithValue("$category", preset.Category.Trim());
        command.Parameters.AddWithValue("$libraryId", preset.LibraryId.Trim());
        command.Parameters.AddWithValue("$keywords", preset.Keywords);
        command.Parameters.AddWithValue("$keypoints", preset.KeypointsJson);
        command.Parameters.AddWithValue("$skeleton", (object?)preset.SkeletonPngPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$thumbnail", (object?)preset.ThumbnailPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$knownGood", preset.KnownGood ? 1 : 0);
        command.Parameters.AddWithValue("$provenance", (object?)preset.ProvenanceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", preset.CreatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Pose preset id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PosePresets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectSql = """
        SELECT Id, Name, Category, KeypointsJson, SkeletonPngPath, ThumbnailPath, KnownGood, ProvenanceJson, CreatedUtc, LibraryId, Keywords
        FROM PosePresets
        """;

    private const string SelectLibrarySql = """
        SELECT Id, Name, Description, IsSystem, CreatedUtc
        FROM PoseLibraries
        """;

    private static async Task<IReadOnlyList<PosePreset>> QueryAsync(
        SqliteConnection connection,
        string where,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE {where} ORDER BY LibraryId, Category, Name;";
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }

        var results = new List<PosePreset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPreset(reader));
        }

        return results;
    }

    private static PosePreset ReadPreset(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Category = reader.GetString(2),
        KeypointsJson = reader.GetString(3),
        SkeletonPngPath = reader.IsDBNull(4) ? null : reader.GetString(4),
        ThumbnailPath = reader.IsDBNull(5) ? null : reader.GetString(5),
        KnownGood = reader.GetInt32(6) != 0,
        ProvenanceJson = reader.IsDBNull(7) ? null : reader.GetString(7),
        CreatedUtc = ParseUtc(reader.GetString(8)),
        LibraryId = reader.GetString(9),
        Keywords = reader.GetString(10)
    };

    private static PoseLibrary ReadLibrary(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        IsSystem = reader.GetInt32(3) != 0,
        CreatedUtc = ParseUtc(reader.GetString(4))
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
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS PoseLibraries (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    IsSystem INTEGER NOT NULL DEFAULT 0,
                    CreatedUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS PosePresets (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Category TEXT NOT NULL DEFAULT '',
                    LibraryId TEXT NOT NULL DEFAULT '',
                    Keywords TEXT NOT NULL DEFAULT '',
                    KeypointsJson TEXT NOT NULL DEFAULT '[]',
                    SkeletonPngPath TEXT NULL,
                    ThumbnailPath TEXT NULL,
                    KnownGood INTEGER NOT NULL DEFAULT 0,
                    ProvenanceJson TEXT NULL,
                    CreatedUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_PosePresets_Category ON PosePresets (Category, Name);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Additive columns for databases created before the library model existed. Presence-checked rather than
        // assumed, so an existing dev DB upgrades in place instead of failing on the first query. The library
        // index is created AFTER the columns, because indexing a column that does not exist yet is an error.
        await AddColumnIfMissingAsync(connection, "PosePresets", "LibraryId", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "PosePresets", "Keywords", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await using (var index = connection.CreateCommand())
        {
            index.CommandText =
                "CREATE INDEX IF NOT EXISTS IX_PosePresets_Library ON PosePresets (LibraryId, Category, Name);";
            await index.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        bool exists;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
            exists = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists) return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Escapes the LIKE wildcards so a keyword containing a percent sign matches that character instead of
    /// matching every preset.
    /// </summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static void Validate(PosePreset preset)
    {
        Require(preset.Id, "Pose preset id");
        Require(preset.Name, "Pose preset name");
        if (string.IsNullOrWhiteSpace(preset.KeypointsJson))
            throw new InvalidOperationException("Pose preset keypoints JSON is required.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid UTC value '{value}' for PosePresets.");
}
