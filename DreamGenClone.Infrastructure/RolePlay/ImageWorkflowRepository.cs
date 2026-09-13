using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for the editable workflow prompt templates and reference-workflow settings.
/// Schema creation and seeding are idempotent; the seed rows are migration data (configuration in the
/// database), never runtime fallback constants.
/// </summary>
public sealed class ImageWorkflowRepository : IImageWorkflowRepository
{
    private readonly string _connectionString;

    public ImageWorkflowRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
    }

    public async Task<ImageWorkflowPromptTemplate?> GetTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Template id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Key, Scope, CharacterProfileId, WorkflowStep, Body, SeedBody, UpdatedUtc
            FROM ImageWorkflowPromptTemplates WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTemplate(reader) : null;
    }

    public async Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Key, Scope, CharacterProfileId, WorkflowStep, Body, SeedBody, UpdatedUtc
            FROM ImageWorkflowPromptTemplates ORDER BY Key, Scope, CharacterProfileId;
            """;
        var results = new List<ImageWorkflowPromptTemplate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadTemplate(reader));
        }

        return results;
    }

    public async Task UpsertTemplateAsync(ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default)
    {
        ValidateTemplate(template);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImageWorkflowPromptTemplates (Id, Key, Scope, CharacterProfileId, WorkflowStep, Body, SeedBody, UpdatedUtc)
            VALUES ($id, $key, $scope, $characterProfileId, $workflowStep, $body, $seedBody, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Key = excluded.Key,
                Scope = excluded.Scope,
                CharacterProfileId = excluded.CharacterProfileId,
                WorkflowStep = excluded.WorkflowStep,
                Body = excluded.Body,
                SeedBody = excluded.SeedBody,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", template.Id.Trim());
        command.Parameters.AddWithValue("$key", template.Key.Trim());
        command.Parameters.AddWithValue("$scope", (int)template.Scope);
        command.Parameters.AddWithValue("$characterProfileId", (object?)template.CharacterProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$workflowStep", template.WorkflowStep.Trim());
        command.Parameters.AddWithValue("$body", template.Body);
        command.Parameters.AddWithValue("$seedBody", template.SeedBody);
        command.Parameters.AddWithValue("$updatedUtc", template.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ReferenceWorkflowSettings?> GetSettingsAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Settings id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CharacterProfileId, EditorModelId, UpscalerModelName, EnhanceTargetLongEdge,
                   EyeGateMaxAbsIrisDyPercent, QualityGateMinSharpness, CropHeadroomPercent, CropTargetAspect,
                   DeriveByMirrorThreeQuarterRight, DeriveByMirrorProfileRight, DeriveByMirrorThreeQuarterLeft,
                   DeriveByMirrorProfileLeft, EyeToolPythonPath, UpdatedUtc, FrontModelId
            FROM ReferenceWorkflowSettings WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSettings(reader) : null;
    }

    public async Task UpsertSettingsAsync(ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ReferenceWorkflowSettings (
                Id, CharacterProfileId, EditorModelId, UpscalerModelName, EnhanceTargetLongEdge,
                EyeGateMaxAbsIrisDyPercent, QualityGateMinSharpness, CropHeadroomPercent, CropTargetAspect,
                DeriveByMirrorThreeQuarterRight, DeriveByMirrorProfileRight, DeriveByMirrorThreeQuarterLeft,
                DeriveByMirrorProfileLeft, EyeToolPythonPath, UpdatedUtc, FrontModelId)
            VALUES (
                $id, $characterProfileId, $editorModelId, $upscalerModelName, $enhanceTargetLongEdge,
                $eyeGate, $qualityGate, $cropHeadroom, $cropAspect,
                $mirror3qr, $mirrorProfR, $mirror3ql, $mirrorProfL, $eyeToolPythonPath, $updatedUtc, $frontModelId)
            ON CONFLICT(Id) DO UPDATE SET
                EditorModelId = excluded.EditorModelId,
                UpscalerModelName = excluded.UpscalerModelName,
                EnhanceTargetLongEdge = excluded.EnhanceTargetLongEdge,
                EyeGateMaxAbsIrisDyPercent = excluded.EyeGateMaxAbsIrisDyPercent,
                QualityGateMinSharpness = excluded.QualityGateMinSharpness,
                CropHeadroomPercent = excluded.CropHeadroomPercent,
                CropTargetAspect = excluded.CropTargetAspect,
                DeriveByMirrorThreeQuarterRight = excluded.DeriveByMirrorThreeQuarterRight,
                DeriveByMirrorProfileRight = excluded.DeriveByMirrorProfileRight,
                DeriveByMirrorThreeQuarterLeft = excluded.DeriveByMirrorThreeQuarterLeft,
                DeriveByMirrorProfileLeft = excluded.DeriveByMirrorProfileLeft,
                EyeToolPythonPath = excluded.EyeToolPythonPath,
                FrontModelId = excluded.FrontModelId,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", settings.Id.Trim());
        command.Parameters.AddWithValue("$characterProfileId", (object?)settings.CharacterProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$editorModelId", (object?)settings.EditorModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$upscalerModelName", (object?)settings.UpscalerModelName ?? DBNull.Value);
        command.Parameters.AddWithValue("$enhanceTargetLongEdge", settings.EnhanceTargetLongEdge);
        command.Parameters.AddWithValue("$eyeGate", settings.EyeGateMaxAbsIrisDyPercent);
        command.Parameters.AddWithValue("$qualityGate", settings.QualityGateMinSharpness);
        command.Parameters.AddWithValue("$cropHeadroom", settings.CropHeadroomPercent);
        command.Parameters.AddWithValue("$cropAspect", settings.CropTargetAspect);
        command.Parameters.AddWithValue("$mirror3qr", settings.DeriveByMirrorThreeQuarterRight ? 1 : 0);
        command.Parameters.AddWithValue("$mirrorProfR", settings.DeriveByMirrorProfileRight ? 1 : 0);
        command.Parameters.AddWithValue("$mirror3ql", settings.DeriveByMirrorThreeQuarterLeft ? 1 : 0);
        command.Parameters.AddWithValue("$mirrorProfL", settings.DeriveByMirrorProfileLeft ? 1 : 0);
        command.Parameters.AddWithValue("$eyeToolPythonPath", (object?)settings.EyeToolPythonPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", settings.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$frontModelId", (object?)settings.FrontModelId ?? DBNull.Value);
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
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ImageWorkflowPromptTemplates (
                    Id TEXT PRIMARY KEY,
                    Key TEXT NOT NULL,
                    Scope INTEGER NOT NULL,
                    CharacterProfileId TEXT NULL,
                    WorkflowStep TEXT NOT NULL DEFAULT '',
                    Body TEXT NOT NULL DEFAULT '',
                    SeedBody TEXT NOT NULL DEFAULT '',
                    UpdatedUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ImageWorkflowPromptTemplates_KeyScope
                    ON ImageWorkflowPromptTemplates (Key, Scope, CharacterProfileId);

                CREATE TABLE IF NOT EXISTS ReferenceWorkflowSettings (
                    Id TEXT PRIMARY KEY,
                    CharacterProfileId TEXT NULL,
                    EditorModelId TEXT NULL,
                    UpscalerModelName TEXT NULL,
                    EnhanceTargetLongEdge INTEGER NOT NULL DEFAULT 1024,
                    EyeGateMaxAbsIrisDyPercent REAL NOT NULL DEFAULT 1.5,
                    QualityGateMinSharpness INTEGER NOT NULL DEFAULT 250,
                    CropHeadroomPercent INTEGER NOT NULL DEFAULT 8,
                    CropTargetAspect REAL NOT NULL DEFAULT 1.0,
                    DeriveByMirrorThreeQuarterRight INTEGER NOT NULL DEFAULT 1,
                    DeriveByMirrorProfileRight INTEGER NOT NULL DEFAULT 1,
                    DeriveByMirrorThreeQuarterLeft INTEGER NOT NULL DEFAULT 0,
                    DeriveByMirrorProfileLeft INTEGER NOT NULL DEFAULT 0,
                    EyeToolPythonPath TEXT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    FrontModelId TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            var settingsColumns = await QueryColumnsAsync(connection, "ReferenceWorkflowSettings", cancellationToken);
            if (!settingsColumns.Contains("FrontModelId"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE ReferenceWorkflowSettings ADD COLUMN FrontModelId TEXT NULL;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await SeedAsync(connection, cancellationToken);
    }

    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        foreach (var seed in SeedTemplates())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO ImageWorkflowPromptTemplates
                    (Id, Key, Scope, CharacterProfileId, WorkflowStep, Body, SeedBody, UpdatedUtc)
                VALUES ($id, $key, 1, NULL, $workflowStep, $body, $body, $updatedUtc);
                """;
            command.Parameters.AddWithValue("$id", seed.Id);
            command.Parameters.AddWithValue("$key", seed.Key);
            command.Parameters.AddWithValue("$workflowStep", seed.WorkflowStep);
            command.Parameters.AddWithValue("$body", seed.Body);
            command.Parameters.AddWithValue("$updatedUtc", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var settingsCommand = connection.CreateCommand())
        {
            settingsCommand.CommandText = """
                INSERT OR IGNORE INTO ReferenceWorkflowSettings (
                    Id, CharacterProfileId, EditorModelId, UpscalerModelName, EnhanceTargetLongEdge,
                    EyeGateMaxAbsIrisDyPercent, QualityGateMinSharpness, CropHeadroomPercent, CropTargetAspect,
                    DeriveByMirrorThreeQuarterRight, DeriveByMirrorProfileRight, DeriveByMirrorThreeQuarterLeft,
                    DeriveByMirrorProfileLeft, EyeToolPythonPath, UpdatedUtc, FrontModelId)
                VALUES ('global', NULL, NULL, NULL, 1024, 1.5, 250, 8, 1.0, 1, 1, 0, 0, NULL, $updatedUtc, NULL);
                """;
            settingsCommand.Parameters.AddWithValue("$updatedUtc", now);
            await settingsCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<ImageWorkflowPromptTemplate> SeedTemplates()
    {
        var seeds = new[]
        {
            new ImageWorkflowPromptTemplate
            {
                Key = "identity.front.generate",
                WorkflowStep = "Front",
                Body = "Photorealistic frontal portrait of {CharacterName}: {Description}. Head and shoulders, facing the camera. Neutral background, even lighting, sharp focus, natural skin texture."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = "identity.garment.remove",
                WorkflowStep = "GarmentRemoval",
                Body = "Remove the shirt and replace it with the same plain background, so the subject is shown with bare neck and bare shoulders. Keep {SubjectPronounPossessive} neck, throat, collarbones and shoulders exactly as they are - do not remove, shorten, hide or cover the neck. Keep the identical crop, framing, zoom and head size. Do not add any clothing."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = "identity.angle.three-quarter",
                WorkflowStep = "Angles",
                Body = "Turn the person's head and upper body to a three-quarter view so the nose points toward the LEFT side of the image and more of the left side of the face is visible. Keep the exact same face, hair, facial features, identity, bare neck and bare shoulders, and lighting unchanged. Do not add any clothing. Keep the identical crop, framing, zoom and head size."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = "identity.angle.profile",
                WorkflowStep = "Angles",
                Body = "Turn the person's head to a full profile so the nose points toward the LEFT side of the image and the left side of the face is shown in full profile. Keep the exact same face, hair, facial features, identity, bare neck and bare shoulders, and lighting unchanged. Do not add any clothing. Keep the identical crop, framing, zoom and head size."
            }
        };

        foreach (var seed in seeds)
        {
            seed.Id = ImageWorkflowPromptTemplate.ComputeId(seed.Key, ImageWorkflowPromptTemplateScope.Global, null);
        }

        return seeds;
    }

    private static ImageWorkflowPromptTemplate ReadTemplate(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Key = reader.GetString(1),
        Scope = (ImageWorkflowPromptTemplateScope)reader.GetInt32(2),
        CharacterProfileId = reader.IsDBNull(3) ? null : reader.GetString(3),
        WorkflowStep = reader.GetString(4),
        Body = reader.GetString(5),
        SeedBody = reader.GetString(6),
        UpdatedUtc = ParseUtc(reader.GetString(7))
    };

    private static ReferenceWorkflowSettings ReadSettings(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        CharacterProfileId = reader.IsDBNull(1) ? null : reader.GetString(1),
        EditorModelId = reader.IsDBNull(2) ? null : reader.GetString(2),
        UpscalerModelName = reader.IsDBNull(3) ? null : reader.GetString(3),
        EnhanceTargetLongEdge = reader.GetInt32(4),
        EyeGateMaxAbsIrisDyPercent = reader.GetDouble(5),
        QualityGateMinSharpness = reader.GetInt32(6),
        CropHeadroomPercent = reader.GetInt32(7),
        CropTargetAspect = reader.GetDouble(8),
        DeriveByMirrorThreeQuarterRight = reader.GetInt32(9) != 0,
        DeriveByMirrorProfileRight = reader.GetInt32(10) != 0,
        DeriveByMirrorThreeQuarterLeft = reader.GetInt32(11) != 0,
        DeriveByMirrorProfileLeft = reader.GetInt32(12) != 0,
        EyeToolPythonPath = reader.IsDBNull(13) ? null : reader.GetString(13),
        UpdatedUtc = ParseUtc(reader.GetString(14)),
        FrontModelId = reader.IsDBNull(15) ? null : reader.GetString(15)
    };

    private static void ValidateTemplate(ImageWorkflowPromptTemplate template)
    {
        Require(template.Id, "Template id");
        Require(template.Key, "Template key");
        if (string.IsNullOrWhiteSpace(template.Body))
            throw new InvalidOperationException($"Template body is required (key '{template.Key}').");
    }

    private static void ValidateSettings(ReferenceWorkflowSettings settings)
    {
        Require(settings.Id, "Settings id");

        // These three drive pixel-exact behaviour (crop framing and enhance size). They are persisted
        // configuration, deliberately with no code default: an unset value must fail fast naming the key
        // rather than quietly applying a number nobody chose.
        if (settings.EnhanceTargetLongEdge is not { } enhanceTargetLongEdge || enhanceTargetLongEdge <= 0)
        {
            throw new InvalidOperationException(
                "EnhanceTargetLongEdge is required and must be positive; it has no code default and must be configured.");
        }

        if (settings.CropHeadroomPercent is not { } cropHeadroomPercent || cropHeadroomPercent is < 0 or > 100)
        {
            throw new InvalidOperationException(
                "CropHeadroomPercent is required and must be between 0 and 100; it has no code default and must be configured.");
        }

        if (settings.CropTargetAspect is not { } cropTargetAspect || cropTargetAspect <= 0)
        {
            throw new InvalidOperationException(
                "CropTargetAspect is required and must be positive; it has no code default and must be configured.");
        }
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static async Task<HashSet<string>> QueryColumnsAsync(
        SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid UTC value '{value}'.");
}
