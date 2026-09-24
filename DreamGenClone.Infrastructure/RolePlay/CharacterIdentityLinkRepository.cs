using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for character-instance → character-template links (B-127). Its own table, created by the
/// same lazy idempotent schema call as its neighbours, so an existing dev DB gains it without a migration step.
/// Kept out of <c>SceneAssets</c> deliberately: the link is not a property of an asset — a scenario character can
/// be linked without any asset existing — and this table also carries who made the link and when.
/// </summary>
public sealed class CharacterIdentityLinkRepository : ICharacterIdentityLinkRepository
{
    private const string Columns = "OwnerInstanceId, CharacterTemplateId, LinkedBy, LinkedUtc";

    private readonly string _connectionString;

    public CharacterIdentityLinkRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
    }

    public async Task<string?> GetTemplateIdAsync(
        string ownerInstanceId, CancellationToken cancellationToken = default)
    {
        Require(ownerInstanceId, "Character instance id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CharacterTemplateId FROM CharacterIdentityLinks WHERE OwnerInstanceId = $instanceId;";
        command.Parameters.AddWithValue("$instanceId", ownerInstanceId.Trim());
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<IReadOnlyList<CharacterIdentityLink>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM CharacterIdentityLinks ORDER BY CharacterTemplateId, OwnerInstanceId;";
        var links = new List<CharacterIdentityLink>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            links.Add(ReadLink(reader));
        }

        return links;
    }

    public async Task<CharacterIdentityLink> SaveAsync(
        CharacterIdentityLink link, bool replaceExisting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        Require(link.OwnerInstanceId, "Character instance id");
        Require(link.CharacterTemplateId, "Character template id");

        var instanceId = link.OwnerInstanceId.Trim();
        var templateId = link.CharacterTemplateId.Trim();

        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetLinkAsync(connection, instanceId, cancellationToken);
        if (existing is not null
            && !string.Equals(existing.CharacterTemplateId, templateId, StringComparison.Ordinal)
            && !replaceExisting)
        {
            throw new InvalidOperationException(
                $"Character '{instanceId}' is already linked to template '{existing.CharacterTemplateId}', and this "
                + $"link names '{templateId}'. Re-pointing a character's identity is deliberate: confirm the "
                + "replacement explicitly.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityLinks (OwnerInstanceId, CharacterTemplateId, LinkedBy, LinkedUtc)
            VALUES ($instanceId, $templateId, $linkedBy, $linkedUtc)
            ON CONFLICT(OwnerInstanceId) DO UPDATE SET
                CharacterTemplateId = excluded.CharacterTemplateId,
                LinkedBy = excluded.LinkedBy,
                LinkedUtc = excluded.LinkedUtc;
            """;
        command.Parameters.AddWithValue("$instanceId", instanceId);
        command.Parameters.AddWithValue("$templateId", templateId);
        command.Parameters.AddWithValue("$linkedBy", (object?)link.LinkedBy?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$linkedUtc", (link.LinkedUtc == default ? DateTime.UtcNow : link.LinkedUtc).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetLinkAsync(connection, instanceId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The identity link for character '{instanceId}' could not be read back after saving.");
    }

    public async Task DeleteAsync(string ownerInstanceId, CancellationToken cancellationToken = default)
    {
        Require(ownerInstanceId, "Character instance id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CharacterIdentityLinks WHERE OwnerInstanceId = $instanceId;";
        command.Parameters.AddWithValue("$instanceId", ownerInstanceId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CharacterIdentityLink?> GetLinkAsync(
        SqliteConnection connection, string instanceId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM CharacterIdentityLinks WHERE OwnerInstanceId = $instanceId;";
        command.Parameters.AddWithValue("$instanceId", instanceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLink(reader) : null;
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
            CREATE TABLE IF NOT EXISTS CharacterIdentityLinks (
                OwnerInstanceId TEXT PRIMARY KEY,
                CharacterTemplateId TEXT NOT NULL,
                LinkedBy TEXT NULL,
                LinkedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityLinks_Template
                ON CharacterIdentityLinks (CharacterTemplateId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CharacterIdentityLink ReadLink(SqliteDataReader reader) => new()
    {
        OwnerInstanceId = reader.GetString(0),
        CharacterTemplateId = reader.GetString(1),
        LinkedBy = reader.IsDBNull(2) ? null : reader.GetString(2),
        LinkedUtc = ParseUtc(reader.GetString(3))
    };

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid UTC value '{value}'.");
}
