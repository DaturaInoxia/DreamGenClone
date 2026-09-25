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
                   DeriveByMirrorProfileLeft, EyeToolPythonPath, UpdatedUtc, FrontModelId, AngleYawMinAbsPercent,
                   BodyModelId, BodyImageSize, LoraCellModelId
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
                DeriveByMirrorProfileLeft, EyeToolPythonPath, UpdatedUtc, FrontModelId, AngleYawMinAbsPercent,
                BodyModelId, BodyImageSize, LoraCellModelId)
            VALUES (
                $id, $characterProfileId, $editorModelId, $upscalerModelName, $enhanceTargetLongEdge,
                $eyeGate, $qualityGate, $cropHeadroom, $cropAspect,
                $mirror3qr, $mirrorProfR, $mirror3ql, $mirrorProfL, $eyeToolPythonPath, $updatedUtc, $frontModelId,
                $angleYawMinAbs, $bodyModelId, $bodyImageSize, $loraCellModelId)
            ON CONFLICT(Id) DO UPDATE SET
                EditorModelId = excluded.EditorModelId,
                UpscalerModelName = excluded.UpscalerModelName,
                EnhanceTargetLongEdge = excluded.EnhanceTargetLongEdge,
                EyeGateMaxAbsIrisDyPercent = excluded.EyeGateMaxAbsIrisDyPercent,
                AngleYawMinAbsPercent = excluded.AngleYawMinAbsPercent,
                QualityGateMinSharpness = excluded.QualityGateMinSharpness,
                CropHeadroomPercent = excluded.CropHeadroomPercent,
                CropTargetAspect = excluded.CropTargetAspect,
                DeriveByMirrorThreeQuarterRight = excluded.DeriveByMirrorThreeQuarterRight,
                DeriveByMirrorProfileRight = excluded.DeriveByMirrorProfileRight,
                DeriveByMirrorThreeQuarterLeft = excluded.DeriveByMirrorThreeQuarterLeft,
                DeriveByMirrorProfileLeft = excluded.DeriveByMirrorProfileLeft,
                EyeToolPythonPath = excluded.EyeToolPythonPath,
                FrontModelId = excluded.FrontModelId,
                BodyModelId = excluded.BodyModelId,
                BodyImageSize = excluded.BodyImageSize,
                LoraCellModelId = excluded.LoraCellModelId,
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
        command.Parameters.AddWithValue("$angleYawMinAbs", settings.AngleYawMinAbsPercent);
        command.Parameters.AddWithValue("$bodyModelId", (object?)settings.BodyModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$bodyImageSize", (object?)settings.BodyImageSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$loraCellModelId", (object?)settings.LoraCellModelId ?? DBNull.Value);
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
                    FrontModelId TEXT NULL,
                    AngleYawMinAbsPercent REAL NOT NULL DEFAULT 5.0,
                    BodyModelId TEXT NULL,
                    BodyImageSize TEXT NULL,
                    LoraCellModelId TEXT NULL
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

            if (!settingsColumns.Contains("AngleYawMinAbsPercent"))
            {
                // 5.0 is the seed's starting value written into the column for existing rows; the value in
                // force is always the persisted one.
                await using var alterYaw = connection.CreateCommand();
                alterYaw.CommandText = "ALTER TABLE ReferenceWorkflowSettings ADD COLUMN AngleYawMinAbsPercent REAL NOT NULL DEFAULT 5.0;";
                await alterYaw.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!settingsColumns.Contains("BodyModelId"))
            {
                await using var alterBodyModel = connection.CreateCommand();
                alterBodyModel.CommandText = "ALTER TABLE ReferenceWorkflowSettings ADD COLUMN BodyModelId TEXT NULL;";
                await alterBodyModel.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!settingsColumns.Contains("BodyImageSize"))
            {
                await using var alterBodySize = connection.CreateCommand();
                alterBodySize.CommandText = "ALTER TABLE ReferenceWorkflowSettings ADD COLUMN BodyImageSize TEXT NULL;";
                await alterBodySize.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!settingsColumns.Contains("LoraCellModelId"))
            {
                await using var alterLoraCellModel = connection.CreateCommand();
                alterLoraCellModel.CommandText = "ALTER TABLE ReferenceWorkflowSettings ADD COLUMN LoraCellModelId TEXT NULL;";
                await alterLoraCellModel.ExecuteNonQueryAsync(cancellationToken);
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
                    DeriveByMirrorProfileLeft, EyeToolPythonPath, UpdatedUtc, FrontModelId, AngleYawMinAbsPercent,
                    BodyImageSize)
                VALUES ('global', NULL, NULL, NULL, 1024, 1.5, 250, 8, 1.0, 1, 1, 0, 0, NULL, $updatedUtc, NULL, 5.0,
                        '1024x1536');
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
                Key = "identity.angle.three-quarter.right",
                WorkflowStep = "Angles",
                Body = "Turn the person's head and upper body to a three-quarter view so the nose points toward the RIGHT side of the image and more of the right side of the face is visible. Keep the exact same face, hair, facial features, identity, bare neck and bare shoulders, and lighting unchanged. Do not add any clothing. Keep the identical crop, framing, zoom and head size."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = "identity.angle.profile",
                WorkflowStep = "Angles",
                Body = "Turn the person's head to a full profile so the nose points toward the LEFT side of the image and the left side of the face is shown in full profile. Keep the exact same face, hair, facial features, identity, bare neck and bare shoulders, and lighting unchanged. Do not add any clothing. Keep the identical crop, framing, zoom and head size."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = "identity.angle.profile.right",
                WorkflowStep = "Angles",
                Body = "Turn the person's head to a full profile so the nose points toward the RIGHT side of the image and the right side of the face is shown in full profile. Keep the exact same face, hair, facial features, identity, bare neck and bare shoulders, and lighting unchanged. Do not add any clothing. Keep the identical crop, framing, zoom and head size."
            },

            // B-122 Phase 0 — the body target's keys. These are the same store, a body namespace: the body
            // pipeline resolves its prompt by key exactly as the face pipeline does, so no prompt body is
            // embedded in code. The card line ({BodyCard}) is the invariant body description and is pasted
            // verbatim, never paraphrased.
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.ClothedAcquire,
                WorkflowStep = "BodyClothedBase",
                Body = "Full-body photograph of {CharacterName}, head to feet, standing straight and facing the camera: {BodyCard}. Wearing plain everyday clothing. Neutral background, even lighting, sharp focus, natural skin texture, the whole body in frame and unobstructed."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.UnclothedAcquire,
                WorkflowStep = "BodyUnclothedBase",
                Body = "Full-body photograph of {CharacterName}, head to feet, standing straight and facing the camera: {BodyCard}. No clothing covering the body. Neutral background, even lighting, sharp focus, natural skin texture, the whole body in frame and unobstructed."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.AngleThreeQuarterLeft,
                WorkflowStep = "BodyAngles",
                Body = "Rotate the person's whole body to a three-quarter view so they face toward the LEFT side of the image. Keep the identical body, proportions, skin, body hair and marks, the identical head-to-feet framing, zoom and lighting. Do not change the body shape or the set."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.AngleThreeQuarterRight,
                WorkflowStep = "BodyAngles",
                Body = "Rotate the person's whole body to a three-quarter view so they face toward the RIGHT side of the image. Keep the identical body, proportions, skin, body hair and marks, the identical head-to-feet framing, zoom and lighting. Do not change the body shape or the set."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.AngleProfileLeft,
                WorkflowStep = "BodyAngles",
                Body = "Rotate the person's whole body to a full profile facing toward the LEFT side of the image, seen from the side. Keep the identical body, proportions, skin, body hair and marks, the identical head-to-feet framing, zoom and lighting. Do not change the body shape or the set."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.AngleProfileRight,
                WorkflowStep = "BodyAngles",
                Body = "Rotate the person's whole body to a full profile facing toward the RIGHT side of the image, seen from the side. Keep the identical body, proportions, skin, body hair and marks, the identical head-to-feet framing, zoom and lighting. Do not change the body shape or the set."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.ExtendedView,
                WorkflowStep = "BodyExtended",
                Body = "Turn the person's whole body to the requested rotation and body position while keeping the identical body, proportions, skin, body hair and marks. Keep the identical head-to-feet framing, zoom, lighting and set. Do not change the body shape."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.Normalize,
                WorkflowStep = "BodyNormalize",
                Body = "Keep the pose, position, framing, lighting and background exactly as they are and align only the body to this description: {BodyCard}. Do not change the pose or the composition."
            },

            // The ANGLE RENDER camera clauses. These are appended to the compiled body prompt when an angle is
            // RENDERED from an accepted body rather than rotated out of it, and they say where the camera is — the
            // geometry of the view — because the committed angle skeleton supplies the pose and the accepted body
            // supplies the build. Measured 2026-09-23: this wording, with those two references, is what produced a
            // true three-quarter and a true edge-on profile (cases body-angle-34-*, body-profile-*).
            //
            // They are deliberately SHORT and deliberately do NOT restate the framing or use {CharacterName}: the
            // combined text is validated as one body prompt, whose ceiling is 800 characters (§2.2) and which forbids
            // a name (§2.3 rule 2). A long clause made every angle render exceed the ceiling and refuse — measured
            // 2026-09-23 on the live build: 563-char body prompt + 328-char clause = 891 > 800.
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.RenderThreeQuarterLeft,
                WorkflowStep = "BodyAngleRender",
                Body = "Camera slightly to the front-left, body turned three-quarters away, left side nearer the camera, facing left of frame."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.RenderThreeQuarterRight,
                WorkflowStep = "BodyAngleRender",
                Body = "Camera slightly to the front-right, body turned three-quarters away, right side nearer the camera, facing right of frame."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.RenderProfileLeft,
                WorkflowStep = "BodyAngleRender",
                Body = "Camera directly to the left, body edge-on in full left profile, nose pointing to the left of frame."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.RenderProfileRight,
                WorkflowStep = "BodyAngleRender",
                Body = "Camera directly to the right, body edge-on in full right profile, nose pointing to the right of frame."
            },
            // The BACK view's clause (operator request, 2026-09-24). "No face is visible" is not decoration: the accepted
            // body reference is the FRONT, so without that sentence the model is free to turn the head and present the
            // face it was shown. Measured: with this wording the render is a true back view with no face (proof case
            // body-back-front-plus-skeleton).
            new ImageWorkflowPromptTemplate
            {
                Key = CharacterBodyWorkflowKeys.RenderBack,
                WorkflowStep = "BodyAngleRender",
                Body = "Camera directly behind the subject in a full back view: the back of the head, the back, the backside and the backs of the legs, the face not visible."
            },

            // B-123 Phase 1 — the LoRA coverage cell's prompts. A training image is a photograph of a person
            // under stated conditions, so each template states the framing, the angle family, the wardrobe and
            // the four context axes, and nothing else. Slots only: the invariant body description arrives as
            // {BodyCard} and the variable axes arrive as phrases resolved from the vocabulary rows below, so no
            // wording lives in code and every word is editable here.
            //
            // There is deliberately no {CharacterName}: in a training image identity comes from the trigger
            // token and the references, and a name in the text would bind the look to a word the caption is
            // forbidden to contain.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderFrontClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {BodyCard}. {Facing}, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderFrontHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {BodyCard} from the waist up. {Facing}, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderFrontFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {BodyCard}, head to feet. {Facing}, the whole body in frame and unobstructed, nothing cropped. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderThreeQuarterClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {BodyCard}. The head and shoulders are turned three-quarters away from the camera, {Facing}, one cheek nearer the camera than the other, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderThreeQuarterHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {BodyCard} from the waist up, the head and upper body turned three-quarters away from the camera, {Facing}, one side of the body nearer the camera, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderThreeQuarterFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {BodyCard}, head to feet, the body turned three-quarters away from the camera, {Facing}, one side of the body nearer the camera, the whole body in frame and unobstructed. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderProfileClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {BodyCard} in full profile, seen from the side, {Facing}, a true edge-on profile of the head and shoulders rather than a turned head. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderProfileHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {BodyCard} from the waist up in full profile, seen from the side, {Facing}, a true edge-on profile of the head and upper body. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderProfileFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {BodyCard}, head to feet, in full profile seen from the side, {Facing}, a true edge-on profile of the whole body, nothing cropped. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderBehindClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {BodyCard} from behind, {Facing}, the back of the head and both shoulders in frame, no part of the face visible. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderBehindHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {BodyCard} from behind, from the waist up, {Facing}, the back of the head, the shoulders and the back in frame, no part of the face visible. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderBehindFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {BodyCard}, head to feet, from behind, {Facing}, the back of the head, the back, the backside and the backs of the legs in frame, no part of the face visible, nothing cropped. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, no retouching."
            },

            // The caption. Comma-separated tags with the trigger token FIRST: the token is what the whole
            // identity binds to, and pinning it at the front is what lets a trainer shuffle the tail without
            // ever shuffling the identity into the middle of the caption. There is no invariant slot here, and
            // there must never be one — face, body shape, skin, body hair, grooming, marks and tattoos bind to
            // the token precisely because no caption ever names them.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.Caption,
                WorkflowStep = "LoraCellCaption",
                Body = "{TriggerToken}, {Wardrobe}, {Angle}, {Distance}, {Pose}, {Expression}, {Lighting}, {Background}"
            },
            // The minor-tweak edit: change one named thing, keep everything else byte-for-byte.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.EditTweak,
                WorkflowStep = "LoraCellEdit",
                Body = "Keep the identity, the body, the pose, the framing, the zoom, the lighting and the background exactly as they are and change only this: {Change}. Do not alter the face, the body shape or any mark on the body."
            },
            // No negative-prompt row: see the note in LoraCellWorkflowKeys. The families in use take no negative,
            // and the render path compiles the cell prompt (no compiler id is set), which authors none itself.
            // The reference rule, shown beside the picker so the operator can see what each reference is for
            // and what it must NOT be allowed to bring along.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.References,
                WorkflowStep = "LoraCellReferences",
                Body = "Face reference: {FaceReference}. Body reference: {BodyReference}. Take identity from the face reference only, and body shape, proportions, skin, body hair and marks from the body reference only. Do not take the pose, the clothing, the lighting or the background from either reference."
            },

            // The variant wording. Every phrase here is data: the generator reads it and the operator can edit
            // it. A phrase that is missing fails fast by key rather than falling back to a guess.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyWardrobeClothed,
                WorkflowStep = "LoraCellVocabulary",
                Body = "clothed"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyWardrobeUnclothed,
                WorkflowStep = "LoraCellVocabulary",
                Body = "nude"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyPoseStanding,
                WorkflowStep = "LoraCellVocabulary",
                Body = "standing"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyPoseSitting,
                WorkflowStep = "LoraCellVocabulary",
                Body = "sitting"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyPoseKneeling,
                WorkflowStep = "LoraCellVocabulary",
                Body = "kneeling"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyPoseLying,
                WorkflowStep = "LoraCellVocabulary",
                Body = "lying down"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyPoseAllFours,
                WorkflowStep = "LoraCellVocabulary",
                Body = "on all fours"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyPoseHandsRaised,
                WorkflowStep = "LoraCellVocabulary",
                Body = "with both arms raised above the head"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyExpressionNeutral,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a neutral relaxed expression"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyExpressionSmiling,
                WorkflowStep = "LoraCellVocabulary",
                Body = "smiling"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyExpressionLaughing,
                WorkflowStep = "LoraCellVocabulary",
                Body = "laughing openly"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyExpressionSurprised,
                WorkflowStep = "LoraCellVocabulary",
                Body = "surprised, eyebrows raised, mouth slightly open"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyExpressionSerious,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a serious closed-mouth expression"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyExpressionSensual,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a soft sensual expression, lips slightly parted"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingIndoorBright,
                WorkflowStep = "LoraCellVocabulary",
                Body = "even bright indoor lighting"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
                WorkflowStep = "LoraCellVocabulary",
                Body = "dim indoor lighting with soft shadows"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingOutdoorDay,
                WorkflowStep = "LoraCellVocabulary",
                Body = "flat daylight outdoors"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingOutdoorGolden,
                WorkflowStep = "LoraCellVocabulary",
                Body = "warm golden-hour sunlight from the side"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingOutdoorNight,
                WorkflowStep = "LoraCellVocabulary",
                Body = "low ambient light at night with a single practical light source"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingHardRim,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a hard rim light along the edge of the body against a dark surround"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyBackgroundPlainWall,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a plain neutral wall"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyBackgroundBedroom,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a simple bedroom with a plain bed and one window"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyBackgroundLivingRoom,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a simply furnished living room"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyBackgroundKitchen,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a plain kitchen with visible cabinets"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyBackgroundOutdoors,
                WorkflowStep = "LoraCellVocabulary",
                Body = "an outdoor setting with trees and open sky"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyBackgroundStudio,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a plain studio backdrop with a soft shadow on the floor"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitCasual,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a plain t-shirt and jeans"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitFormal,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a buttoned shirt and tailored trousers"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitAthletic,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a fitted athletic top and shorts"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitLoungewear,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a loose knit sweater and soft trousers"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitSleepwear,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a thin sleeveless top and short sleep shorts"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitUnclothed,
                WorkflowStep = "LoraCellVocabulary",
                Body = "completely unclothed, with no clothing at all and nothing covering the body"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyDistanceClose,
                WorkflowStep = "LoraCellVocabulary",
                Body = "close-up"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyDistanceHalf,
                WorkflowStep = "LoraCellVocabulary",
                Body = "half-body"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyDistanceFull,
                WorkflowStep = "LoraCellVocabulary",
                Body = "full-body"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyAngleFront,
                WorkflowStep = "LoraCellVocabulary",
                Body = "front view"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyAngleThreeQuarter,
                WorkflowStep = "LoraCellVocabulary",
                Body = "three-quarter view"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyAngleProfile,
                WorkflowStep = "LoraCellVocabulary",
                Body = "profile view"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyAngleBehind,
                WorkflowStep = "LoraCellVocabulary",
                Body = "from behind"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyFacingCamera,
                WorkflowStep = "LoraCellVocabulary",
                Body = "facing the camera straight on"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyFacingLeft,
                WorkflowStep = "LoraCellVocabulary",
                Body = "with the nose pointing toward the left of frame"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyFacingRight,
                WorkflowStep = "LoraCellVocabulary",
                Body = "with the nose pointing toward the right of frame"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyFacingAway,
                WorkflowStep = "LoraCellVocabulary",
                Body = "with the back of the head toward the camera"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularySplitTrain,
                WorkflowStep = "LoraCellVocabulary",
                Body = "train"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularySplitValidation,
                WorkflowStep = "LoraCellVocabulary",
                Body = "validation"
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
        FrontModelId = reader.IsDBNull(15) ? null : reader.GetString(15),
        AngleYawMinAbsPercent = reader.GetDouble(16),
        BodyModelId = reader.IsDBNull(17) ? null : reader.GetString(17),
        BodyImageSize = reader.IsDBNull(18) ? null : reader.GetString(18),
        LoraCellModelId = reader.IsDBNull(19) ? null : reader.GetString(19)
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
