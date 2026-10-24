using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for the per-surface image staging profiles (B-157).
///
/// <para>
/// Schema creation and seeding are idempotent, and the seed is reached from the SAME private ensure that
/// <c>OpenAsync</c> calls, so any read seeds — a store whose data must exist for the store to be usable cannot rely
/// on a startup path someone forgets to call.
/// </para>
///
/// <para>
/// Seeding never overwrites an existing row for a surface: a staging profile is editable configuration, and a
/// hand-edited row must survive the next open.
/// </para>
/// </summary>
public sealed class ImageStagingProfileRepository : IImageStagingProfileRepository
{
    private const string SelectColumns = """
        Id, DisplayName, Surface, PlacementLadderJson, ActiveDepth, ObserverDepth,
        DefaultCameraAngle, DefaultShotSize, CameraKeywordMapJson, Version, UpdatedUtc, OmniscientShotSize,
        DefaultCameraHeight
        """;

    private readonly string _connectionString;

    public ImageStagingProfileRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
    }

    public async Task<ImageStagingProfile?> FindBySurfaceAsync(ImageStagingSurface surface, CancellationToken cancellationToken = default)
    {
        if (surface == ImageStagingSurface.Unknown)
        {
            throw new InvalidOperationException(
                "A staging surface is required to resolve an image staging profile; 'Unknown' is not a surface.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM ImageStagingProfiles
            WHERE Surface = $surface;
            """;
        command.Parameters.AddWithValue("$surface", (int)surface);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfile(reader) : null;
    }

    public async Task<IReadOnlyList<ImageStagingProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM ImageStagingProfiles
            ORDER BY Surface;
            """;
        var results = new List<ImageStagingProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadProfile(reader));
        }

        return results;
    }

    public async Task UpsertAsync(ImageStagingProfile profile, CancellationToken cancellationToken = default)
    {
        ImageStagingProfileValidation.Validate(profile);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImageStagingProfiles (
                Id, DisplayName, Surface, PlacementLadderJson, ActiveDepth, ObserverDepth,
                DefaultCameraAngle, DefaultShotSize, CameraKeywordMapJson, Version, UpdatedUtc, OmniscientShotSize,
                DefaultCameraHeight)
            VALUES (
                $id, $displayName, $surface, $ladder, $activeDepth, $observerDepth,
                $angle, $shotSize, $keywords, $version, $updatedUtc, $omniscientShotSize, $cameraHeight)
            ON CONFLICT(Surface) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                PlacementLadderJson = excluded.PlacementLadderJson,
                ActiveDepth = excluded.ActiveDepth,
                ObserverDepth = excluded.ObserverDepth,
                DefaultCameraAngle = excluded.DefaultCameraAngle,
                DefaultShotSize = excluded.DefaultShotSize,
                CameraKeywordMapJson = excluded.CameraKeywordMapJson,
                Version = excluded.Version,
                UpdatedUtc = excluded.UpdatedUtc,
                OmniscientShotSize = excluded.OmniscientShotSize,
                DefaultCameraHeight = excluded.DefaultCameraHeight;
            """;
        AddProfileParameters(command, profile);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddProfileParameters(SqliteCommand command, ImageStagingProfile profile)
    {
        command.Parameters.AddWithValue("$id", profile.Id.Trim());
        command.Parameters.AddWithValue("$displayName", profile.DisplayName.Trim());
        command.Parameters.AddWithValue("$surface", (int)profile.Surface);
        command.Parameters.AddWithValue("$ladder", profile.PlacementLadderJson);
        command.Parameters.AddWithValue("$activeDepth", (int)profile.ActiveDepth);
        command.Parameters.AddWithValue("$observerDepth", (int)profile.ObserverDepth);
        command.Parameters.AddWithValue("$angle", profile.DefaultCameraAngle.Trim());
        command.Parameters.AddWithValue("$shotSize", profile.DefaultShotSize.Trim());
        command.Parameters.AddWithValue("$omniscientShotSize", profile.OmniscientShotSize.Trim());
        command.Parameters.AddWithValue("$cameraHeight", profile.DefaultCameraHeight.Trim());
        command.Parameters.AddWithValue("$keywords", profile.CameraKeywordMapJson);
        command.Parameters.AddWithValue("$version", profile.Version);
        command.Parameters.AddWithValue("$updatedUtc", profile.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static ImageStagingProfile ReadProfile(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        DisplayName = reader.GetString(1),
        Surface = (ImageStagingSurface)reader.GetInt32(2),
        PlacementLadderJson = reader.GetString(3),
        ActiveDepth = (FrameDepth)reader.GetInt32(4),
        ObserverDepth = (FrameDepth)reader.GetInt32(5),
        DefaultCameraAngle = reader.GetString(6),
        DefaultShotSize = reader.GetString(7),
        CameraKeywordMapJson = reader.GetString(8),
        Version = reader.GetInt32(9),
        UpdatedUtc = DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        OmniscientShotSize = reader.GetString(11),
        DefaultCameraHeight = reader.FieldCount > 12 ? reader.GetString(12) : string.Empty
    };

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ImageStagingProfiles (
                    Id TEXT PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    Surface INTEGER NOT NULL,
                    PlacementLadderJson TEXT NOT NULL DEFAULT '[]',
                    ActiveDepth INTEGER NOT NULL,
                    ObserverDepth INTEGER NOT NULL,
                    DefaultCameraAngle TEXT NOT NULL DEFAULT '',
                    DefaultShotSize TEXT NOT NULL DEFAULT '',
                    CameraKeywordMapJson TEXT NOT NULL DEFAULT '[]',
                    Version INTEGER NOT NULL DEFAULT 1,
                    UpdatedUtc TEXT NOT NULL,
                    OmniscientShotSize TEXT NOT NULL DEFAULT '',
                    DefaultCameraHeight TEXT NOT NULL DEFAULT ''
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ImageStagingProfiles_Surface
                    ON ImageStagingProfiles (Surface);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // B-166 D7: the omniscient lean is persisted configuration. Additive migration for a database created before
        // the column existed; the seed below fills it for a row that predates the setting.
        await AddColumnIfMissingAsync(
            connection, "ImageStagingProfiles", "OmniscientShotSize", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        // Camera HEIGHT is persisted configuration too, added the same additive way so an existing database keeps its
        // values and the seed below fills only the blank rows.
        await AddColumnIfMissingAsync(
            connection, "ImageStagingProfiles", "DefaultCameraHeight", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await BackfillOmniscientShotSizeAsync(connection, cancellationToken);
        await BackfillDefaultCameraHeightAsync(connection, cancellationToken);
        await SeedAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Fills <see cref="ImageStagingProfile.DefaultCameraHeight"/> on a row created before the setting existed. It writes
    /// only when the value is EMPTY, so it can never revert a height the operator edited on the profile panel.
    /// </summary>
    private static async Task BackfillDefaultCameraHeightAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var profile in SeedProfiles())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ImageStagingProfiles
                SET DefaultCameraHeight = $value
                WHERE Surface = $surface AND TRIM(DefaultCameraHeight) = '';
                """;
            command.Parameters.AddWithValue("$value", profile.DefaultCameraHeight.Trim());
            command.Parameters.AddWithValue("$surface", (int)profile.Surface);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Fills <see cref="ImageStagingProfile.OmniscientShotSize"/> on a row created before the setting existed.
    /// It writes only when the value is EMPTY, so it can never revert a value the operator edited on /profiles.
    /// </summary>
    private static async Task BackfillOmniscientShotSizeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var profile in SeedProfiles())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ImageStagingProfiles
                SET OmniscientShotSize = $value
                WHERE Surface = $surface AND TRIM(OmniscientShotSize) = '';
                """;
            command.Parameters.AddWithValue("$value", profile.OmniscientShotSize.Trim());
            command.Parameters.AddWithValue("$surface", (int)profile.Surface);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        bool exists;
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
            exists = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists) return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var profile in SeedProfiles())
        {
            // A bad seed row is a bug in this file, not user data: refuse it loudly rather than writing a profile
            // that would make every render for its surface unusable.
            ImageStagingProfileValidation.Validate(profile);

            await using var command = connection.CreateCommand();
            // DO NOTHING, not DO UPDATE: an existing row may have been edited on /profiles, and a seed must never
            // revert configuration the operator changed.
            command.CommandText = """
                INSERT INTO ImageStagingProfiles (
                    Id, DisplayName, Surface, PlacementLadderJson, ActiveDepth, ObserverDepth,
                    DefaultCameraAngle, DefaultShotSize, CameraKeywordMapJson, Version, UpdatedUtc, OmniscientShotSize,
                    DefaultCameraHeight)
                VALUES (
                    $id, $displayName, $surface, $ladder, $activeDepth, $observerDepth,
                    $angle, $shotSize, $keywords, $version, $updatedUtc, $omniscientShotSize, $cameraHeight)
                ON CONFLICT(Surface) DO NOTHING;
                """;
            AddProfileParameters(command, profile);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Migration rows. These are SEEDS, not read-path fallbacks: nothing at render time substitutes a missing value,
    // and every render reads the row that matched its surface (or fails fast naming it).
    //
    // The ladder declares which positions a frame may use. Seating spreads the cast symmetrically over it — a single
    // subject takes the centre, a pair takes the opposite edges, three fill the frame — so the involvement rule the
    // operator configured (active in the foreground, observers behind) stays expressed in data rather than in code.
    // ---------------------------------------------------------------------------------------------------------
    private static IReadOnlyList<ImageStagingProfile> SeedProfiles() =>
    [
        new()
        {
            Id = "staging-composition",
            DisplayName = "Composition (scene render)",
            Surface = ImageStagingSurface.Composition,
            PlacementLadderJson = """["Center","Left","Right"]""",
            ActiveDepth = FrameDepth.Foreground,
            ObserverDepth = FrameDepth.Background,
            DefaultCameraAngle = "eye level",
            DefaultShotSize = "medium shot",
            // B-166 D7: the Omniscient POV shows every in-scene person in their place, so it is framed as the
            // establishing view of the moment rather than the composition's close default.
            OmniscientShotSize = "wide establishing shot",
            // The composition default is a level camera at the subject's eye height: the neutral, most predictable
            // starting point, and the one official Qwen perspective keyword among the five positions.
            DefaultCameraHeight = CameraHeights.Eye,
            CameraKeywordMapJson = """
                [
                  {"keyword":"wide","shotSize":"wide establishing shot"},
                  {"keyword":"tight","shotSize":"tightly framed shot"},
                  {"keyword":"close","shotSize":"close shot"},
                  {"keyword":"medium","shotSize":"medium shot"},
                  {"keyword":"low","angle":"low angle"},
                  {"keyword":"high","angle":"high angle"}
                ]
                """,
            Version = 1
        },
        new()
        {
            Id = "staging-asset-manager",
            DisplayName = "Asset Manager (bindings only)",
            Surface = ImageStagingSurface.AssetManager,
            // A binding-only render has no scene to describe, so its single subject takes the centre of the frame and
            // the remaining entries only matter if the operator binds more than one person to one asset.
            PlacementLadderJson = """["Center","Left","Right"]""",
            ActiveDepth = FrameDepth.Foreground,
            ObserverDepth = FrameDepth.Midground,
            DefaultCameraAngle = "eye level",
            DefaultShotSize = "medium portrait",
            // A binding-only render has no POV list, but the value is required so the profile is complete and the
            // /profiles editor never shows a blank. It is not applied on the AssetManager surface.
            OmniscientShotSize = "medium portrait",
            DefaultCameraHeight = CameraHeights.Eye,
            CameraKeywordMapJson = "[]",
            Version = 1
        }
    ];
}
