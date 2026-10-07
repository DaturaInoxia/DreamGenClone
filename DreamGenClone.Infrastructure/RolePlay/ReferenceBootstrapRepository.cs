using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ReferenceBootstrapRepository : IReferenceBootstrapRepository
{
    private readonly string _connectionString;

    public ReferenceBootstrapRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<ReferenceBootstrapBatch?> GetBatchAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Batch id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, CharacterProfileId, TargetAssetType, Description, FrozenTextBlock, RequestedCandidateCount, Status, CreatedUtc, UpdatedUtc FROM ReferenceBootstrapBatches WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBatch(reader) : null;
    }

    public async Task<IReadOnlyList<ReferenceBootstrapBatch>> ListBatchesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, CharacterProfileId, TargetAssetType, Description, FrozenTextBlock, RequestedCandidateCount, Status, CreatedUtc, UpdatedUtc FROM ReferenceBootstrapBatches ORDER BY CreatedUtc DESC, Id DESC;";
        return await ReadBatchesAsync(command, cancellationToken);
    }

    public async Task UpsertBatchAsync(ReferenceBootstrapBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Require(batch.Id, "Batch id");
        Require(batch.Description, "Batch description");
        if (batch.RequestedCandidateCount <= 0)
            throw new InvalidOperationException("Requested candidate count must be greater than zero.");
        ValidateBatchTarget(batch);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ReferenceBootstrapBatches
                (Id, CharacterProfileId, TargetAssetType, Description, FrozenTextBlock, RequestedCandidateCount, Status, CreatedUtc, UpdatedUtc)
            VALUES
                ($id, $characterProfileId, $targetAssetType, $description, $frozenTextBlock, $requestedCandidateCount, $status, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                CharacterProfileId = excluded.CharacterProfileId,
                TargetAssetType = excluded.TargetAssetType,
                Description = excluded.Description,
                FrozenTextBlock = excluded.FrozenTextBlock,
                RequestedCandidateCount = excluded.RequestedCandidateCount,
                Status = excluded.Status,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", batch.Id.Trim());
        command.Parameters.AddWithValue("$characterProfileId", (object?)batch.CharacterProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$targetAssetType", (object?)batch.TargetAssetType?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", batch.Description.Trim());
        command.Parameters.AddWithValue("$frozenTextBlock", (object?)batch.FrozenTextBlock?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$requestedCandidateCount", batch.RequestedCandidateCount);
        command.Parameters.AddWithValue("$status", batch.Status.ToString());
        command.Parameters.AddWithValue("$createdUtc", batch.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", batch.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteBatchAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Batch id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ReferenceBootstrapBatches WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            CREATE TABLE IF NOT EXISTS ReferenceBootstrapBatches (
                Id TEXT PRIMARY KEY,
                CharacterProfileId TEXT NULL,
                TargetAssetType TEXT NULL,
                LocationProfileId TEXT NULL,
                Description TEXT NOT NULL,
                FrozenTextBlock TEXT NULL,
                RequestedCandidateCount INTEGER NOT NULL,
                Status TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ReferenceBootstrapLocationProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                Status TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ReferenceBootstrapLocationReferences (
                Id TEXT PRIMARY KEY,
                ProfileId TEXT NOT NULL,
                OrderedIndex INTEGER NOT NULL,
                AssetId TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ReferenceBootstrapLocationReferences_ProfileId
                ON ReferenceBootstrapLocationReferences (ProfileId, OrderedIndex);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var migration = connection.CreateCommand();
        migration.CommandText = "ALTER TABLE ReferenceBootstrapBatches ADD COLUMN FrozenTextBlock TEXT NULL;";
        try
        {
            await migration.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static async Task<List<ReferenceBootstrapBatch>> ReadBatchesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var batches = new List<ReferenceBootstrapBatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) batches.Add(ReadBatch(reader));
        return batches;
    }

    private static ReferenceBootstrapBatch ReadBatch(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        CharacterProfileId = reader.IsDBNull(1) ? null : reader.GetString(1),
        TargetAssetType = reader.IsDBNull(2) ? null : ParseEnum<SceneAssetType>(reader.GetString(2), reader.GetString(0)),
        Description = reader.GetString(3),
        FrozenTextBlock = reader.IsDBNull(4) ? null : reader.GetString(4),
        RequestedCandidateCount = reader.GetInt32(5),
        Status = ParseEnum<ReferenceBootstrapBatchStatus>(reader.GetString(6), reader.GetString(0)),
        CreatedUtc = ParseUtc(reader.GetString(7), reader.GetString(0)),
        UpdatedUtc = ParseUtc(reader.GetString(8), reader.GetString(0))
    };

    private static void ValidateBatchTarget(ReferenceBootstrapBatch batch)
    {
        if (string.IsNullOrWhiteSpace(batch.CharacterProfileId) || batch.TargetAssetType is null)
            throw new InvalidOperationException("A batch must target a character profile and an asset type.");
    }

    private static TEnum ParseEnum<TEnum>(string value, string id) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid {typeof(TEnum).Name} value '{value}' for Reference Bootstrap record '{id}'.");

    private static DateTime ParseUtc(string value, string id) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid UTC value '{value}' for Reference Bootstrap record '{id}'.");

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }
}