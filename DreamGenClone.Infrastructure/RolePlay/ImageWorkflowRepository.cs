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
                   BodyModelId, BodyImageSize, LoraCellModelId, RegionGrowMaskBy, RegionFeatherPixels
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
                BodyModelId, BodyImageSize, LoraCellModelId, RegionGrowMaskBy, RegionFeatherPixels)
            VALUES (
                $id, $characterProfileId, $editorModelId, $upscalerModelName, $enhanceTargetLongEdge,
                $eyeGate, $qualityGate, $cropHeadroom, $cropAspect,
                $mirror3qr, $mirrorProfR, $mirror3ql, $mirrorProfL, $eyeToolPythonPath, $updatedUtc, $frontModelId,
                $angleYawMinAbs, $bodyModelId, $bodyImageSize, $loraCellModelId, $regionGrowMaskBy, $regionFeatherPixels)
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
                RegionGrowMaskBy = excluded.RegionGrowMaskBy,
                RegionFeatherPixels = excluded.RegionFeatherPixels,
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
        command.Parameters.AddWithValue("$regionGrowMaskBy", settings.RegionGrowMaskBy);
        command.Parameters.AddWithValue("$regionFeatherPixels", settings.RegionFeatherPixels);
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
                    LoraCellModelId TEXT NULL,
                    RegionGrowMaskBy INTEGER NOT NULL DEFAULT 8,
                    RegionFeatherPixels INTEGER NOT NULL DEFAULT 0
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

            if (!settingsColumns.Contains("RegionGrowMaskBy"))
            {
                // 8 is the seed's starting value written into the column for existing rows: a mask cut exactly on the
                // rectangle's edge leaves a seam, so the seed starts a few pixels of overlap. The value in force is
                // always the persisted one, and the region panel lets the operator change it per run.
                await using var alterRegionGrow = connection.CreateCommand();
                alterRegionGrow.CommandText = "ALTER TABLE ReferenceWorkflowSettings ADD COLUMN RegionGrowMaskBy INTEGER NOT NULL DEFAULT 8;";
                await alterRegionGrow.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!settingsColumns.Contains("RegionFeatherPixels"))
            {
                // 0 is the seed: no feather node is emitted at all, which is the plainest possible region edit and the
                // one the masked-latent wiring was first proven with. Softening the edge is the operator's choice.
                await using var alterRegionFeather = connection.CreateCommand();
                alterRegionFeather.CommandText = "ALTER TABLE ReferenceWorkflowSettings ADD COLUMN RegionFeatherPixels INTEGER NOT NULL DEFAULT 0;";
                await alterRegionFeather.ExecuteNonQueryAsync(cancellationToken);
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
            // the four context axes, and nothing else. Slots only: the subject description arrives as
            // {Subject} and the variable axes arrive as phrases resolved from the vocabulary rows below, so no
            // wording lives in code and every word is editable here.
            //
            // The tail is written WITHOUT negations. The SDXL-family research is explicit that "no X" belongs in
            // the negative and never in the positive (prompt-compiler standards 3.2), and this pipeline authors no
            // negative at all for any of the families it renders - so "no retouching" was not a mild instruction,
            // it was text the model had nothing to do with. "Nothing cropped" said the same thing the framing
            // clause already says, and the behind views now use the same "the face not visible" clause the
            // measured back view uses.
            //
            // There is deliberately no {CharacterName}: in a training image identity comes from the trigger
            // token and the references, and a name in the text would bind the look to a word the caption is
            // forbidden to contain.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderFrontClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {Subject}. {Facing}, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderFrontHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {Subject} from the waist up. {Facing}, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderFrontFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {Subject}, head to feet. {Facing}, the whole body in frame and unobstructed. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderThreeQuarterClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {Subject}. The head and shoulders are turned three-quarters away from the camera, {Facing}, one cheek nearer the camera than the other, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderThreeQuarterHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {Subject} from the waist up, the head and upper body turned three-quarters away from the camera, {Facing}, one side of the body nearer the camera, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderThreeQuarterFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {Subject}, head to feet, the body turned three-quarters away from the camera, {Facing}, one side of the body nearer the camera, the whole body in frame and unobstructed. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderProfileClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {Subject} in full profile, seen from the side, {Facing}, a true edge-on profile of the head and shoulders rather than a turned head. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderProfileHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {Subject} from the waist up in full profile, seen from the side, {Facing}, a true edge-on profile of the head and upper body. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderProfileFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {Subject}, head to feet, in full profile seen from the side, {Facing}, a true edge-on profile of the whole body. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderBehindClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {Subject} from behind, {Facing}, the back of the head and both shoulders in frame, the face not visible. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderBehindHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {Subject} from behind, from the waist up, {Facing}, the back of the head, the shoulders and the back in frame, the face not visible. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderBehindFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {Subject}, head to feet, from behind, {Facing}, the back of the head, the back, the backside and the backs of the legs in frame, the face not visible, the whole body in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },

            // ---- FAR cells, added 2026-10-04. ---------------------------------------------------------------
            //
            // The distance is stated as THE SPACE AND WHERE THE CAMERA IS IN IT, never as a demand about the subject.
            // That distinction is measured, not stylistic: a brief that asked for "the whole room in frame with her
            // standing small in it" was ignored and returned a normal framing with a face around 130 px, while the
            // same subject briefed as a long bare room with the camera across it returned the far framing the operator
            // wanted at around 60 px. A model reading "small in the frame" has no reason to shrink the subject; a
            // model reading "the camera is at the far end of a long room" has to, because that is what the camera
            // position means. So every far template below names the room, the camera's place at the far end of it, and
            // the floor between the two - and lets the subject's size follow from that.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderFrontFar,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic wide photograph taken from the far end of a long room, {Subject} standing a long way off with the full length of bare floor between the camera and the subject and the whole space visible around the subject. {Facing}, the camera back at the far end of the room looking down its length. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderProfileFar,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic wide photograph taken from the far end of a long room, {Subject} in full profile standing a long way off, seen edge-on, with the full length of bare floor between the camera and the subject and the whole space visible around the subject. {Facing}, the camera back at the far end of the room looking down its length, a true edge-on profile of the whole figure at that distance. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderBehindFar,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic wide photograph taken from the far end of a long room, {Subject} seen from behind standing a long way off with the full length of bare floor between the camera and the subject and the whole space visible around the subject. {Facing}, the back of the head and the whole figure at that distance, the face not visible, the camera back at the far end of the room looking down its length. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },

            // ---- OVER-THE-SHOULDER cells, added 2026-10-04. ---------------------------------------------------
            //
            // The one framing in the set that shows a FACE while the body is turned away, so every template states
            // both halves of that pose rather than the turn alone: the back of the near shoulder faces the camera and
            // the head has come back over it into view. Saying only "from behind" is the back-view row, which is
            // defined by having no face at all; saying only "looking back" leaves the body facing the camera and is a
            // three-quarter. The two halves together are the shot.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderOverShoulderClose,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic close-up photograph of {Subject} with the body turned away and the head turned back over one shoulder to look at the camera, {Facing}, the back of the near shoulder closest to the camera and the face come back into view over it, the whole head and both shoulders in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderOverShoulderHalf,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic photograph of {Subject} from the waist up with the body turned away and the head turned back over one shoulder to look at the camera, {Facing}, the back of the near shoulder closest to the camera and the face come back into view over it, the whole upper body and both hands in frame. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.RenderOverShoulderFull,
                WorkflowStep = "LoraCellRender",
                Body = "Photorealistic full-body photograph of {Subject}, head to feet, with the body turned away and the head turned back over one shoulder to look at the camera, {Facing}, the back of the near shoulder closest to the camera and the face come back into view over it, the whole body in frame and unobstructed. {Wardrobe}. {Pose}. {Expression}. {Lighting}. Background: {Background}. Sharp focus, natural skin texture, fine detail."
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
            //
            // The LIGHTING phrases name the LIGHT and never the setting. The environment belongs to the Background
            // row, so a lighting phrase that also named it ("... indoor lighting", "... outdoors", "against a dark
            // surround") put the same fact in two places and the two were free to disagree: measured on the live
            // plan, 14 of 36 cells paired an indoor phrase with an outdoor setting or the reverse, and the model
            // resolved the contradiction toward the bright studio look its reference images already have. Each
            // phrase now states only intensity, direction and what the light does to the subject, so no pairing can
            // contradict it - and the dim values say explicitly what stays dark, which is what lets them win against
            // a bright reference image.
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
                Body = "bright, even lighting with soft shadows"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
                WorkflowStep = "LoraCellVocabulary",
                Body = "dim, low-key lighting with soft shadows"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingOutdoorDay,
                WorkflowStep = "LoraCellVocabulary",
                Body = "flat overcast daylight, soft and even"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingOutdoorGolden,
                WorkflowStep = "LoraCellVocabulary",
                Body = "warm golden-hour sunlight from the side, long shadows"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingOutdoorNight,
                WorkflowStep = "LoraCellVocabulary",
                Body = "low ambient light at night, one practical light source, deep shadows"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingHardRim,
                WorkflowStep = "LoraCellVocabulary",
                Body = "a hard rim light along the edge of the body, the far side in deep shadow"
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
            // The frame-scoped wardrobe phrases. A cell may only claim what its framing can show: the full-body rows
            // above name garments BELOW the waist ("... and jeans"), and a "waist up" or "head and both shoulders"
            // frame cannot show them. Measured on the live plan, all ten close-up cells claimed a full outfit over a
            // frame that shows neither - five of them claiming the subject was completely unclothed. These are the
            // same states phrased for the frame that carries them.
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyWardrobeClothedClose,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a top whose neckline and shoulder seams are visible at the throat and the shoulders"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyWardrobeUnclothedClose,
                WorkflowStep = "LoraCellVocabulary",
                Body = "bare at the neckline, the shoulders and the collarbone, with no garment visible anywhere in the frame"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitCasualHalf,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a plain t-shirt"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitFormalHalf,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a buttoned shirt"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitAthleticHalf,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a fitted athletic top"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitLoungewearHalf,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a loose knit sweater"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitSleepwearHalf,
                WorkflowStep = "LoraCellVocabulary",
                Body = "wearing a thin sleeveless top"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyOutfitUnclothedHalf,
                WorkflowStep = "LoraCellVocabulary",
                Body = "bare-chested with the upper body uncovered and nothing worn on the torso"
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
                Key = LoraCellWorkflowKeys.VocabularyDistanceFar,
                WorkflowStep = "LoraCellVocabulary",
                Body = "far away"
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
                Key = LoraCellWorkflowKeys.VocabularyAngleOverShoulder,
                WorkflowStep = "LoraCellVocabulary",
                Body = "over the shoulder"
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
            },

            // B-133 — the lighting and expression PRESETS, and the clauses they are assembled with. A preset is what
            // an EDIT pass says about a photograph that already exists, which is why these are not the LoRA vocabulary
            // rows above: a cell row is a condition to shoot under, a preset detail is an instruction to change a
            // finished image, and one wording cannot do both jobs. The keys align by suffix, so a cell's own lighting
            // or expression axis selects the preset that changes its image to the same condition.
            //
            // The detail is written as a noun phrase and the ASSEMBLY supplies the verb, so one row serves both modes
            // without a second copy to keep in sync: an edit says "Relight this photograph to the following lighting:
            // <detail>", a compose step says "The scene is lit by <detail>".
            //
            // The detail carries the ACTUAL mechanics of the condition, not its name. That is the entire reason the
            // feature exists: "angry" tells the model nothing it cannot guess wrongly, while "the eyebrows pulled down
            // and drawn together with vertical creases between them, the lips pressed thin with the corners pulled
            // down" is the expression. The same holds for light - "dim" produced bright images; a single warm lamp
            // outside the frame with the far side falling into shadow is a light an edit model can actually build.
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.PreserveLighting,
                WorkflowStep = "ImagePresetClause",
                Body = "Keep the person identical - the same face, body, skin, hair and marks - and keep the pose, the camera angle, the framing, the crop, the clothing and the setting itself unchanged; only the lighting changes."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.PreserveExpression,
                WorkflowStep = "ImagePresetClause",
                Body = "Keep the person identical - the same face, body, skin, hair and marks - and keep the pose, the camera angle, the framing, the crop, the clothing, the lighting and the setting unchanged; only the facial expression changes."
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.AssemblyLightingChange,
                WorkflowStep = "ImagePresetAssembly",
                Body = "Relight this photograph to the following lighting: {Detail} {Preserve}"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.AssemblyLightingCondition,
                WorkflowStep = "ImagePresetAssembly",
                Body = "The scene is lit by {Detail}"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.AssemblyExpressionChange,
                WorkflowStep = "ImagePresetAssembly",
                Body = "Change the subject's facial expression only. The new expression is exactly this: {Detail} {Preserve}"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.AssemblyExpressionCondition,
                WorkflowStep = "ImagePresetAssembly",
                Body = "with {Detail}"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.LightingIndoorBright,
                WorkflowStep = "ImagePresetLighting",
                Body = "bright, even indoor light from a broad overhead key with soft mid-tones, gentle shading under the chin and a gentle falloff at the edges of the body, neutral white balance and the skin highlights held"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.LightingIndoorDim,
                WorkflowStep = "ImagePresetLighting",
                Body = "dim, low-key indoor light from one warm lamp just outside the frame to camera left, the near side of the face and body lit with visible detail while the far side and the background fall into deep shadow, warm white balance and clean shadow detail"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.LightingOutdoorDay,
                WorkflowStep = "ImagePresetLighting",
                Body = "flat, overcast daylight from a large bright sky, soft even illumination with almost no visible shadow edges, cool neutral white balance and soft catchlights in the eyes"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.LightingOutdoorGolden,
                WorkflowStep = "ImagePresetLighting",
                Body = "low, warm golden-hour sunlight raking in from camera right, long soft shadows across the body, warm highlights on the hair and skin and the surroundings falling a stop darker than the subject"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.LightingOutdoorNight,
                WorkflowStep = "ImagePresetLighting",
                Body = "night light from a single small practical source at mid-distance, the subject lit mainly by that one light with its fall-off visible across the body, the surroundings almost black and only cool ambient fill on the shadow side"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.LightingHardRim,
                WorkflowStep = "ImagePresetLighting",
                Body = "a single hard light raking across the edge of the body, a bright rim following the contour, the far side of the body in deep unlit shadow, high contrast and the surroundings falling away to darkness"
            },
            // HOW FLUSH IS WRITTEN, and why it is written this way. Reported 2026-09-27: renders "coming out looking
            // sun burnt" on the flush presets. The cause is in the wording rather than the model's mood - a diffusion
            // model renders VISIBLE COLOUR literally, so every part of the old phrasing made the red wash worse:
            //   - "flush" already MEANS a red wash; "heavy", "deep" and "warm" raise its chroma without raising the
            //     emotional read at all;
            //   - "across the cheeks" reads as a band across the face, not a localised blush;
            //   - naming the NOSE as red is the sunburn line itself (and the cold, and the drunk read);
            //   - "the whole face" invites the model to extend it everywhere.
            // So the rules are: the nose is never COLOURED (where heat belongs on it, it is sheen); colour is pinned to
            // the apples of the cheeks rather than "the cheeks"; the rest of the face is explicitly told to keep its own
            // skin tone, which is the guard that stops the spread; and sweat carries the heat read, which cannot
            // sunburn because it has no colour.
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionNeutral,
                WorkflowStep = "ImagePresetExpression",
                Body = "the face relaxed and symmetrical, the eyes open at their natural width, the brows level, the lips closed and resting together and the jaw loose"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionSmiling,
                WorkflowStep = "ImagePresetExpression",
                Body = "the mouth corners pulled up and slightly back, the cheeks raised so the smile lines at the outer eye corners show, the eyes slightly narrowed and warm and the lips closed or only just parted"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionLaughing,
                WorkflowStep = "ImagePresetExpression",
                Body = "the mouth open wide enough to show the upper teeth, the cheeks strongly raised, the eyes narrowed almost closed with creases at the outer corners and the head tipped back a little"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionSurprised,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyebrows raised high and arched, the eyes wide with the upper eyelids lifted so the whole iris shows, the mouth open in a rounded shape and the jaw dropped"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionSerious,
                WorkflowStep = "ImagePresetExpression",
                Body = "the brows level but drawn slightly down, the eyes steady and open, the lips closed and pressed evenly, the mouth corners level and the jaw set"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionSensual,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyes half-lidded with a heavy downward gaze, the brows relaxed and lifted at the inner ends, the lips softly parted and slightly full, the mouth corners relaxed and a soft rose on the apples of the cheeks, the rest of the face keeping its own even skin tone"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionAngry,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyebrows pulled down and drawn together with vertical creases between them, the upper eyelids lowered and the lower lids tense, the eyes narrowed on the camera, the lips pressed thin with the corners pulled down, the nostrils slightly flared and the jaw set with the chin pushed forward"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionSad,
                WorkflowStep = "ImagePresetExpression",
                Body = "the inner ends of the eyebrows raised and drawn together with creases above the nose, the upper eyelids drooping so the gaze falls downward, the mouth corners pulled down, the lower lip pushed out a little and the chin creased"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionAfraid,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyebrows raised and drawn together in the middle, the eyes wide with the whites showing above the iris, the lips stretched back and slightly parted and the jaw tense and pulled back"
            },
            // Reported 2026-09-27: "disgusted contorted the face a bit, it was close just the bottom lip and cheek look
            // wrong". Two causes in the old phrasing, and both are asymmetry the model had to invent or over-apply:
            //   - "the cheeks pushed up" raised BOTH cheeks, which is a snarl read from the wrong expression and is what
            //     distorted the cheek; the crinkle belongs to the raised side only.
            //   - the lower lip was never mentioned, so the model followed the raised upper lip with it. Naming the lower
            //     lip as deliberately NOT following is what keeps the mouth from contorting.
            // The sneer is also pinned to ONE stated side ("on that same side") because the old text said "one side" and
            // "one mouth corner" separately, leaving the model to choose - and choosing differently for each distorts the
            // mouth.
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionDisgusted,
                WorkflowStep = "ImagePresetExpression",
                Body = "the nose wrinkled with creases across the bridge, the eyebrows lowered and drawn together, the upper lip raised on one side only enough to expose the teeth on that side, the mouth corner on that same side pulled up with it, the lower lip kept relaxed and slightly pushed down rather than following the sneer, the cheek on the raised side crinkled while the other stays smooth, the eyes narrowed and the head turned slightly away"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionCrying,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyes narrowed and glossy with tears pooled on the lower lids, the eyebrows pulled up at the inner ends, the lower lid reddened, the mouth open in a downturned shape with the lower lip trembling, the cheeks wet and the chin creased"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionAroused,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyes half-closed and heavy with the pupils dilated, the gaze dropped and steady, the brows relaxed and lifted at the inner ends, the lips parted and slightly swollen with the lower lip drawn in between the teeth, the jaw loose, the mouth corners slack and a soft rose on the apples of the cheeks, the rest of the face keeping its own even skin tone"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionOrgasm,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyes rolled up and fluttering behind barely open lids, the brows drawn together and lifted at the inner ends, the jaw dropped slack with the mouth held open wide, the nostrils flared, a fine sheen of sweat on the cheeks and throat and the head tipped back"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionGoofy,
                WorkflowStep = "ImagePresetExpression",
                Body = "one eyebrow cocked high while the other drops, the eyes squinting unevenly, the lips pulled to one side in a lopsided grin with the teeth showing, the jaw pushed forward and the tongue tucked into the cheek"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionTongueOut,
                WorkflowStep = "ImagePresetExpression",
                Body = "the mouth open wide with the jaw dropped low, the tongue pushed out and hanging over the lower lip, the eyes wide and bright or narrowed with mischief, the brows raised and the nostrils slightly flared"
            },
            // Deliberately single-valued, and the pupils are left alone by instruction: the crossed eyes are the read
            // here, so naming the irises as still visible is what keeps the model from answering with blank whites.
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionAhegao,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyes crossed inward so the pupils converge toward the bridge of the nose with the irises still visible in both, the eyelids held wide, the mouth open wide and completely slack, the tongue pushed out and lolling over the lower lip, the brows raised, a fine sheen of sweat over the cheeks and the tip of the nose and the head tipped back a little"
            },
            // The peak read: everything the arousal preset has, thrown all the way — the gaze gone, the jaw fully
            // open, the head back and the face slack. No breath or sound cue, because neither is visible in a still.
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionOrgasmIntense,
                WorkflowStep = "ImagePresetExpression",
                Body = "the eyes rolled back so only the whites show beneath lids that flutter half-closed, the gaze unfocused and turned up and inward, the brows drawn together and lifted, the jaw dropped fully open with the lips slack and glistening, the head tipped back with the neck extended, a fine sheen of sweat high on the cheeks and the whole face gone slack"
            },
            // The flirtation set. Each one is written against the neighbour it would otherwise collapse into: pouty has
            // to exclude the sad read (level lowered brows and the gaze UP at the camera, where sad raises the inner
            // ends and drops the gaze), and mischievous has to exclude the goofy read (restraint - a closed-lipped
            // smirk and a sidelong glance, where goofy shows teeth, pushes the jaw forward and clowns).
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionPouty,
                WorkflowStep = "ImagePresetExpression",
                Body = "the lower lip pushed out and turned down so it rolls over the upper lip, the mouth small and drawn together, the chin lifted slightly, the brows level and slightly lowered, the eyes lifted to look up at the camera through the lashes and the cheeks kept still"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionEager,
                WorkflowStep = "ImagePresetExpression",
                Body = "the brows raised and lifted so the whole iris shows, the eyes wide and fixed on the camera, the cheeks lifted high, the mouth open in a quick open smile that shows the upper teeth, the head tipped forward and the chin raised"
            },
            new ImageWorkflowPromptTemplate
            {
                Key = ImagePresetKeys.ExpressionMischievous,
                WorkflowStep = "ImagePresetExpression",
                Body = "one eyebrow raised in a slow arch while the other stays level, the eyes narrowed and half-lidded and looking off to the side away from the camera, one mouth corner pulled up into a closed-lip smirk, the head tilted a few degrees and the lips pressed together over a hint of teeth"
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
        LoraCellModelId = reader.IsDBNull(19) ? null : reader.GetString(19),
        RegionGrowMaskBy = reader.GetInt32(20),
        RegionFeatherPixels = reader.GetInt32(21)
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

        // The region mask's geometry, bounded by what the graph can honour: the host's encode node accepts a grow of
        // 0-64, and a negative feather is not a softening but a mistake. Both are persisted configuration with no code
        // default, so an unset or out-of-range value fails fast naming the key.
        //
        // A feather of ZERO is accepted HERE and refused by a confined edit (MediaEditRegionOperation.Validate). The
        // row is shared with flows that never confine anything (front/body/capture settings round-trip it), and the
        // defect CASE-25 fixed is in the edit, not in the row - so the refusal belongs where an edit is attempted.
        if (settings.RegionGrowMaskBy is not { } regionGrowMaskBy || regionGrowMaskBy is < 0 or > 64)
        {
            throw new InvalidOperationException(
                "RegionGrowMaskBy is required and must be between 0 and 64; it has no code default and must be configured.");
        }

        if (settings.RegionFeatherPixels is not { } regionFeatherPixels || regionFeatherPixels < 0)
        {
            throw new InvalidOperationException(
                "RegionFeatherPixels is required and must not be negative; it has no code default and must be configured.");
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
