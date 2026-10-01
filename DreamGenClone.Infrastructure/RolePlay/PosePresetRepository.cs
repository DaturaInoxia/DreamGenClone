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
            INSERT INTO PosePresets (Id, Name, Category, LibraryId, Keywords, KeypointsJson, SkeletonPngPath, ThumbnailPath, KnownGood, ProvenanceJson, CreatedUtc,
                                     Stance, Direction, CameraAngle, ContentRating, MetadataPrompt, MetadataNeedsReview, MetadataReviewNote, MetadataOperatorEdited)
            VALUES ($id, $name, $category, $libraryId, $keywords, $keypoints, $skeleton, $thumbnail, $knownGood, $provenance, $createdUtc,
                    $stance, $direction, $camera, $rating, $prompt, $needsReview, $reviewNote, $operatorEdited)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Category = excluded.Category,
                LibraryId = excluded.LibraryId,
                Keywords = excluded.Keywords,
                KeypointsJson = excluded.KeypointsJson,
                SkeletonPngPath = excluded.SkeletonPngPath,
                ThumbnailPath = excluded.ThumbnailPath,
                KnownGood = excluded.KnownGood,
                ProvenanceJson = excluded.ProvenanceJson,
                -- The CALLER is the authority for metadata here, so the incoming value wins. This is the method an
                -- operator's edit is saved through, and a fill-only CASE would silently discard it — which is exactly
                -- what it did before 2026-09-30: the panel showed the edit as saved and the next page load reinstated
                -- the pack's prompt.
                --
                -- The "a re-import never overwrites an edited prompt" promise therefore does NOT live in this
                -- statement; a second guard in a second place is how the two silently disagree. It lives in the
                -- importer, on its single decision path: PoseLibraryImporter skips existing rows that have metadata
                -- (HasNoMetadata) and fills through UpdateMetadataAsync only when there is none.
                Stance = excluded.Stance,
                Direction = excluded.Direction,
                CameraAngle = excluded.CameraAngle,
                ContentRating = excluded.ContentRating,
                MetadataPrompt = excluded.MetadataPrompt,
                MetadataNeedsReview = excluded.MetadataNeedsReview,
                MetadataReviewNote = excluded.MetadataReviewNote,
                MetadataOperatorEdited = excluded.MetadataOperatorEdited;
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
        AddMetadataParameters(command, preset);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateMetadataAsync(PosePreset preset, CancellationToken cancellationToken = default)
    {
        Require(preset.Id, "Pose preset id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // Only the metadata columns, and only on a row that is already there. An UPDATE rather than an upsert on
        // purpose: a backfill that could INSERT would be a second creation path for pose rows, and the pose's own
        // keypoints are what make it a pose.
        command.CommandText = """
            UPDATE PosePresets SET
                Stance = $stance,
                Direction = $direction,
                CameraAngle = $camera,
                ContentRating = $rating,
                MetadataPrompt = $prompt,
                MetadataNeedsReview = $needsReview,
                MetadataReviewNote = $reviewNote,
                MetadataOperatorEdited = $operatorEdited
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", preset.Id.Trim());
        AddMetadataParameters(command, preset);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The metadata parameters, shared by the upsert and the metadata-only update so the two cannot write different
    /// values for the same column.
    /// </summary>
    private static void AddMetadataParameters(SqliteCommand command, PosePreset preset)
    {
        command.Parameters.AddWithValue("$stance", preset.Stance.ToString());
        command.Parameters.AddWithValue("$direction", preset.Direction.ToString());
        command.Parameters.AddWithValue("$camera", preset.CameraAngle.ToString());
        command.Parameters.AddWithValue("$rating", preset.ContentRating.ToString());
        command.Parameters.AddWithValue("$prompt", preset.MetadataPrompt);
        command.Parameters.AddWithValue("$needsReview", preset.MetadataNeedsReview ? 1 : 0);
        command.Parameters.AddWithValue("$reviewNote", preset.MetadataReviewNote);
        command.Parameters.AddWithValue("$operatorEdited", preset.MetadataOperatorEdited ? 1 : 0);
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
        SELECT Id, Name, Category, KeypointsJson, SkeletonPngPath, ThumbnailPath, KnownGood, ProvenanceJson, CreatedUtc, LibraryId, Keywords,
               Stance, Direction, CameraAngle, ContentRating, MetadataPrompt, MetadataNeedsReview, MetadataReviewNote, MetadataOperatorEdited
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
        Keywords = reader.GetString(10),
        Stance = ParseMetadataEnum(reader.GetString(11), PoseStance.Unknown),
        Direction = ParseMetadataEnum(reader.GetString(12), PoseFacingDirection.Unknown),
        CameraAngle = ParseMetadataEnum(reader.GetString(13), PoseCameraAngle.Unknown),
        ContentRating = ParseMetadataEnum(reader.GetString(14), PoseContentRating.Unrated),
        MetadataPrompt = reader.GetString(15),
        MetadataNeedsReview = reader.GetInt32(16) != 0,
        MetadataReviewNote = reader.GetString(17),
        MetadataOperatorEdited = reader.GetInt32(18) != 0
    };

    /// <summary>
    /// Reads a stored metadata enum. An EMPTY string is the state of a row written before the column existed, so it
    /// maps to the "not declared" member; anything else must parse, and a value that does not is an error rather than
    /// a silent default.
    ///
    /// Note the deliberate difference from the identity repository's parser, which rejects the zero member: for pose
    /// metadata, "not declared" IS a legitimate stored value, and collapsing it would hide the very state the library
    /// reports.
    /// </summary>
    private static T ParseMetadataEnum<T>(string value, T notDeclared) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return notDeclared;

        return Enum.TryParse<T>(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Pose preset metadata column holds '{value}', which is not a {typeof(T).Name} value.");
    }

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
                    CreatedUtc TEXT NOT NULL,
                    Stance TEXT NOT NULL DEFAULT '',
                    Direction TEXT NOT NULL DEFAULT '',
                    CameraAngle TEXT NOT NULL DEFAULT '',
                    ContentRating TEXT NOT NULL DEFAULT '',
                    MetadataPrompt TEXT NOT NULL DEFAULT '',
                    MetadataNeedsReview INTEGER NOT NULL DEFAULT 0,
                    MetadataReviewNote TEXT NOT NULL DEFAULT '',
                    MetadataOperatorEdited INTEGER NOT NULL DEFAULT 0
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

        // Pose metadata, added 2026-09-30. Same additive shape as the two above: an existing dev DB upgrades in place,
        // and every pre-existing row comes back as "not declared" until the backfill fills it in.
        await AddColumnIfMissingAsync(connection, "PosePresets", "Stance", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "PosePresets", "Direction", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "PosePresets", "CameraAngle", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "PosePresets", "ContentRating", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "PosePresets", "MetadataPrompt", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, "PosePresets", "MetadataNeedsReview", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "PosePresets", "MetadataReviewNote", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        // Whether an OPERATOR saved this pose's metadata by hand, added with the metadata editor. It is the single
        // thing that separates "no one has declared anything" from "an operator declared nothing", and it is what
        // keeps the backfill from overwriting an edit that set only the camera angle.
        await AddColumnIfMissingAsync(connection, "PosePresets", "MetadataOperatorEdited", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

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
