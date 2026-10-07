using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneMomentLocationLinkRepository : ISceneMomentLocationLinkRepository
{
    private readonly string _connectionString;

    public SceneMomentLocationLinkRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<SceneMomentLocationLink?> GetAsync(
        string momentId,
        CancellationToken cancellationToken = default)
    {
        Require(momentId, "Moment id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MomentId, LocationAssetId, ScenarioLocationId, Origin, CreatedUtc, UpdatedUtc
            FROM SceneMomentLocationLinks
            WHERE MomentId = $momentId;
            """;
        command.Parameters.AddWithValue("$momentId", momentId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task UpsertAsync(
        SceneMomentLocationLink link,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        Require(link.MomentId, "Moment id");
        Require(link.LocationAssetId, "Location asset id");
        Require(link.Origin, "Location link origin");
        _ = FormatUtc(link.CreatedUtc);
        _ = FormatUtc(link.UpdatedUtc);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneMomentLocationLinks
                (MomentId, LocationAssetId, ScenarioLocationId, Origin, CreatedUtc, UpdatedUtc)
            VALUES
                ($momentId, $locationAssetId, $scenarioLocationId, $origin, $createdUtc, $updatedUtc)
            ON CONFLICT(MomentId) DO UPDATE SET
                LocationAssetId = excluded.LocationAssetId,
                ScenarioLocationId = excluded.ScenarioLocationId,
                Origin = excluded.Origin,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$momentId", link.MomentId.Trim());
        command.Parameters.AddWithValue("$locationAssetId", link.LocationAssetId.Trim());
        command.Parameters.AddWithValue("$scenarioLocationId", (object?)link.ScenarioLocationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$origin", link.Origin.Trim());
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(link.CreatedUtc));
        command.Parameters.AddWithValue("$updatedUtc", FormatUtc(link.UpdatedUtc));
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
            CREATE TABLE IF NOT EXISTS SceneMomentLocationLinks (
                MomentId TEXT PRIMARY KEY,
                LocationAssetId TEXT NOT NULL CHECK (length(trim(LocationAssetId)) > 0),
                ScenarioLocationId TEXT NULL,
                Origin TEXT NOT NULL CHECK (length(trim(Origin)) > 0),
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneMomentLocationLinks_LocationAsset
                ON SceneMomentLocationLinks (LocationAssetId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SceneMomentLocationLink Read(SqliteDataReader reader) => new()
    {
        MomentId = reader.GetString(0),
        LocationAssetId = reader.GetString(1),
        ScenarioLocationId = reader.IsDBNull(2) ? null : reader.GetString(2),
        Origin = reader.GetString(3),
        CreatedUtc = ParseUtc(reader.GetString(4)),
        UpdatedUtc = ParseUtc(reader.GetString(5))
    };

    private static string FormatUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value.ToString("O", CultureInfo.InvariantCulture)
            : throw new InvalidOperationException("Persistence timestamps must be UTC.");

    private static DateTime ParseUtc(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
    }
}
