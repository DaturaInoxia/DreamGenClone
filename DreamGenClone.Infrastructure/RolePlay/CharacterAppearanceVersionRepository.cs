using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class CharacterAppearanceVersionRepository : ICharacterAppearanceVersionRepository
{
    private readonly string _connectionString;

    public CharacterAppearanceVersionRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<CharacterBodyProfileVersion?> GetBodyProfileAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Body profile version id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetBodyProfileAsync(connection, null, id.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterBodyProfileVersion>> ListBodyProfilesAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{BodyProfileSelect} WHERE CharacterProfileId = $owner ORDER BY Version DESC;";
        command.Parameters.AddWithValue("$owner", characterProfileId.Trim());
        return await ReadBodyProfilesAsync(command, cancellationToken);
    }

    public async Task<CharacterBodyProfileVersion?> GetLatestApprovedBodyProfileAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{BodyProfileSelect} WHERE CharacterProfileId = $owner AND Status = 'Approved' ORDER BY Version DESC LIMIT 1;";
        command.Parameters.AddWithValue("$owner", characterProfileId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBodyProfile(reader) : null;
    }

    public async Task<CharacterBodyProfileVersion> CreateBodyProfileDraftAsync(
        CharacterBodyProfileVersion version, CancellationToken cancellationToken = default)
    {
        ValidateBodyProfileDraft(version);
        await using var connection = await OpenAsync(cancellationToken);
        await InsertBodyProfileAsync(connection, null, version, cancellationToken);
        return (await GetBodyProfileAsync(connection, null, version.Id.Trim(), cancellationToken))!;
    }

    public async Task AddBodyAssetBindingAsync(
        CharacterBodyAssetBinding binding, CancellationToken cancellationToken = default)
    {
        ValidateBodyBinding(binding);
        await using var connection = await OpenAsync(cancellationToken);
        await RequireBodyDraftAsync(connection, null, binding.BodyProfileVersionId, cancellationToken);
        await RequireSceneAssetAsync(connection, null, binding.SceneAssetId, SceneAssetType.CharacterBody, cancellationToken);
        await InsertBodyBindingAsync(connection, null, binding, cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterBodyAssetBinding>> ListBodyAssetBindingsAsync(
        string bodyProfileVersionId, CancellationToken cancellationToken = default)
    {
        Require(bodyProfileVersionId, "Body profile version id");
        await using var connection = await OpenAsync(cancellationToken);
        return await ListBodyBindingsAsync(connection, null, bodyProfileVersionId.Trim(), cancellationToken);
    }

    public async Task<CharacterBodyProfileVersion> ApproveBodyProfileAsync(
        string id, string descriptorSnapshotJson, CancellationToken cancellationToken = default)
    {
        Require(id, "Body profile version id");
        RequireJson(descriptorSnapshotJson, "Body descriptor snapshot");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var profile = await RequireBodyDraftAsync(connection, transaction, id.Trim(), cancellationToken);
        var bindings = await ListBodyBindingsAsync(connection, transaction, profile.Id, cancellationToken);
        if (bindings.Count == 0)
            throw new InvalidOperationException("A body profile requires at least one body asset binding before approval.");
        foreach (var binding in bindings)
        {
            await RequireApprovedSceneAssetAsync(
                connection,
                transaction,
                binding.SceneAssetId,
                SceneAssetType.CharacterBody,
                SceneAssetApprovedUseScope.CharacterBody,
                cancellationToken);
        }

        var approvedUtc = DateTime.UtcNow;
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE CharacterBodyProfileVersions
            SET Status = 'Approved', DescriptorSnapshotJson = $descriptor, ApprovedUtc = $approvedUtc
            WHERE Id = $id AND Status = 'Draft';
            """;
        update.Parameters.AddWithValue("$descriptor", descriptorSnapshotJson.Trim());
        update.Parameters.AddWithValue("$approvedUtc", approvedUtc.ToString("O"));
        update.Parameters.AddWithValue("$id", profile.Id);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Body profile version '{profile.Id}' changed before approval completed.");
        await transaction.CommitAsync(cancellationToken);
        return (await GetBodyProfileAsync(connection, null, profile.Id, cancellationToken))!;
    }

    public async Task<CharacterBodyProfileVersion> SupersedeBodyProfileAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Body profile version id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var current = await GetBodyProfileAsync(connection, transaction, id.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Body profile version '{id}' was not found.");
        if (current.Status != CharacterAppearanceVersionStatus.Approved)
            throw new InvalidOperationException($"Only an approved body profile can be superseded; '{id}' is {current.Status}.");

        var next = new CharacterBodyProfileVersion
        {
            CharacterProfileId = current.CharacterProfileId,
            Version = await GetNextVersionAsync(connection, transaction, "CharacterBodyProfileVersions", current.CharacterProfileId, cancellationToken),
            Status = CharacterAppearanceVersionStatus.Draft,
            DescriptorSnapshotJson = current.DescriptorSnapshotJson,
            SupersedesId = current.Id,
            CreatedUtc = DateTime.UtcNow
        };
        await RetireVersionAsync(connection, transaction, "CharacterBodyProfileVersions", current.Id, cancellationToken);
        await InsertBodyProfileAsync(connection, transaction, next, cancellationToken);
        foreach (var binding in await ListBodyBindingsAsync(connection, transaction, current.Id, cancellationToken))
        {
            binding.Id = Guid.NewGuid().ToString("N");
            binding.BodyProfileVersionId = next.Id;
            binding.CreatedUtc = DateTime.UtcNow;
            await InsertBodyBindingAsync(connection, transaction, binding, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    public async Task DeleteBodyProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Body profile version id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var profile = await RequireBodyDraftAsync(connection, transaction, id.Trim(), cancellationToken);
        await RestoreSupersededParentAsync(
            connection, transaction, "CharacterBodyProfileVersions", profile.SupersedesId, cancellationToken);
        await DeleteVersionAsync(
            connection, transaction, "CharacterBodyAssetBindings", "BodyProfileVersionId",
            "CharacterBodyProfileVersions", profile.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CharacterWardrobeLookVersion?> GetWardrobeLookAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Wardrobe look version id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetWardrobeLookAsync(connection, null, id.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterWardrobeLookVersion>> ListWardrobeLooksAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{WardrobeLookSelect} WHERE CharacterProfileId = $owner ORDER BY Version DESC;";
        command.Parameters.AddWithValue("$owner", characterProfileId.Trim());
        return await ReadWardrobeLooksAsync(command, cancellationToken);
    }

    public async Task<CharacterWardrobeLookVersion?> GetLatestApprovedWardrobeLookAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{WardrobeLookSelect} WHERE CharacterProfileId = $owner AND Status = 'Approved' ORDER BY Version DESC LIMIT 1;";
        command.Parameters.AddWithValue("$owner", characterProfileId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadWardrobeLook(reader) : null;
    }

    public async Task<CharacterWardrobeLookVersion> CreateWardrobeLookDraftAsync(
        CharacterWardrobeLookVersion version, CancellationToken cancellationToken = default)
    {
        ValidateWardrobeLookDraft(version);
        await using var connection = await OpenAsync(cancellationToken);
        await InsertWardrobeLookAsync(connection, null, version, cancellationToken);
        return (await GetWardrobeLookAsync(connection, null, version.Id.Trim(), cancellationToken))!;
    }

    public async Task AddWardrobeAssetBindingAsync(
        CharacterWardrobeAssetBinding binding, CancellationToken cancellationToken = default)
    {
        ValidateWardrobeBinding(binding);
        await using var connection = await OpenAsync(cancellationToken);
        await RequireWardrobeDraftAsync(connection, null, binding.WardrobeLookVersionId, cancellationToken);
        await RequireSceneAssetAsync(connection, null, binding.SceneAssetId, SceneAssetType.Wardrobe, cancellationToken);
        await InsertWardrobeBindingAsync(connection, null, binding, cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterWardrobeAssetBinding>> ListWardrobeAssetBindingsAsync(
        string wardrobeLookVersionId, CancellationToken cancellationToken = default)
    {
        Require(wardrobeLookVersionId, "Wardrobe look version id");
        await using var connection = await OpenAsync(cancellationToken);
        return await ListWardrobeBindingsAsync(connection, null, wardrobeLookVersionId.Trim(), cancellationToken);
    }

    public async Task<CharacterWardrobeLookVersion> ApproveWardrobeLookAsync(
        string id,
        string descriptorSnapshotJson,
        string coverageFactsJson,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Wardrobe look version id");
        RequireJson(descriptorSnapshotJson, "Wardrobe descriptor snapshot");
        RequireJson(coverageFactsJson, "Wardrobe coverage facts");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var look = await RequireWardrobeDraftAsync(connection, transaction, id.Trim(), cancellationToken);
        var bindings = await ListWardrobeBindingsAsync(connection, transaction, look.Id, cancellationToken);
        if (bindings.Count == 0)
            throw new InvalidOperationException("A wardrobe look requires at least one wardrobe asset binding before approval.");
        foreach (var binding in bindings)
        {
            await RequireApprovedSceneAssetAsync(
                connection,
                transaction,
                binding.SceneAssetId,
                SceneAssetType.Wardrobe,
                SceneAssetApprovedUseScope.CharacterWardrobe,
                cancellationToken);
        }

        var approvedUtc = DateTime.UtcNow;
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE CharacterWardrobeLookVersions
            SET Status = 'Approved', DescriptorSnapshotJson = $descriptor,
                CoverageFactsJson = $coverage, ApprovedUtc = $approvedUtc
            WHERE Id = $id AND Status = 'Draft';
            """;
        update.Parameters.AddWithValue("$descriptor", descriptorSnapshotJson.Trim());
        update.Parameters.AddWithValue("$coverage", coverageFactsJson.Trim());
        update.Parameters.AddWithValue("$approvedUtc", approvedUtc.ToString("O"));
        update.Parameters.AddWithValue("$id", look.Id);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Wardrobe look version '{look.Id}' changed before approval completed.");
        await transaction.CommitAsync(cancellationToken);
        return (await GetWardrobeLookAsync(connection, null, look.Id, cancellationToken))!;
    }

    public async Task<CharacterWardrobeLookVersion> SupersedeWardrobeLookAsync(
        string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Wardrobe look version id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var current = await GetWardrobeLookAsync(connection, transaction, id.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe look version '{id}' was not found.");
        if (current.Status != CharacterAppearanceVersionStatus.Approved)
            throw new InvalidOperationException($"Only an approved wardrobe look can be superseded; '{id}' is {current.Status}.");

        var next = new CharacterWardrobeLookVersion
        {
            CharacterProfileId = current.CharacterProfileId,
            Version = await GetNextVersionAsync(connection, transaction, "CharacterWardrobeLookVersions", current.CharacterProfileId, cancellationToken),
            Status = CharacterAppearanceVersionStatus.Draft,
            DescriptorSnapshotJson = current.DescriptorSnapshotJson,
            CoverageFactsJson = current.CoverageFactsJson,
            SupersedesId = current.Id,
            CreatedUtc = DateTime.UtcNow
        };
        await RetireVersionAsync(connection, transaction, "CharacterWardrobeLookVersions", current.Id, cancellationToken);
        await InsertWardrobeLookAsync(connection, transaction, next, cancellationToken);
        foreach (var binding in await ListWardrobeBindingsAsync(connection, transaction, current.Id, cancellationToken))
        {
            binding.Id = Guid.NewGuid().ToString("N");
            binding.WardrobeLookVersionId = next.Id;
            binding.CreatedUtc = DateTime.UtcNow;
            await InsertWardrobeBindingAsync(connection, transaction, binding, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    public async Task DeleteWardrobeLookAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Wardrobe look version id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var look = await RequireWardrobeDraftAsync(connection, transaction, id.Trim(), cancellationToken);
        await RestoreSupersededParentAsync(
            connection, transaction, "CharacterWardrobeLookVersions", look.SupersedesId, cancellationToken);
        await DeleteVersionAsync(
            connection, transaction, "CharacterWardrobeAssetBindings", "WardrobeLookVersionId",
            "CharacterWardrobeLookVersions", look.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

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
            CREATE TABLE IF NOT EXISTS CharacterBodyProfileVersions (
                Id TEXT PRIMARY KEY,
                CharacterProfileId TEXT NOT NULL,
                Version INTEGER NOT NULL CHECK (Version > 0),
                Status TEXT NOT NULL,
                DescriptorSnapshotJson TEXT NOT NULL,
                SupersedesId TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                ApprovedUtc TEXT NULL,
                UNIQUE (CharacterProfileId, Version),
                FOREIGN KEY (SupersedesId) REFERENCES CharacterBodyProfileVersions(Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterBodyProfileVersions_Owner
                ON CharacterBodyProfileVersions (CharacterProfileId, Version DESC);

            CREATE TABLE IF NOT EXISTS CharacterBodyAssetBindings (
                Id TEXT PRIMARY KEY,
                BodyProfileVersionId TEXT NOT NULL,
                SceneAssetId TEXT NOT NULL,
                SemanticRole TEXT NOT NULL,
                Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
                CropFactsJson TEXT NOT NULL,
                AngleFactsJson TEXT NOT NULL,
                BodyCoverageJson TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UNIQUE (BodyProfileVersionId, Ordinal),
                UNIQUE (BodyProfileVersionId, SemanticRole),
                FOREIGN KEY (BodyProfileVersionId) REFERENCES CharacterBodyProfileVersions(Id) ON DELETE RESTRICT,
                FOREIGN KEY (SceneAssetId) REFERENCES SceneAssets(Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterBodyAssetBindings_Version
                ON CharacterBodyAssetBindings (BodyProfileVersionId, Ordinal);
            CREATE INDEX IF NOT EXISTS IX_CharacterBodyAssetBindings_Asset
                ON CharacterBodyAssetBindings (SceneAssetId);

            CREATE TABLE IF NOT EXISTS CharacterWardrobeLookVersions (
                Id TEXT PRIMARY KEY,
                CharacterProfileId TEXT NOT NULL,
                Version INTEGER NOT NULL CHECK (Version > 0),
                Status TEXT NOT NULL,
                DescriptorSnapshotJson TEXT NOT NULL,
                CoverageFactsJson TEXT NOT NULL,
                SupersedesId TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                ApprovedUtc TEXT NULL,
                UNIQUE (CharacterProfileId, Version),
                FOREIGN KEY (SupersedesId) REFERENCES CharacterWardrobeLookVersions(Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterWardrobeLookVersions_Owner
                ON CharacterWardrobeLookVersions (CharacterProfileId, Version DESC);

            CREATE TABLE IF NOT EXISTS CharacterWardrobeAssetBindings (
                Id TEXT PRIMARY KEY,
                WardrobeLookVersionId TEXT NOT NULL,
                SceneAssetId TEXT NOT NULL,
                SemanticRole TEXT NOT NULL,
                Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
                GarmentFactsJson TEXT NOT NULL,
                ColorFactsJson TEXT NOT NULL,
                BodyCoverageJson TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UNIQUE (WardrobeLookVersionId, Ordinal),
                UNIQUE (WardrobeLookVersionId, SemanticRole),
                FOREIGN KEY (WardrobeLookVersionId) REFERENCES CharacterWardrobeLookVersions(Id) ON DELETE RESTRICT,
                FOREIGN KEY (SceneAssetId) REFERENCES SceneAssets(Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterWardrobeAssetBindings_Version
                ON CharacterWardrobeAssetBindings (WardrobeLookVersionId, Ordinal);
            CREATE INDEX IF NOT EXISTS IX_CharacterWardrobeAssetBindings_Asset
                ON CharacterWardrobeAssetBindings (SceneAssetId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string BodyProfileSelect = """
        SELECT Id, CharacterProfileId, Version, Status, DescriptorSnapshotJson, SupersedesId, CreatedUtc, ApprovedUtc
        FROM CharacterBodyProfileVersions
        """;

    private const string WardrobeLookSelect = """
        SELECT Id, CharacterProfileId, Version, Status, DescriptorSnapshotJson, CoverageFactsJson, SupersedesId, CreatedUtc, ApprovedUtc
        FROM CharacterWardrobeLookVersions
        """;

    private static async Task<CharacterBodyProfileVersion?> GetBodyProfileAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"{BodyProfileSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBodyProfile(reader) : null;
    }

    private static async Task<CharacterWardrobeLookVersion?> GetWardrobeLookAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"{WardrobeLookSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadWardrobeLook(reader) : null;
    }

    private static CharacterBodyProfileVersion ReadBodyProfile(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        CharacterProfileId = reader.GetString(1),
        Version = reader.GetInt32(2),
        Status = ParseEnum<CharacterAppearanceVersionStatus>(reader.GetString(3), "body profile", reader.GetString(0)),
        DescriptorSnapshotJson = reader.GetString(4),
        SupersedesId = reader.IsDBNull(5) ? null : reader.GetString(5),
        CreatedUtc = ParseUtc(reader.GetString(6), "body profile", reader.GetString(0)),
        ApprovedUtc = reader.IsDBNull(7) ? null : ParseUtc(reader.GetString(7), "body profile", reader.GetString(0))
    };

    private static CharacterWardrobeLookVersion ReadWardrobeLook(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        CharacterProfileId = reader.GetString(1),
        Version = reader.GetInt32(2),
        Status = ParseEnum<CharacterAppearanceVersionStatus>(reader.GetString(3), "wardrobe look", reader.GetString(0)),
        DescriptorSnapshotJson = reader.GetString(4),
        CoverageFactsJson = reader.GetString(5),
        SupersedesId = reader.IsDBNull(6) ? null : reader.GetString(6),
        CreatedUtc = ParseUtc(reader.GetString(7), "wardrobe look", reader.GetString(0)),
        ApprovedUtc = reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8), "wardrobe look", reader.GetString(0))
    };

    private static async Task<IReadOnlyList<CharacterBodyProfileVersion>> ReadBodyProfilesAsync(
        SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<CharacterBodyProfileVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadBodyProfile(reader));
        return results;
    }

    private static async Task<IReadOnlyList<CharacterWardrobeLookVersion>> ReadWardrobeLooksAsync(
        SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<CharacterWardrobeLookVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadWardrobeLook(reader));
        return results;
    }

    private static async Task<IReadOnlyList<CharacterBodyAssetBinding>> ListBodyBindingsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string versionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, BodyProfileVersionId, SceneAssetId, SemanticRole, Ordinal,
                   CropFactsJson, AngleFactsJson, BodyCoverageJson, CreatedUtc
            FROM CharacterBodyAssetBindings WHERE BodyProfileVersionId = $id ORDER BY Ordinal;
            """;
        command.Parameters.AddWithValue("$id", versionId);
        var results = new List<CharacterBodyAssetBinding>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CharacterBodyAssetBinding
            {
                Id = reader.GetString(0),
                BodyProfileVersionId = reader.GetString(1),
                SceneAssetId = reader.GetString(2),
                SemanticRole = reader.GetString(3),
                Ordinal = reader.GetInt32(4),
                CropFactsJson = reader.GetString(5),
                AngleFactsJson = reader.GetString(6),
                BodyCoverageJson = reader.GetString(7),
                CreatedUtc = ParseUtc(reader.GetString(8), "body asset binding", reader.GetString(0))
            });
        }
        return results;
    }

    private static async Task<IReadOnlyList<CharacterWardrobeAssetBinding>> ListWardrobeBindingsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string versionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, WardrobeLookVersionId, SceneAssetId, SemanticRole, Ordinal,
                   GarmentFactsJson, ColorFactsJson, BodyCoverageJson, CreatedUtc
            FROM CharacterWardrobeAssetBindings WHERE WardrobeLookVersionId = $id ORDER BY Ordinal;
            """;
        command.Parameters.AddWithValue("$id", versionId);
        var results = new List<CharacterWardrobeAssetBinding>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CharacterWardrobeAssetBinding
            {
                Id = reader.GetString(0),
                WardrobeLookVersionId = reader.GetString(1),
                SceneAssetId = reader.GetString(2),
                SemanticRole = reader.GetString(3),
                Ordinal = reader.GetInt32(4),
                GarmentFactsJson = reader.GetString(5),
                ColorFactsJson = reader.GetString(6),
                BodyCoverageJson = reader.GetString(7),
                CreatedUtc = ParseUtc(reader.GetString(8), "wardrobe asset binding", reader.GetString(0))
            });
        }
        return results;
    }

    private static async Task InsertBodyProfileAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CharacterBodyProfileVersion version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CharacterBodyProfileVersions
                (Id, CharacterProfileId, Version, Status, DescriptorSnapshotJson, SupersedesId, CreatedUtc, ApprovedUtc)
            VALUES ($id, $owner, $version, $status, $descriptor, $supersedes, $createdUtc, $approvedUtc);
            """;
        AddVersionParameters(command, version.Id, version.CharacterProfileId, version.Version, version.Status,
            version.DescriptorSnapshotJson, version.SupersedesId, version.CreatedUtc, version.ApprovedUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertWardrobeLookAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CharacterWardrobeLookVersion version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CharacterWardrobeLookVersions
                (Id, CharacterProfileId, Version, Status, DescriptorSnapshotJson, CoverageFactsJson, SupersedesId, CreatedUtc, ApprovedUtc)
            VALUES ($id, $owner, $version, $status, $descriptor, $coverage, $supersedes, $createdUtc, $approvedUtc);
            """;
        AddVersionParameters(command, version.Id, version.CharacterProfileId, version.Version, version.Status,
            version.DescriptorSnapshotJson, version.SupersedesId, version.CreatedUtc, version.ApprovedUtc);
        command.Parameters.AddWithValue("$coverage", version.CoverageFactsJson.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddVersionParameters(
        SqliteCommand command,
        string id,
        string owner,
        int version,
        CharacterAppearanceVersionStatus status,
        string descriptor,
        string? supersedesId,
        DateTime createdUtc,
        DateTime? approvedUtc)
    {
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$owner", owner.Trim());
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$descriptor", descriptor.Trim());
        command.Parameters.AddWithValue("$supersedes", (object?)supersedesId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", createdUtc.ToString("O"));
        command.Parameters.AddWithValue("$approvedUtc", approvedUtc?.ToString("O") ?? (object)DBNull.Value);
    }

    private static async Task InsertBodyBindingAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CharacterBodyAssetBinding binding,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CharacterBodyAssetBindings
                (Id, BodyProfileVersionId, SceneAssetId, SemanticRole, Ordinal,
                 CropFactsJson, AngleFactsJson, BodyCoverageJson, CreatedUtc)
            VALUES ($id, $versionId, $assetId, $role, $ordinal, $crop, $angle, $coverage, $createdUtc);
            """;
        AddBindingParameters(command, binding.Id, binding.BodyProfileVersionId, binding.SceneAssetId,
            binding.SemanticRole, binding.Ordinal, binding.CreatedUtc);
        command.Parameters.AddWithValue("$crop", binding.CropFactsJson.Trim());
        command.Parameters.AddWithValue("$angle", binding.AngleFactsJson.Trim());
        command.Parameters.AddWithValue("$coverage", binding.BodyCoverageJson.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertWardrobeBindingAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CharacterWardrobeAssetBinding binding,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CharacterWardrobeAssetBindings
                (Id, WardrobeLookVersionId, SceneAssetId, SemanticRole, Ordinal,
                 GarmentFactsJson, ColorFactsJson, BodyCoverageJson, CreatedUtc)
            VALUES ($id, $versionId, $assetId, $role, $ordinal, $garment, $color, $coverage, $createdUtc);
            """;
        AddBindingParameters(command, binding.Id, binding.WardrobeLookVersionId, binding.SceneAssetId,
            binding.SemanticRole, binding.Ordinal, binding.CreatedUtc);
        command.Parameters.AddWithValue("$garment", binding.GarmentFactsJson.Trim());
        command.Parameters.AddWithValue("$color", binding.ColorFactsJson.Trim());
        command.Parameters.AddWithValue("$coverage", binding.BodyCoverageJson.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddBindingParameters(
        SqliteCommand command,
        string id,
        string versionId,
        string assetId,
        string role,
        int ordinal,
        DateTime createdUtc)
    {
        command.Parameters.AddWithValue("$id", id.Trim());
        command.Parameters.AddWithValue("$versionId", versionId.Trim());
        command.Parameters.AddWithValue("$assetId", assetId.Trim());
        command.Parameters.AddWithValue("$role", role.Trim());
        command.Parameters.AddWithValue("$ordinal", ordinal);
        command.Parameters.AddWithValue("$createdUtc", createdUtc.ToString("O"));
    }

    private static async Task<CharacterBodyProfileVersion> RequireBodyDraftAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id,
        CancellationToken cancellationToken)
    {
        var version = await GetBodyProfileAsync(connection, transaction, id.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Body profile version '{id}' was not found.");
        if (version.Status != CharacterAppearanceVersionStatus.Draft)
            throw new InvalidOperationException($"Body profile version '{id}' is {version.Status}; only drafts can be modified.");
        return version;
    }

    private static async Task<CharacterWardrobeLookVersion> RequireWardrobeDraftAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id,
        CancellationToken cancellationToken)
    {
        var version = await GetWardrobeLookAsync(connection, transaction, id.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe look version '{id}' was not found.");
        if (version.Status != CharacterAppearanceVersionStatus.Draft)
            throw new InvalidOperationException($"Wardrobe look version '{id}' is {version.Status}; only drafts can be modified.");
        return version;
    }

    private static async Task RequireSceneAssetAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string assetId,
        SceneAssetType expectedType,
        CancellationToken cancellationToken)
    {
        Require(assetId, "Scene asset id");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Type FROM SceneAssets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", assetId.Trim());
        var type = await command.ExecuteScalarAsync(cancellationToken);
        if (type is null or DBNull)
            throw new InvalidOperationException($"Scene asset '{assetId}' was not found or has no explicit type.");
        if (!string.Equals(Convert.ToString(type, CultureInfo.InvariantCulture), expectedType.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException($"Scene asset '{assetId}' must have type {expectedType}.");
    }

    private static async Task RequireApprovedSceneAssetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string assetId,
        SceneAssetType expectedType,
        SceneAssetApprovedUseScope requiredUseScope,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Type, Status, FileRelativePath, ByteLength, Sha256, SourceProvenanceJson,
                   ProductionApprovalStatus, ConsentState, LicenseState, LicenseLabel,
                   ApprovedUseScope, ContentPolicyKey, CompatibilityMetadataJson
            FROM SceneAssets WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", assetId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Scene asset '{assetId}' was not found.");
        if (!string.Equals(reader.IsDBNull(0) ? null : reader.GetString(0), expectedType.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException($"Scene asset '{assetId}' must have type {expectedType}.");
        if (!string.Equals(reader.GetString(1), SceneAssetStatus.Complete.ToString(), StringComparison.Ordinal)
            || reader.IsDBNull(2)
            || reader.GetInt64(3) <= 0
            || !IsSha256(reader.GetString(4)))
        {
            throw new InvalidOperationException($"Scene asset '{assetId}' is not a complete immutable content asset.");
        }
        if (reader.IsDBNull(5) || string.IsNullOrWhiteSpace(reader.GetString(5)))
            throw new InvalidOperationException($"Scene asset '{assetId}' requires provenance.");
        if (reader.IsDBNull(6) || !string.Equals(reader.GetString(6), SceneAssetProductionApprovalStatus.Approved.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException($"Scene asset '{assetId}' is not approved for production.");
        if (reader.IsDBNull(7) || string.Equals(reader.GetString(7), SceneAssetConsentState.Unknown.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException($"Scene asset '{assetId}' requires confirmed or not-applicable consent.");
        if (reader.IsDBNull(8) || string.Equals(reader.GetString(8), SceneAssetLicenseState.Unknown.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException($"Scene asset '{assetId}' requires a confirmed or not-applicable license state.");
        if (reader.IsDBNull(9) || string.IsNullOrWhiteSpace(reader.GetString(9)))
            throw new InvalidOperationException($"Scene asset '{assetId}' requires a license label.");
        if (reader.IsDBNull(10) || (((SceneAssetApprovedUseScope)reader.GetInt32(10)) & requiredUseScope) == 0)
            throw new InvalidOperationException($"Scene asset '{assetId}' is not approved for {requiredUseScope} use.");
        if (reader.IsDBNull(11) || string.IsNullOrWhiteSpace(reader.GetString(11)))
            throw new InvalidOperationException($"Scene asset '{assetId}' requires a content policy key.");
        if (reader.IsDBNull(12) || string.IsNullOrWhiteSpace(reader.GetString(12)))
            throw new InvalidOperationException($"Scene asset '{assetId}' requires compatibility metadata.");
    }

    private static async Task<int> GetNextVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string characterProfileId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COALESCE(MAX(Version), 0) + 1 FROM {table} WHERE CharacterProfileId = $owner;";
        command.Parameters.AddWithValue("$owner", characterProfileId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task RetireVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {table} SET Status = 'Superseded' WHERE Id = $id AND Status = 'Approved';";
        command.Parameters.AddWithValue("$id", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Version '{id}' changed before supersession completed.");
    }

    private static async Task RestoreSupersededParentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string? supersedesId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(supersedesId)) return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {table} SET Status = 'Approved' WHERE Id = $id AND Status = 'Superseded';";
        command.Parameters.AddWithValue("$id", supersedesId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string bindingTable,
        string ownerColumn,
        string versionTable,
        string id,
        CancellationToken cancellationToken)
    {
        await using var deleteBindings = connection.CreateCommand();
        deleteBindings.Transaction = transaction;
        deleteBindings.CommandText = $"DELETE FROM {bindingTable} WHERE {ownerColumn} = $id;";
        deleteBindings.Parameters.AddWithValue("$id", id);
        await deleteBindings.ExecuteNonQueryAsync(cancellationToken);
        await using var deleteVersion = connection.CreateCommand();
        deleteVersion.Transaction = transaction;
        deleteVersion.CommandText = $"DELETE FROM {versionTable} WHERE Id = $id AND Status = 'Draft';";
        deleteVersion.Parameters.AddWithValue("$id", id);
        if (await deleteVersion.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Draft version '{id}' changed before deletion completed.");
    }

    private static void ValidateBodyProfileDraft(CharacterBodyProfileVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        ValidateVersion(version.Id, version.CharacterProfileId, version.Version, version.Status,
            version.DescriptorSnapshotJson, version.ApprovedUtc, "Body profile");
    }

    private static void ValidateWardrobeLookDraft(CharacterWardrobeLookVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        ValidateVersion(version.Id, version.CharacterProfileId, version.Version, version.Status,
            version.DescriptorSnapshotJson, version.ApprovedUtc, "Wardrobe look");
        RequireJson(version.CoverageFactsJson, "Wardrobe coverage facts");
    }

    private static void ValidateVersion(
        string id,
        string owner,
        int version,
        CharacterAppearanceVersionStatus status,
        string descriptor,
        DateTime? approvedUtc,
        string label)
    {
        Require(id, $"{label} version id");
        Require(owner, "Character profile id");
        if (version <= 0) throw new InvalidOperationException($"{label} version must be positive.");
        if (status != CharacterAppearanceVersionStatus.Draft || approvedUtc is not null)
            throw new InvalidOperationException($"A new {label.ToLowerInvariant()} version must be an unapproved draft.");
        RequireJson(descriptor, $"{label} descriptor snapshot");
    }

    private static void ValidateBodyBinding(CharacterBodyAssetBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ValidateBinding(binding.Id, binding.BodyProfileVersionId, binding.SceneAssetId,
            binding.SemanticRole, binding.Ordinal, "Body asset binding");
        RequireJson(binding.CropFactsJson, "Body crop facts");
        RequireJson(binding.AngleFactsJson, "Body angle facts");
        RequireJson(binding.BodyCoverageJson, "Body coverage facts");
    }

    private static void ValidateWardrobeBinding(CharacterWardrobeAssetBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ValidateBinding(binding.Id, binding.WardrobeLookVersionId, binding.SceneAssetId,
            binding.SemanticRole, binding.Ordinal, "Wardrobe asset binding");
        RequireJson(binding.GarmentFactsJson, "Wardrobe garment facts");
        RequireJson(binding.ColorFactsJson, "Wardrobe color facts");
        RequireJson(binding.BodyCoverageJson, "Wardrobe body coverage facts");
    }

    private static void ValidateBinding(
        string id, string versionId, string assetId, string role, int ordinal, string label)
    {
        Require(id, $"{label} id");
        Require(versionId, $"{label} version id");
        Require(assetId, $"{label} scene asset id");
        Require(role, $"{label} semantic role");
        if (ordinal < 0) throw new InvalidOperationException($"{label} ordinal cannot be negative.");
    }

    private static void RequireJson(string value, string label)
    {
        Require(value, label);
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{label} must be valid JSON.", exception);
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

    private static TEnum ParseEnum<TEnum>(string value, string label, string id) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new InvalidOperationException($"Invalid {typeof(TEnum).Name} value '{value}' for {label} '{id}'.");
    }

    private static DateTime ParseUtc(string value, string label, string id)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;
        throw new InvalidOperationException($"Invalid UTC value '{value}' for {label} '{id}'.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }
}