using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for the app-wide scene asset library. Self-contained schema creation mirrors
/// the other RP repositories. Assets are free-floating rows (not scoped to a character) so the same
/// library can back identity packs, locations, and wardrobe packs.
/// </summary>
public sealed class SceneAssetRepository : ISceneAssetRepository
{
    private readonly string _connectionString;

    public SceneAssetRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<SceneAsset?> GetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        Require(assetId, "Asset id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson, FileRelativePath,
                   MediaType, Width, Height, ByteLength, Sha256, FaceView, IdentityPackId, CharacterProfileId,
                   ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM SceneAssets
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", assetId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAsset(reader);
    }

    public async Task<IReadOnlyList<SceneAsset>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson, FileRelativePath,
                   MediaType, Width, Height, ByteLength, Sha256, FaceView, IdentityPackId, CharacterProfileId,
                   ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM SceneAssets
            ORDER BY CreatedUtc DESC;
            """;

        var results = new List<SceneAsset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAsset(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<SceneAsset>> ListByPackAsync(
        string identityPackId, CancellationToken cancellationToken = default)
    {
        Require(identityPackId, "Identity pack id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson, FileRelativePath,
                   MediaType, Width, Height, ByteLength, Sha256, FaceView, IdentityPackId, CharacterProfileId,
                   ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM SceneAssets
            WHERE IdentityPackId = $packId
            ORDER BY CreatedUtc ASC;
            """;
        command.Parameters.AddWithValue("$packId", identityPackId.Trim());

        var results = new List<SceneAsset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAsset(reader));
        }

        return results;
    }

    public async Task UpsertAsync(SceneAsset asset, CancellationToken cancellationToken = default)
    {
        Require(asset.Id, "Asset id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO SceneAssets (
                Id, Name, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson, FileRelativePath,
                MediaType, Width, Height, ByteLength, Sha256, FaceView, IdentityPackId, CharacterProfileId,
                ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $kind, $status, $prompt, $sourceAssetId, $modelSnapshotJson, $fileRelativePath,
                $mediaType, $width, $height, $byteLength, $sha256, $faceView, $identityPackId, $characterProfileId,
                $errorMessage, $createdUtc, $startedUtc, $completedUtc, $updatedUtc);
            """;
        command.Parameters.AddWithValue("$id", asset.Id.Trim());
        command.Parameters.AddWithValue("$name", asset.Name ?? string.Empty);
        command.Parameters.AddWithValue("$kind", asset.Kind.ToString());
        command.Parameters.AddWithValue("$status", asset.Status.ToString());
        command.Parameters.AddWithValue("$prompt", asset.Prompt ?? string.Empty);
        command.Parameters.AddWithValue("$sourceAssetId", (object?)asset.SourceAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelSnapshotJson", (object?)asset.ModelSnapshotJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileRelativePath", (object?)asset.FileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$mediaType", asset.MediaType ?? string.Empty);
        command.Parameters.AddWithValue("$width", (object?)asset.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)asset.Height ?? DBNull.Value);
        command.Parameters.AddWithValue("$byteLength", asset.ByteLength);
        command.Parameters.AddWithValue("$sha256", asset.Sha256 ?? string.Empty);
        command.Parameters.AddWithValue("$faceView", (object?)asset.FaceView?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$identityPackId", (object?)asset.IdentityPackId ?? DBNull.Value);
        command.Parameters.AddWithValue("$characterProfileId", (object?)asset.CharacterProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)asset.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", asset.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$startedUtc", asset.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", asset.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", asset.UpdatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string assetId, CancellationToken cancellationToken = default)
    {
        Require(assetId, "Asset id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SceneAssets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", assetId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SceneAssets WHERE FileRelativePath = $path;";
        command.Parameters.AddWithValue("$path", fileRelativePath);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    // ---------------- Readers ----------------

    private static SceneAsset ReadAsset(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new SceneAsset
        {
            Id = id,
            Name = reader.GetString(1),
            Kind = ParseEnum<SceneAssetKind>(reader.GetString(2), id, "SceneAssets"),
            Status = ParseEnum<SceneAssetStatus>(reader.GetString(3), id, "SceneAssets"),
            Prompt = reader.GetString(4),
            SourceAssetId = reader.IsDBNull(5) ? null : reader.GetString(5),
            ModelSnapshotJson = reader.IsDBNull(6) ? null : reader.GetString(6),
            FileRelativePath = reader.IsDBNull(7) ? null : reader.GetString(7),
            MediaType = reader.GetString(8),
            Width = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            Height = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            ByteLength = reader.GetInt64(11),
            Sha256 = reader.GetString(12),
            FaceView = reader.IsDBNull(13) ? null : ParseEnum<SceneImageReferenceFaceView>(reader.GetString(13), id, "SceneAssets"),
            IdentityPackId = reader.IsDBNull(14) ? null : reader.GetString(14),
            CharacterProfileId = reader.IsDBNull(15) ? null : reader.GetString(15),
            ErrorMessage = reader.IsDBNull(16) ? null : reader.GetString(16),
            CreatedUtc = ParseUtc(reader.GetString(17), id, "CreatedUtc"),
            StartedUtc = reader.IsDBNull(18) ? null : ParseUtc(reader.GetString(18), id, "StartedUtc"),
            CompletedUtc = reader.IsDBNull(19) ? null : ParseUtc(reader.GetString(19), id, "CompletedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(20), id, "UpdatedUtc")
        };
    }

    private static TEnum ParseEnum<TEnum>(string value, string id, string table) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Invalid {typeof(TEnum).Name} value '{value}' for {table} record '{id}'.");
    }

    private static DateTime ParseUtc(string value, string id, string field)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Invalid UTC value '{value}' for SceneAssets record '{id}' field '{field}'.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneAssets (
                Id                 TEXT PRIMARY KEY,
                Name               TEXT NOT NULL DEFAULT '',
                Kind               TEXT NOT NULL,
                Status             TEXT NOT NULL,
                Prompt             TEXT NOT NULL DEFAULT '',
                SourceAssetId      TEXT NULL,
                ModelSnapshotJson  TEXT NULL,
                FileRelativePath   TEXT NULL,
                MediaType          TEXT NOT NULL DEFAULT '',
                Width              INTEGER NULL,
                Height             INTEGER NULL,
                ByteLength         INTEGER NOT NULL DEFAULT 0,
                Sha256             TEXT NOT NULL DEFAULT '',
                FaceView           TEXT NULL,
                IdentityPackId     TEXT NULL,
                CharacterProfileId TEXT NULL,
                ErrorMessage       TEXT NULL,
                CreatedUtc         TEXT NOT NULL,
                StartedUtc         TEXT NULL,
                CompletedUtc       TEXT NULL,
                UpdatedUtc         TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneAssets_IdentityPackId
                ON SceneAssets (IdentityPackId);
            CREATE INDEX IF NOT EXISTS IX_SceneAssets_Status
                ON SceneAssets (Status);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
