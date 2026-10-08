using System.Globalization;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.ModelManager;

/// <summary>
/// SQLite-backed scene-LoRA catalog (B-137 §4). The schema is created idempotently on every open, so the table
/// exists before the first read without a migration step and without an ordering requirement on startup.
///
/// <para>
/// Enum values are stored as their NAMES, matching the sibling LoRA stores (a stored catalog row is readable
/// months later without the enum's integer values in hand). A value the enum no longer knows fails fast rather
/// than being read as <c>Unknown</c>: a catalog row whose family cannot be resolved would otherwise be offered to
/// every model.
/// </para>
/// </summary>
public sealed class SceneLoraRepository : ISceneLoraRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SceneLoras (
            Id TEXT PRIMARY KEY,
            FileName TEXT NOT NULL,
            DisplayName TEXT NOT NULL,
            SceneImageModelFamily TEXT NOT NULL,
            Category TEXT NOT NULL,
            DefaultStrength REAL NOT NULL CHECK (DefaultStrength > 0),
            IsEnabled INTEGER NOT NULL CHECK (IsEnabled IN (0, 1)),
            TriggerToken TEXT NULL,
            Notes TEXT NULL,
            CreatedUtc TEXT NOT NULL,
            UNIQUE (SceneImageModelFamily, FileName)
        );
        CREATE INDEX IF NOT EXISTS IX_SceneLoras_Family
            ON SceneLoras (SceneImageModelFamily, IsEnabled, Category);
        """;

    /// <summary>
    /// Additive column for catalogs created before triggers existed. The table is created idempotently, so an
    /// existing database never re-runs the CREATE above and needs the column added explicitly.
    /// </summary>
    private const string AddTriggerTokenColumnSql =
        "ALTER TABLE SceneLoras ADD COLUMN TriggerToken TEXT NULL";

    private readonly string _connectionString;

    public SceneLoraRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var _ = await OpenAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SceneLora>> ListAsync(
        SceneImageModelFamily family, CancellationToken cancellationToken = default)
    {
        if (family == SceneImageModelFamily.Unknown)
        {
            throw new ArgumentException(
                "A scene-LoRA list needs a model family; 'Unknown' has no catalog rows by construction.",
                nameof(family));
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, FileName, DisplayName, SceneImageModelFamily, Category, DefaultStrength, IsEnabled,
                   TriggerToken, Notes
            FROM SceneLoras
            WHERE SceneImageModelFamily = $family AND IsEnabled = 1
            ORDER BY Category, DisplayName COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$family", family.ToString());

        var rows = new List<SceneLora>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async Task<SceneLora?> GetByFileNameAsync(
        string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A scene-LoRA lookup needs a file name.", nameof(fileName));
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, FileName, DisplayName, SceneImageModelFamily, Category, DefaultStrength, IsEnabled,
                   TriggerToken, Notes
            FROM SceneLoras
            WHERE FileName = $fileName;
            """;
        command.Parameters.AddWithValue("$fileName", fileName.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static SceneLora Read(SqliteDataReader reader)
    {
        var familyText = reader.GetString(3);
        if (!Enum.TryParse<SceneImageModelFamily>(familyText, ignoreCase: false, out var family)
            || !Enum.IsDefined(family))
        {
            throw new InvalidOperationException(
                $"Scene-LoRA row '{reader.GetString(0)}' declares family '{familyText}', which is not a known "
                + "scene-image model family. Fix the row in the SceneLoras table (dbq) rather than letting the "
                + "catalog offer a LoRA to a model it was not trained for.");
        }

        var categoryText = reader.GetString(4);
        if (!Enum.TryParse<SceneLoraCategory>(categoryText, ignoreCase: false, out var category)
            || !Enum.IsDefined(category))
        {
            throw new InvalidOperationException(
                $"Scene-LoRA row '{reader.GetString(0)}' declares category '{categoryText}', which is not a known "
                + "scene-LoRA category. Fix the row in the SceneLoras table (dbq).");
        }

        var strength = reader.GetDouble(5);
        if (!(strength > 0))
        {
            throw new InvalidOperationException(
                $"Scene-LoRA row '{reader.GetString(0)}' has DefaultStrength {strength.ToString(CultureInfo.InvariantCulture)}, "
                + "which is not positive. A LoRA applied at a strength nobody chose is a different LoRA: set the "
                + "catalog row's DefaultStrength rather than letting the render guess one.");
        }

        return new SceneLora
        {
            Id = reader.GetString(0),
            FileName = reader.GetString(1),
            DisplayName = reader.GetString(2),
            SceneImageModelFamily = family,
            Category = category,
            DefaultStrength = strength,
            IsEnabled = reader.GetInt32(6) == 1,
            TriggerToken = reader.IsDBNull(7) ? null : reader.GetString(7),
            Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON; " + SchemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureTriggerTokenColumnAsync(connection, cancellationToken);
        return connection;
    }

    /// <summary>
    /// Adds the trigger column to a catalog created before it existed. SQLite has no
    /// <c>ADD COLUMN IF NOT EXISTS</c>, so the table is inspected first; the column is added only when absent,
    /// which keeps every open idempotent.
    /// </summary>
    private static async Task EnsureTriggerTokenColumnAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(SceneLoras);";
            await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "TriggerToken", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = AddTriggerTokenColumnSql;
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
}
