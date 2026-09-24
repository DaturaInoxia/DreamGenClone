using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for the character body card (B-122 Phase 0). One row per character, created lazily by
/// the first save; schema creation is idempotent and carries the same ALTER-if-missing guard as its
/// neighbours, so an existing dev DB gains the table without a migration step.
/// </summary>
public sealed class CharacterBodyCardRepository : ICharacterBodyCardRepository
{
    private const int CreateExpectedVersion = 0;

    /// <summary>Matches how the rest of the app serializes structured payloads (TemplateService, the identity stores).</summary>
    private static readonly JsonSerializerOptions AxesJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;

    public CharacterBodyCardRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
    }

    public async Task<CharacterBodyCard?> GetAsync(
        string characterProfileId, CancellationToken cancellationToken = default)
    {
        Require(characterProfileId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM CharacterBodyCards WHERE CharacterProfileId = $characterProfileId;
            """;
        command.Parameters.AddWithValue("$characterProfileId", characterProfileId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCard(reader) : null;
    }

    public async Task<CharacterBodyCard> SaveAsync(
        CharacterBodyCard card, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        Require(card.CharacterTemplateId, "Character profile id");
        if (expectedVersion < 0)
        {
            throw new InvalidOperationException(
                $"The expected body card version cannot be negative, but was {expectedVersion}.");
        }

        var now = DateTime.UtcNow;
        var characterProfileId = card.CharacterTemplateId.Trim();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (expectedVersion == CreateExpectedVersion)
        {
            // Create. A second writer racing this insert gets a PK violation, which is the honest answer.
            command.CommandText = $"""
                INSERT INTO CharacterBodyCards ({FieldColumns}, Version, UpdatedUtc)
                VALUES ($characterProfileId, $bodyShape, $heightBuild, $skin, $bodyHair, $tattoos, $scarsMarks,
                        $pubicHair, $bodyAxes, 1, $updatedUtc);
                """;
        }
        else
        {
            // Update only when the stored version still matches what the editor read: no silent overwrite.
            command.CommandText = $"""
                UPDATE CharacterBodyCards
                SET BodyShape = $bodyShape,
                    HeightBuild = $heightBuild,
                    Skin = $skin,
                    BodyHair = $bodyHair,
                    Tattoos = $tattoos,
                    ScarsMarks = $scarsMarks,
                    PubicHair = $pubicHair,
                    BodyAxesJson = $bodyAxes,
                    Version = Version + 1,
                    UpdatedUtc = $updatedUtc
                WHERE CharacterProfileId = $characterProfileId AND Version = $expectedVersion;
                """;
            command.Parameters.AddWithValue("$expectedVersion", expectedVersion);
        }

        command.Parameters.AddWithValue("$characterProfileId", characterProfileId);
        command.Parameters.AddWithValue("$bodyShape", card.BodyShape?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$heightBuild", card.HeightBuild?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$skin", card.Skin?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$bodyHair", card.BodyHair?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$tattoos", card.Tattoos?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$scarsMarks", card.ScarsMarks?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$pubicHair", card.PubicHair?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$bodyAxes", SerializeAxes(card.Axes));
        command.Parameters.AddWithValue("$updatedUtc", now.ToString("O", CultureInfo.InvariantCulture));

        int affected;
        try
        {
            affected = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (expectedVersion == CreateExpectedVersion && ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                $"Character '{characterProfileId}' already has a body card. Re-read it and save with its "
                + "current version instead of creating a second one.", ex);
        }

        if (affected == 0)
        {
            var stored = await GetAsync(characterProfileId, cancellationToken);
            throw new InvalidOperationException(
                stored is null
                    ? $"Character '{characterProfileId}' has no body card, so version {expectedVersion} cannot be saved."
                    : $"The body card for character '{characterProfileId}' changed while it was being edited: "
                      + $"the stored version is {stored.Version}, but this edit expected {expectedVersion}. "
                      + "Re-read the card and re-apply the change.");
        }

        return await GetAsync(characterProfileId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The body card for character '{characterProfileId}' could not be read back after saving.");
    }

    /// <summary>SELECT column list (and the order <see cref="ReadCard"/> reads).</summary>
    private const string Columns =
        "CharacterProfileId, BodyShape, HeightBuild, Skin, BodyHair, Tattoos, ScarsMarks, PubicHair, Version, UpdatedUtc, BodyAxesJson";

    /// <summary>The card's own fields, without the version/updated pair the store owns.</summary>
    private const string FieldColumns =
        "CharacterProfileId, BodyShape, HeightBuild, Skin, BodyHair, Tattoos, ScarsMarks, PubicHair, BodyAxesJson";

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
            CREATE TABLE IF NOT EXISTS CharacterBodyCards (
                CharacterProfileId TEXT PRIMARY KEY,
                BodyShape TEXT NOT NULL DEFAULT '',
                HeightBuild TEXT NOT NULL DEFAULT '',
                Skin TEXT NOT NULL DEFAULT '',
                BodyHair TEXT NOT NULL DEFAULT '',
                Tattoos TEXT NOT NULL DEFAULT '',
                ScarsMarks TEXT NOT NULL DEFAULT '',
                PubicHair TEXT NOT NULL DEFAULT '',
                BodyAxesJson TEXT NOT NULL DEFAULT '{}',
                Version INTEGER NOT NULL DEFAULT 1,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        var columns = await ListColumnsAsync(connection, cancellationToken);

        // The axes arrived after the table did: add the column in place so an existing dev DB gains it without a
        // migration step, the same guard the pubic-hair split below uses.
        if (!columns.Contains("BodyAxesJson"))
        {
            await using var addAxes = connection.CreateCommand();
            addAxes.CommandText = "ALTER TABLE CharacterBodyCards ADD COLUMN BodyAxesJson TEXT NOT NULL DEFAULT '{}';";
            await addAxes.ExecuteNonQueryAsync(cancellationToken);
        }

        // A card written before the body/pubic split kept its pubic-hair value in `Grooming`. Move it across once,
        // then drop the old column: leaving it behind would be a second, silently-diverging place for one fact.
        if (columns.Contains("Grooming"))
        {
            if (!columns.Contains("PubicHair"))
            {
                await using var addColumn = connection.CreateCommand();
                addColumn.CommandText = "ALTER TABLE CharacterBodyCards ADD COLUMN PubicHair TEXT NOT NULL DEFAULT '';";
                await addColumn.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var copy = connection.CreateCommand();
            copy.CommandText = "UPDATE CharacterBodyCards SET PubicHair = Grooming WHERE PubicHair = '' AND Grooming <> '';";
            await copy.ExecuteNonQueryAsync(cancellationToken);

            await using var dropColumn = connection.CreateCommand();
            dropColumn.CommandText = "ALTER TABLE CharacterBodyCards DROP COLUMN Grooming;";
            await dropColumn.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<HashSet<string>> ListColumnsAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('CharacterBodyCards');";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static CharacterBodyCard ReadCard(SqliteDataReader reader) => new()
    {
        CharacterTemplateId = reader.GetString(0),
        BodyShape = reader.GetString(1),
        HeightBuild = reader.GetString(2),
        Skin = reader.GetString(3),
        BodyHair = reader.GetString(4),
        Tattoos = reader.GetString(5),
        ScarsMarks = reader.GetString(6),
        PubicHair = reader.GetString(7),
        Version = reader.GetInt32(8),
        UpdatedUtc = ParseUtc(reader.GetString(9)),
        Axes = ParseAxes(reader.IsDBNull(10) ? null : reader.GetString(10))
    };

    /// <summary>Serializes the picks. The axes are never null on a card, so this always writes a document.</summary>
    private static string SerializeAxes(CharacterBodyAxes? axes)
        => JsonSerializer.Serialize(axes ?? new CharacterBodyAxes(), AxesJsonOptions);

    /// <summary>
    /// Reads the picks back. Malformed or unreadable JSON fails fast rather than silently becoming "no axes picked":
    /// a card whose stored picks cannot be read is a data fault to surface, not something to overwrite with empty.
    /// </summary>
    private static CharacterBodyAxes ParseAxes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CharacterBodyAxes();
        }

        try
        {
            return JsonSerializer.Deserialize<CharacterBodyAxes>(json, AxesJsonOptions)
                ?? new CharacterBodyAxes();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The stored body-axis picks are not valid JSON, so the card cannot be read: {ex.Message}", ex);
        }
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid UTC value '{value}'.");
}
