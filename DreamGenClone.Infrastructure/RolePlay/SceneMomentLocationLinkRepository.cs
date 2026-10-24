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
        string pov,
        CancellationToken cancellationToken = default)
    {
        Require(momentId, "Moment id");
        Require(pov, "POV");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // POV is matched case-insensitively, exactly as the unique index that enforces one row per (moment, POV) is
        // built. A legacy row (Pov NULL, written before the binding became per-POV) can never satisfy this predicate,
        // so it is inert history rather than a value that stands in for a POV the operator never chose.
        command.CommandText = """
            SELECT MomentId, Pov, LocationAssetId, ScenarioLocationId, Origin, BackdropJson, CreatedUtc, UpdatedUtc
            FROM SceneMomentLocationLinks
            WHERE MomentId = $momentId AND Pov = $pov COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$momentId", momentId.Trim());
        command.Parameters.AddWithValue("$pov", pov.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task UpsertAsync(
        SceneMomentLocationLink link,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        Require(link.MomentId, "Moment id");
        Require(link.Pov, "POV");
        Require(link.LocationAssetId, "Location asset id");
        Require(link.Origin, "Location link origin");
        _ = FormatUtc(link.CreatedUtc);
        _ = FormatUtc(link.UpdatedUtc);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneMomentLocationLinks
                (MomentId, Pov, LocationAssetId, ScenarioLocationId, Origin, BackdropJson, CreatedUtc, UpdatedUtc)
            VALUES
                ($momentId, $pov, $locationAssetId, $scenarioLocationId, $origin, $backdropJson, $createdUtc, $updatedUtc)
            ON CONFLICT(MomentId, Pov) DO UPDATE SET
                LocationAssetId = excluded.LocationAssetId,
                ScenarioLocationId = excluded.ScenarioLocationId,
                Origin = excluded.Origin,
                BackdropJson = excluded.BackdropJson,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$momentId", link.MomentId.Trim());
        command.Parameters.AddWithValue("$pov", link.Pov.Trim());
        command.Parameters.AddWithValue("$locationAssetId", link.LocationAssetId.Trim());
        command.Parameters.AddWithValue("$scenarioLocationId", (object?)link.ScenarioLocationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$origin", link.Origin.Trim());
        command.Parameters.AddWithValue("$backdropJson", (object?)link.BackdropJson ?? DBNull.Value);
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
        await MigrateToPerPovKeyingAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Rebuilds the table when it still carries the pre-per-POV shape (<c>MomentId TEXT PRIMARY KEY</c>, no <c>Pov</c>
    /// column), which cannot hold a second row for the same moment. The rebuild is guarded on the missing column and is
    /// therefore idempotent, and it COPIES the existing rows rather than dropping them: a pre-per-POV row records a
    /// container for a moment with no POV, so it is carried across with a NULL POV and never matches a POV lookup —
    /// deleting the operator's own history to save a column was not worth the certainty it destroys.
    /// </summary>
    private static async Task MigrateToPerPovKeyingAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SceneMomentLocationLinks';";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                return;
            }
        }

        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('SceneMomentLocationLinks') WHERE name = 'Pov';";
            if (Convert.ToInt32(await probe.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                return;
            }
        }

        await using var migrate = connection.CreateCommand();
        migrate.CommandText = """
            CREATE TABLE SceneMomentLocationLinks_PerPov (
                MomentId TEXT NOT NULL CHECK (length(trim(MomentId)) > 0),
                Pov TEXT NULL COLLATE NOCASE CHECK (Pov IS NULL OR length(trim(Pov)) > 0),
                LocationAssetId TEXT NOT NULL CHECK (length(trim(LocationAssetId)) > 0),
                ScenarioLocationId TEXT NULL,
                Origin TEXT NOT NULL CHECK (length(trim(Origin)) > 0),
                BackdropJson TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            INSERT INTO SceneMomentLocationLinks_PerPov
                (MomentId, Pov, LocationAssetId, ScenarioLocationId, Origin, BackdropJson, CreatedUtc, UpdatedUtc)
                SELECT MomentId, NULL, LocationAssetId, ScenarioLocationId, Origin, NULL, CreatedUtc, UpdatedUtc
                FROM SceneMomentLocationLinks;
            DROP TABLE SceneMomentLocationLinks;
            ALTER TABLE SceneMomentLocationLinks_PerPov RENAME TO SceneMomentLocationLinks;
            """;
        await migrate.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <remarks>
    /// <c>Pov</c> is declared NULLABLE and carries the case-insensitive collation in the column itself — so the unique
    /// index below is the one <c>ON CONFLICT(MomentId, Pov)</c> targets, and so two POVs named with different casing
    /// cannot both take a row. NULL is allowed only because a pre-per-POV row has no POV to carry across; the repository
    /// refuses to WRITE a link without one, and SQLite treats NULLs as distinct, so legacy rows neither collide with
    /// each other nor match any POV's lookup.
    /// </remarks>
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SceneMomentLocationLinks (
            MomentId TEXT NOT NULL CHECK (length(trim(MomentId)) > 0),
            Pov TEXT NULL COLLATE NOCASE CHECK (Pov IS NULL OR length(trim(Pov)) > 0),
            LocationAssetId TEXT NOT NULL CHECK (length(trim(LocationAssetId)) > 0),
            ScenarioLocationId TEXT NULL,
            Origin TEXT NOT NULL CHECK (length(trim(Origin)) > 0),
            BackdropJson TEXT NULL,
            CreatedUtc TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_SceneMomentLocationLinks_MomentPov
            ON SceneMomentLocationLinks (MomentId, Pov);
        CREATE INDEX IF NOT EXISTS IX_SceneMomentLocationLinks_LocationAsset
            ON SceneMomentLocationLinks (LocationAssetId);
        """;

    private static SceneMomentLocationLink Read(SqliteDataReader reader) => new()
    {
        MomentId = reader.GetString(0),
        Pov = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
        LocationAssetId = reader.GetString(2),
        ScenarioLocationId = reader.IsDBNull(3) ? null : reader.GetString(3),
        Origin = reader.GetString(4),
        BackdropJson = reader.IsDBNull(5) ? null : reader.GetString(5),
        CreatedUtc = ParseUtc(reader.GetString(6)),
        UpdatedUtc = ParseUtc(reader.GetString(7))
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
