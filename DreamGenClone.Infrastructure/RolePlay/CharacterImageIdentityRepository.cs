using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for character image identity packs and reference assets. Self-contained
/// schema creation mirrors the other scene-image repositories. Enforces immutable versioning,
/// approval rules, and in-use delete guards.
/// </summary>
public sealed class CharacterImageIdentityRepository : ICharacterImageIdentityRepository
{
    private readonly string _connectionString;

    public CharacterImageIdentityRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    // ---------------- Packs ----------------

    public async Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default)
    {
        Require(packId, "Identity pack id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetPackAsync(connection, packId.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"{PackSelect} WHERE CharacterProfileId = $profileId ORDER BY Version DESC;";
        command.Parameters.AddWithValue("$profileId", characterProfileId.Trim());

        var results = new List<CharacterImageIdentityPack>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPack(reader));
        }

        return results;
    }

    public async Task<CharacterImageIdentityPack?> GetLatestApprovedPackAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"{PackSelect} WHERE CharacterProfileId = $profileId AND Status = 'Approved' ORDER BY Version DESC LIMIT 1;";
        command.Parameters.AddWithValue("$profileId", characterProfileId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPack(reader) : null;
    }

    public async Task<CharacterImageIdentityPack> UpsertDraftAsync(
        CharacterImageIdentityPack pack, CancellationToken cancellationToken = default)
    {
        ValidatePack(pack);
        if (pack.Status != CharacterImageIdentityPackStatus.Draft)
            throw new InvalidOperationException("Only a draft pack can be upserted.");

        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetPackAsync(connection, pack.Id.Trim(), cancellationToken);

        if (existing is null)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO CharacterImageIdentityPacks
                    (Id, CharacterProfileId, Version, Status, DescriptorSnapshotJson, CanonicalFaceAssetId, SupersedesId, CreatedUtc, ApprovedUtc)
                VALUES
                    ($id, $profileId, $version, $status, $descriptor, $canonicalFace, $supersedes, $createdUtc, $approvedUtc);
                """;
            AddPackParameters(command, pack);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return pack;
        }

        if (existing.Status != CharacterImageIdentityPackStatus.Draft)
            throw new InvalidOperationException($"Identity pack '{pack.Id}' is {existing.Status} and cannot be modified; supersede it to create a new version.");

        await using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE CharacterImageIdentityPacks
            SET DescriptorSnapshotJson = $descriptor,
                CanonicalFaceAssetId = $canonicalFace,
                SupersedesId = $supersedes
            WHERE Id = $id;
            """;
        update.Parameters.AddWithValue("$descriptor", pack.DescriptorSnapshotJson);
        update.Parameters.AddWithValue("$canonicalFace", (object?)pack.CanonicalFaceAssetId ?? DBNull.Value);
        update.Parameters.AddWithValue("$supersedes", (object?)pack.SupersedesId ?? DBNull.Value);
        update.Parameters.AddWithValue("$id", pack.Id.Trim());
        await update.ExecuteNonQueryAsync(cancellationToken);

        return (await GetPackAsync(connection, pack.Id.Trim(), cancellationToken))!;
    }

    public async Task<CharacterImageIdentityPack> ApproveAsync(
        string packId,
        string descriptorSnapshotJson,
        string canonicalFaceAssetId,
        CancellationToken cancellationToken = default)
    {
        Require(packId, "Identity pack id");
        Require(descriptorSnapshotJson, "Descriptor snapshot");
        Require(canonicalFaceAssetId, "Canonical face asset id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var pack = await GetPackAsync(connection, packId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{packId}' was not found.");
        if (pack.Status != CharacterImageIdentityPackStatus.Draft)
            throw new InvalidOperationException($"Only a draft pack can be approved; pack '{packId}' is {pack.Status}.");

        var assets = await ListAssetsAsync(connection, pack.Id, cancellationToken);
        if (assets.Count == 0)
            throw new InvalidOperationException("An identity pack requires at least one reference asset before approval.");

        foreach (var asset in assets)
        {
            if (string.IsNullOrWhiteSpace(asset.SourceLabel))
                throw new InvalidOperationException($"Reference asset '{asset.Id}' requires provenance before the pack can be approved.");
            if (asset.ConsentState == SceneImageReferenceConsentState.Unknown)
                throw new InvalidOperationException($"Reference asset '{asset.Id}' requires a confirmed or not-applicable consent state before the pack can be approved.");
        }

        var canonicalFace = assets.FirstOrDefault(a => a.Id == canonicalFaceAssetId.Trim());
        if (canonicalFace is null)
            throw new InvalidOperationException("The canonical face asset must belong to the pack being approved.");
        if (canonicalFace.AssetKind != SceneImageReferenceAssetKind.Face)
            throw new InvalidOperationException("The canonical face asset must be a Face reference.");
        if (!canonicalFace.IsApproved)
            throw new InvalidOperationException("The canonical face asset must be approved before the pack can be approved.");

        var approvedUtc = DateTime.UtcNow;
        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = """
            UPDATE CharacterImageIdentityPacks
            SET Status = 'Approved',
                DescriptorSnapshotJson = $descriptor,
                CanonicalFaceAssetId = $canonicalFace,
                ApprovedUtc = $approvedUtc
            WHERE Id = $id;
            """;
        update.Parameters.AddWithValue("$descriptor", descriptorSnapshotJson);
        update.Parameters.AddWithValue("$canonicalFace", canonicalFaceAssetId.Trim());
        update.Parameters.AddWithValue("$approvedUtc", approvedUtc.ToString("O"));
        update.Parameters.AddWithValue("$id", packId.Trim());
        await update.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return (await GetPackAsync(connection, packId.Trim(), cancellationToken))!;
    }

    public async Task<CharacterImageIdentityPack> SupersedeAsync(string packId, CancellationToken cancellationToken = default)
    {
        Require(packId, "Identity pack id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var pack = await GetPackAsync(connection, packId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{packId}' was not found.");
        if (pack.Status != CharacterImageIdentityPackStatus.Approved)
            throw new InvalidOperationException($"Only an approved pack can be superseded; pack '{packId}' is {pack.Status}.");

        var assets = await ListAssetsAsync(connection, pack.Id, cancellationToken);

        var nextVersion = await GetNextVersionAsync(connection, (SqliteTransaction)transaction, pack.CharacterProfileId, cancellationToken);

        await using var retire = connection.CreateCommand();
        retire.Transaction = (SqliteTransaction)transaction;
        retire.CommandText = "UPDATE CharacterImageIdentityPacks SET Status = 'Superseded' WHERE Id = $id;";
        retire.Parameters.AddWithValue("$id", packId.Trim());
        await retire.ExecuteNonQueryAsync(cancellationToken);

        var newPack = new CharacterImageIdentityPack
        {
            CharacterProfileId = pack.CharacterProfileId,
            Version = nextVersion,
            Status = CharacterImageIdentityPackStatus.Draft,
            DescriptorSnapshotJson = pack.DescriptorSnapshotJson,
            SupersedesId = pack.Id,
            CreatedUtc = DateTime.UtcNow
        };

        await using var insert = connection.CreateCommand();
        insert.Transaction = (SqliteTransaction)transaction;
        insert.CommandText = """
            INSERT INTO CharacterImageIdentityPacks
                (Id, CharacterProfileId, Version, Status, DescriptorSnapshotJson, CanonicalFaceAssetId, SupersedesId, CreatedUtc, ApprovedUtc)
            VALUES
                ($id, $profileId, $version, $status, $descriptor, $canonicalFace, $supersedes, $createdUtc, $approvedUtc);
            """;
        AddPackParameters(insert, newPack);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        // Carry forward the reference assets so the new draft is an editable copy. Each asset gets a
        // new id but shares the same immutable file (path + checksum). Provenance/consent/approval
        // are inherited; the user may re-approve the copied pack.
        foreach (var asset in assets)
        {
            var copy = new SceneImageReferenceAsset
            {
                IdentityPackId = newPack.Id,
                AssetKind = asset.AssetKind,
                FileRelativePath = asset.FileRelativePath,
                MediaType = asset.MediaType,
                Width = asset.Width,
                Height = asset.Height,
                ByteLength = asset.ByteLength,
                Sha256 = asset.Sha256,
                SourceLabel = asset.SourceLabel,
                ConsentState = asset.ConsentState,
                IsApproved = asset.IsApproved,
                CreatedUtc = DateTime.UtcNow
            };
            await InsertAssetAsync(connection, (SqliteTransaction)transaction, copy, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return (await GetPackAsync(connection, newPack.Id, cancellationToken))!;
    }

    public async Task DeletePackAsync(string packId, CancellationToken cancellationToken = default)
    {
        Require(packId, "Identity pack id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var pack = await GetPackAsync(connection, packId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{packId}' was not found.");
        if (pack.Status != CharacterImageIdentityPackStatus.Draft)
            throw new InvalidOperationException($"Identity pack '{packId}' is {pack.Status} and cannot be deleted; only draft packs can be deleted.");

        await using var deleteAssets = connection.CreateCommand();
        deleteAssets.Transaction = (SqliteTransaction)transaction;
        deleteAssets.CommandText = "DELETE FROM SceneImageReferenceAssets WHERE IdentityPackId = $packId;";
        deleteAssets.Parameters.AddWithValue("$packId", packId.Trim());
        await deleteAssets.ExecuteNonQueryAsync(cancellationToken);

        await using var deletePack = connection.CreateCommand();
        deletePack.Transaction = (SqliteTransaction)transaction;
        deletePack.CommandText = "DELETE FROM CharacterImageIdentityPacks WHERE Id = $id;";
        deletePack.Parameters.AddWithValue("$id", packId.Trim());
        await deletePack.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    // ---------------- Assets ----------------

    public async Task AddAssetAsync(SceneImageReferenceAsset asset, CancellationToken cancellationToken = default)
    {
        ValidateAsset(asset);

        await using var connection = await OpenAsync(cancellationToken);
        var pack = await GetPackAsync(connection, asset.IdentityPackId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{asset.IdentityPackId}' was not found.");
        if (pack.Status != CharacterImageIdentityPackStatus.Draft)
            throw new InvalidOperationException("Reference assets can only be added to a draft pack.");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneImageReferenceAssets
                (Id, IdentityPackId, AssetKind, FileRelativePath, MediaType, Width, Height, ByteLength, Sha256, SourceLabel, ConsentState, IsApproved, CreatedUtc)
            VALUES
                ($id, $packId, $kind, $path, $mediaType, $width, $height, $byteLength, $sha256, $sourceLabel, $consent, $approved, $createdUtc);
            """;
        AddAssetParameters(command, asset);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneImageReferenceAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        Require(assetId, "Reference asset id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetAssetAsync(connection, assetId.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
        string packId, CancellationToken cancellationToken = default)
    {
        Require(packId, "Identity pack id");
        await using var connection = await OpenAsync(cancellationToken);
        return await ListAssetsAsync(connection, packId.Trim(), cancellationToken);
    }

    public async Task UpdateAssetProvenanceAsync(
        string assetId,
        string sourceLabel,
        SceneImageReferenceConsentState consentState,
        CancellationToken cancellationToken = default)
    {
        Require(assetId, "Reference asset id");
        Require(sourceLabel, "Source label");
        if (consentState == SceneImageReferenceConsentState.Unknown)
            throw new InvalidOperationException("A reference asset's consent state cannot be reset to Unknown.");

        await using var connection = await OpenAsync(cancellationToken);
        var asset = await GetAssetAsync(connection, assetId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Reference asset '{assetId}' was not found.");
        await RequireDraftPackAsync(connection, asset.IdentityPackId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SceneImageReferenceAssets SET SourceLabel = $sourceLabel, ConsentState = $consent WHERE Id = $id;";
        command.Parameters.AddWithValue("$sourceLabel", sourceLabel.Trim());
        command.Parameters.AddWithValue("$consent", consentState.ToString());
        command.Parameters.AddWithValue("$id", assetId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default)
    {
        Require(assetId, "Reference asset id");

        await using var connection = await OpenAsync(cancellationToken);
        var asset = await GetAssetAsync(connection, assetId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Reference asset '{assetId}' was not found.");
        await RequireDraftPackAsync(connection, asset.IdentityPackId, cancellationToken);

        if (isApproved)
        {
            if (string.IsNullOrWhiteSpace(asset.SourceLabel))
                throw new InvalidOperationException("A reference asset requires provenance before it can be approved.");
            if (asset.ConsentState == SceneImageReferenceConsentState.Unknown)
                throw new InvalidOperationException("A reference asset requires a confirmed or not-applicable consent state before it can be approved.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SceneImageReferenceAssets SET IsApproved = $approved WHERE Id = $id;";
        command.Parameters.AddWithValue("$approved", isApproved ? 1 : 0);
        command.Parameters.AddWithValue("$id", assetId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        Require(assetId, "Reference asset id");

        await using var connection = await OpenAsync(cancellationToken);
        var asset = await GetAssetAsync(connection, assetId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Reference asset '{assetId}' was not found.");
        await RequireDraftPackAsync(connection, asset.IdentityPackId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SceneImageReferenceAssets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", assetId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountAssetsByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
    {
        Require(fileRelativePath, "Reference asset file path");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SceneImageReferenceAssets WHERE FileRelativePath = $path;";
        command.Parameters.AddWithValue("$path", fileRelativePath.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    // ---------------- Schema and helpers ----------------

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CharacterImageIdentityPacks (
                Id TEXT PRIMARY KEY,
                CharacterProfileId TEXT NOT NULL,
                Version INTEGER NOT NULL CHECK (Version > 0),
                Status TEXT NOT NULL,
                DescriptorSnapshotJson TEXT NOT NULL DEFAULT '{}',
                CanonicalFaceAssetId TEXT NULL,
                SupersedesId TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                ApprovedUtc TEXT NULL,
                UNIQUE (CharacterProfileId, Version)
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterImageIdentityPacks_Profile
                ON CharacterImageIdentityPacks (CharacterProfileId, Version DESC);

            CREATE TABLE IF NOT EXISTS SceneImageReferenceAssets (
                Id TEXT PRIMARY KEY,
                IdentityPackId TEXT NOT NULL,
                AssetKind TEXT NOT NULL,
                FileRelativePath TEXT NOT NULL,
                MediaType TEXT NOT NULL,
                Width INTEGER NULL,
                Height INTEGER NULL,
                ByteLength INTEGER NOT NULL,
                Sha256 TEXT NOT NULL,
                SourceLabel TEXT NOT NULL DEFAULT '',
                ConsentState TEXT NOT NULL,
                IsApproved INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                FOREIGN KEY (IdentityPackId) REFERENCES CharacterImageIdentityPacks(Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImageReferenceAssets_Pack
                ON SceneImageReferenceAssets (IdentityPackId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string PackSelect = """
        SELECT Id, CharacterProfileId, Version, Status, DescriptorSnapshotJson, CanonicalFaceAssetId, SupersedesId, CreatedUtc, ApprovedUtc
        FROM CharacterImageIdentityPacks
        """;

    private const string AssetSelect = """
        SELECT Id, IdentityPackId, AssetKind, FileRelativePath, MediaType, Width, Height, ByteLength, Sha256, SourceLabel, ConsentState, IsApproved, CreatedUtc
        FROM SceneImageReferenceAssets
        """;

    private static async Task<CharacterImageIdentityPack?> GetPackAsync(
        SqliteConnection connection, string packId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{PackSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", packId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPack(reader) : null;
    }

    private static async Task<SceneImageReferenceAsset?> GetAssetAsync(
        SqliteConnection connection, string assetId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{AssetSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAsset(reader) : null;
    }

    private static async Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
        SqliteConnection connection, string packId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{AssetSelect} WHERE IdentityPackId = $packId ORDER BY CreatedUtc;";
        command.Parameters.AddWithValue("$packId", packId);
        var results = new List<SceneImageReferenceAsset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAsset(reader));
        }

        return results;
    }

    private static async Task RequireDraftPackAsync(
        SqliteConnection connection, string packId, CancellationToken cancellationToken)
    {
        var pack = await GetPackAsync(connection, packId, cancellationToken)
            ?? throw new InvalidOperationException($"Identity pack '{packId}' was not found.");
        if (pack.Status != CharacterImageIdentityPackStatus.Draft)
            throw new InvalidOperationException($"Identity pack '{packId}' is {pack.Status}; only draft packs can be modified.");
    }

    private static async Task<int> GetNextVersionAsync(
        SqliteConnection connection, SqliteTransaction transaction, string characterProfileId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) + 1 FROM CharacterImageIdentityPacks WHERE CharacterProfileId = $profileId;";
        command.Parameters.AddWithValue("$profileId", characterProfileId.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertAssetAsync(
        SqliteConnection connection, SqliteTransaction transaction, SceneImageReferenceAsset asset, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SceneImageReferenceAssets
                (Id, IdentityPackId, AssetKind, FileRelativePath, MediaType, Width, Height, ByteLength, Sha256, SourceLabel, ConsentState, IsApproved, CreatedUtc)
            VALUES
                ($id, $packId, $kind, $path, $mediaType, $width, $height, $byteLength, $sha256, $sourceLabel, $consent, $approved, $createdUtc);
            """;
        AddAssetParameters(command, asset);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CharacterImageIdentityPack ReadPack(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new CharacterImageIdentityPack
        {
            Id = id,
            CharacterProfileId = reader.GetString(1),
            Version = reader.GetInt32(2),
            Status = ParseEnum<CharacterImageIdentityPackStatus>(reader.GetString(3), "identity pack", id),
            DescriptorSnapshotJson = reader.GetString(4),
            CanonicalFaceAssetId = reader.IsDBNull(5) ? null : reader.GetString(5),
            SupersedesId = reader.IsDBNull(6) ? null : reader.GetString(6),
            CreatedUtc = ParseUtc(reader.GetString(7), "identity pack", id),
            ApprovedUtc = reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8), "identity pack", id)
        };
    }

    private static SceneImageReferenceAsset ReadAsset(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new SceneImageReferenceAsset
        {
            Id = id,
            IdentityPackId = reader.GetString(1),
            AssetKind = ParseEnum<SceneImageReferenceAssetKind>(reader.GetString(2), "reference asset", id),
            FileRelativePath = reader.GetString(3),
            MediaType = reader.GetString(4),
            Width = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            Height = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            ByteLength = reader.GetInt64(7),
            Sha256 = reader.GetString(8),
            SourceLabel = reader.GetString(9),
            ConsentState = ParseEnum<SceneImageReferenceConsentState>(reader.GetString(10), "reference asset", id),
            IsApproved = reader.GetInt32(11) != 0,
            CreatedUtc = ParseUtc(reader.GetString(12), "reference asset", id)
        };
    }

    private static void AddPackParameters(SqliteCommand command, CharacterImageIdentityPack pack)
    {
        command.Parameters.AddWithValue("$id", pack.Id.Trim());
        command.Parameters.AddWithValue("$profileId", pack.CharacterProfileId.Trim());
        command.Parameters.AddWithValue("$version", pack.Version);
        command.Parameters.AddWithValue("$status", pack.Status.ToString());
        command.Parameters.AddWithValue("$descriptor", pack.DescriptorSnapshotJson);
        command.Parameters.AddWithValue("$canonicalFace", (object?)pack.CanonicalFaceAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$supersedes", (object?)pack.SupersedesId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", pack.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$approvedUtc", pack.ApprovedUtc?.ToString("O") ?? (object)DBNull.Value);
    }

    private static void AddAssetParameters(SqliteCommand command, SceneImageReferenceAsset asset)
    {
        command.Parameters.AddWithValue("$id", asset.Id.Trim());
        command.Parameters.AddWithValue("$packId", asset.IdentityPackId.Trim());
        command.Parameters.AddWithValue("$kind", asset.AssetKind.ToString());
        command.Parameters.AddWithValue("$path", asset.FileRelativePath);
        command.Parameters.AddWithValue("$mediaType", asset.MediaType);
        command.Parameters.AddWithValue("$width", (object?)asset.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)asset.Height ?? DBNull.Value);
        command.Parameters.AddWithValue("$byteLength", asset.ByteLength);
        command.Parameters.AddWithValue("$sha256", NormalizeSha256(asset.Sha256));
        command.Parameters.AddWithValue("$sourceLabel", asset.SourceLabel);
        command.Parameters.AddWithValue("$consent", asset.ConsentState.ToString());
        command.Parameters.AddWithValue("$approved", asset.IsApproved ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", asset.CreatedUtc.ToString("O"));
    }

    private static void ValidatePack(CharacterImageIdentityPack pack)
    {
        Require(pack.Id, "Identity pack id");
        Require(pack.CharacterProfileId, "Character profile id");
        if (pack.Version <= 0) throw new InvalidOperationException("Identity pack version must be positive.");
    }

    private static void ValidateAsset(SceneImageReferenceAsset asset)
    {
        Require(asset.Id, "Reference asset id");
        Require(asset.IdentityPackId, "Identity pack id");
        Require(asset.FileRelativePath, "Reference asset file path");
        Require(asset.MediaType, "Reference asset media type");
        RequireSha256(asset.Sha256, "Reference asset checksum");
        if (asset.ByteLength <= 0) throw new InvalidOperationException("Reference asset byte length must be positive.");
        if (asset.AssetKind == default) throw new InvalidOperationException("Reference asset kind must be explicit.");
        if (asset.ConsentState == SceneImageReferenceConsentState.Unknown && asset.IsApproved)
            throw new InvalidOperationException("A reference asset with unknown consent cannot be approved.");
    }

    private static string NormalizeSha256(string value) => value.Trim().ToUpperInvariant();

    private static void RequireSha256(string value, string label)
    {
        Require(value, label);
        var normalized = NormalizeSha256(value);
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"{label} must be a 64-character SHA-256 value.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static T ParseEnum<T>(string value, string entity, string id) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) && Convert.ToInt32(parsed) != 0
            ? parsed
            : throw new InvalidOperationException($"Stored {entity} '{id}' has invalid {typeof(T).Name} value '{value}'.");

    private static DateTime ParseUtc(string value, string entity, string id) =>
        DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Stored {entity} '{id}' has invalid UTC timestamp '{value}'.");
}
