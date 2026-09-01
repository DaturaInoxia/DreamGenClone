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
                     ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                     SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson
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
                     ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                     SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson
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
                     ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                     SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson
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
                ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson)
            VALUES (
                $id, $name, $kind, $status, $prompt, $sourceAssetId, $modelSnapshotJson, $fileRelativePath,
                $mediaType, $width, $height, $byteLength, $sha256, $faceView, $identityPackId, $characterProfileId,
                $errorMessage, $createdUtc, $startedUtc, $completedUtc, $updatedUtc, $type, $associationMetadataJson,
                $sourceApprovalDecisionId, $sourceSceneImageId, $sourceSha256, $sourceProvenanceJson);
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
        AddPromotionParameters(command, asset);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreatePromotedAsync(SceneAsset asset, CancellationToken cancellationToken = default)
    {
        ValidatePromotedAsset(asset);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneAssets (
                Id, Name, Kind, Status, Prompt, FileRelativePath, MediaType, Width, Height, ByteLength,
                Sha256, CharacterProfileId, CreatedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson)
            VALUES ($id, $name, $kind, $status, $prompt, $fileRelativePath, $mediaType, $width, $height, $byteLength,
                $sha256, $characterProfileId, $createdUtc, $completedUtc, $updatedUtc, $type, $associationMetadataJson,
                $sourceApprovalDecisionId, $sourceSceneImageId, $sourceSha256, $sourceProvenanceJson);
            """;
        command.Parameters.AddWithValue("$id", asset.Id.Trim());
        command.Parameters.AddWithValue("$name", asset.Name.Trim());
        command.Parameters.AddWithValue("$kind", asset.Kind.ToString());
        command.Parameters.AddWithValue("$status", asset.Status.ToString());
        command.Parameters.AddWithValue("$prompt", asset.Prompt ?? string.Empty);
        command.Parameters.AddWithValue("$fileRelativePath", asset.FileRelativePath!);
        command.Parameters.AddWithValue("$mediaType", asset.MediaType ?? string.Empty);
        command.Parameters.AddWithValue("$width", (object?)asset.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)asset.Height ?? DBNull.Value);
        command.Parameters.AddWithValue("$byteLength", asset.ByteLength);
        command.Parameters.AddWithValue("$sha256", asset.Sha256);
        command.Parameters.AddWithValue("$characterProfileId", (object?)asset.CharacterProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", asset.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", asset.CompletedUtc!.Value.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", asset.UpdatedUtc.ToString("O"));
        AddPromotionParameters(command, asset);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                $"Approval decision '{asset.SourceApprovalDecisionId}' has already been promoted as {asset.Type} asset '{asset.Name}'.",
                exception);
        }
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
            ,Type = reader.IsDBNull(21) ? null : ParseEnum<SceneAssetType>(reader.GetString(21), id, "SceneAssets")
            ,AssociationMetadataJson = reader.IsDBNull(22) ? null : reader.GetString(22)
            ,SourceApprovalDecisionId = reader.IsDBNull(23) ? null : reader.GetString(23)
            ,SourceSceneImageId = reader.IsDBNull(24) ? null : reader.GetString(24)
            ,SourceSha256 = reader.IsDBNull(25) ? null : reader.GetString(25)
            ,SourceProvenanceJson = reader.IsDBNull(26) ? null : reader.GetString(26)
        };
    }

    private static void AddPromotionParameters(SqliteCommand command, SceneAsset asset)
    {
        command.Parameters.AddWithValue("$type", (object?)asset.Type?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$associationMetadataJson", (object?)asset.AssociationMetadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceApprovalDecisionId", (object?)asset.SourceApprovalDecisionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceSceneImageId", (object?)asset.SourceSceneImageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceSha256", (object?)asset.SourceSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceProvenanceJson", (object?)asset.SourceProvenanceJson ?? DBNull.Value);
    }

    private static void ValidatePromotedAsset(SceneAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Require(asset.Id, "Asset id");
        Require(asset.Name, "Asset name");
        Require(asset.FileRelativePath, "Promoted asset file path");
        Require(asset.Sha256, "Promoted asset SHA-256");
        Require(asset.SourceApprovalDecisionId, "Source approval decision id");
        Require(asset.SourceSceneImageId, "Source scene image id");
        Require(asset.SourceSha256, "Source SHA-256");
        Require(asset.SourceProvenanceJson, "Source provenance");
        if (asset.Status != SceneAssetStatus.Complete || asset.CompletedUtc is null)
            throw new InvalidOperationException("A promoted scene asset must be complete and finalized.");
        if (asset.Type is null || !Enum.IsDefined(asset.Type.Value))
            throw new InvalidOperationException("A promoted scene asset type is required.");
        if (!string.Equals(asset.Sha256, asset.SourceSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Promoted asset checksum must exactly match its source checksum.");
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
                Type               TEXT NULL,
                AssociationMetadataJson TEXT NULL,
                SourceApprovalDecisionId TEXT NULL,
                SourceSceneImageId TEXT NULL,
                SourceSha256       TEXT NULL,
                SourceProvenanceJson TEXT NULL,
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
        foreach (var (name, sql) in new[]
        {
            ("Type", "ALTER TABLE SceneAssets ADD COLUMN Type TEXT NULL"),
            ("AssociationMetadataJson", "ALTER TABLE SceneAssets ADD COLUMN AssociationMetadataJson TEXT NULL"),
            ("SourceApprovalDecisionId", "ALTER TABLE SceneAssets ADD COLUMN SourceApprovalDecisionId TEXT NULL"),
            ("SourceSceneImageId", "ALTER TABLE SceneAssets ADD COLUMN SourceSceneImageId TEXT NULL"),
            ("SourceSha256", "ALTER TABLE SceneAssets ADD COLUMN SourceSha256 TEXT NULL"),
            ("SourceProvenanceJson", "ALTER TABLE SceneAssets ADD COLUMN SourceProvenanceJson TEXT NULL")
        })
        {
            await using var check = connection.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SceneAssets') WHERE name = '{name}'";
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0) continue;
            await using var alter = connection.CreateCommand();
            alter.CommandText = sql;
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var unique = connection.CreateCommand();
        unique.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_SceneAssets_Promotion
            ON SceneAssets (SourceApprovalDecisionId, Type, Name COLLATE NOCASE)
            WHERE SourceApprovalDecisionId IS NOT NULL;
            """;
        await unique.ExecuteNonQueryAsync(cancellationToken);
    }
}
