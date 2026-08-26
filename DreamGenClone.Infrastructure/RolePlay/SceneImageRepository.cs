using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for the scene-image pipeline (editable prompt records + rendered image
/// records). Self-contained schema creation mirrors the other RP repositories.
/// </summary>
public sealed class SceneImageRepository : ISceneImageRepository
{
    private readonly string _connectionString;

    public SceneImageRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    // ---------------- Beat analysis records ----------------

    public async Task UpsertBeatAnalysisAsync(SceneImageBeatAnalysisRecord analysis, CancellationToken cancellationToken = default)
    {
        ValidateBeatAnalysis(analysis);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneImageBeatAnalyses (
                Id, SessionId, TurnId, AnchorInteractionId, Status, BeatsJson, InputSnapshotJson,
                RawModelResponse, ReasoningContent, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $sessionId, $turnId, $anchorInteractionId, $status, $beatsJson, $inputSnapshotJson,
                $rawModelResponse, $reasoningContent, $modelIdentifier, $errorMessage, $createdUtc, $updatedUtc)
            ON CONFLICT(SessionId, TurnId) DO UPDATE SET
                Id = excluded.Id,
                AnchorInteractionId = excluded.AnchorInteractionId,
                Status = excluded.Status,
                BeatsJson = excluded.BeatsJson,
                InputSnapshotJson = excluded.InputSnapshotJson,
                RawModelResponse = excluded.RawModelResponse,
                ReasoningContent = excluded.ReasoningContent,
                ModelIdentifier = excluded.ModelIdentifier,
                ErrorMessage = excluded.ErrorMessage,
                CreatedUtc = excluded.CreatedUtc,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", analysis.Id);
        command.Parameters.AddWithValue("$sessionId", analysis.SessionId.Trim());
        command.Parameters.AddWithValue("$turnId", analysis.TurnId.Trim());
        command.Parameters.AddWithValue("$anchorInteractionId", analysis.AnchorInteractionId.Trim());
        command.Parameters.AddWithValue("$status", analysis.Status.ToString());
        command.Parameters.AddWithValue("$beatsJson", analysis.BeatsJson);
        command.Parameters.AddWithValue("$inputSnapshotJson", analysis.InputSnapshotJson);
        command.Parameters.AddWithValue("$rawModelResponse", (object?)analysis.RawModelResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$reasoningContent", (object?)analysis.ReasoningContent ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelIdentifier", (object?)analysis.ModelIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)analysis.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", analysis.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", analysis.UpdatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneImageBeatAnalysisRecord?> GetBeatAnalysisByTurnAsync(
        string sessionId, string turnId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(turnId))
        {
            throw new InvalidOperationException("Session id and turn id are required to load scene image beat analysis.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, TurnId, AnchorInteractionId, Status, BeatsJson, InputSnapshotJson,
                     RawModelResponse, ReasoningContent, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc
            FROM SceneImageBeatAnalyses
            WHERE SessionId = $sessionId AND TurnId = $turnId;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$turnId", turnId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SceneImageBeatAnalysisRecord
        {
            Id = reader.GetString(0),
            SessionId = reader.GetString(1),
            TurnId = reader.GetString(2),
            AnchorInteractionId = reader.GetString(3),
            Status = ParseEnum<SceneImageBeatAnalysisStatus>(reader.GetString(4), reader.GetString(1), reader.GetString(3), "SceneImageBeatAnalyses"),
            BeatsJson = reader.GetString(5),
            InputSnapshotJson = reader.GetString(6),
            RawModelResponse = reader.IsDBNull(7) ? null : reader.GetString(7),
            ReasoningContent = reader.IsDBNull(8) ? null : reader.GetString(8),
            ModelIdentifier = reader.IsDBNull(9) ? null : reader.GetString(9),
            ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedUtc = ParseUtc(reader.GetString(11), reader.GetString(1), reader.GetString(3), "CreatedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(12), reader.GetString(1), reader.GetString(3), "UpdatedUtc")
        };
    }

    // ---------------- Prompt records ----------------

    public async Task UpsertPromptAsync(SceneImagePromptRecord prompt, CancellationToken cancellationToken = default)
    {
        ValidatePrompt(prompt);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneImagePrompts (
                Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
                Status, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $sessionId, $interactionId, $beatAnalysisId, $beatSnapshotJson, $pov, $settingsJson, $inputExcerpt, $outputPrompt, $refineInstruction,
                $status, $modelIdentifier, $errorMessage, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                SessionId = excluded.SessionId,
                InteractionId = excluded.InteractionId,
                BeatAnalysisId = excluded.BeatAnalysisId,
                BeatSnapshotJson = excluded.BeatSnapshotJson,
                Pov = excluded.Pov,
                SettingsJson = excluded.SettingsJson,
                InputExcerpt = excluded.InputExcerpt,
                OutputPrompt = excluded.OutputPrompt,
                RefineInstruction = excluded.RefineInstruction,
                Status = excluded.Status,
                ModelIdentifier = excluded.ModelIdentifier,
                ErrorMessage = excluded.ErrorMessage,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", prompt.Id);
        command.Parameters.AddWithValue("$sessionId", prompt.SessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", prompt.InteractionId.Trim());
        command.Parameters.AddWithValue("$beatAnalysisId", prompt.BeatAnalysisId);
        command.Parameters.AddWithValue("$beatSnapshotJson", prompt.BeatSnapshotJson);
        command.Parameters.AddWithValue("$pov", prompt.Pov);
        command.Parameters.AddWithValue("$settingsJson", prompt.SettingsJson);
        command.Parameters.AddWithValue("$inputExcerpt", prompt.InputExcerpt);
        command.Parameters.AddWithValue("$outputPrompt", prompt.OutputPrompt);
        command.Parameters.AddWithValue("$refineInstruction", (object?)prompt.RefineInstruction ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", prompt.Status.ToString());
        command.Parameters.AddWithValue("$modelIdentifier", (object?)prompt.ModelIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)prompt.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", prompt.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", prompt.UpdatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneImagePromptRecord?> GetPromptAsync(string promptId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(promptId))
        {
            throw new InvalidOperationException("Prompt id is required to load a scene image prompt.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
                   Status, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc
            FROM SceneImagePrompts
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", promptId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadPrompt(reader);
    }

    public async Task<SceneImagePromptRecord?> GetLatestPromptAsync(
        string sessionId, string interactionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(interactionId))
        {
            throw new InvalidOperationException("Session id and interaction id are required to load the latest scene image prompt.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
                   Status, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc
            FROM SceneImagePrompts
            WHERE SessionId = $sessionId AND InteractionId = $interactionId
            ORDER BY UpdatedUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", interactionId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadPrompt(reader);
    }

    public async Task<SceneImagePromptRecord?> GetLatestCompletedPromptAsync(
        string sessionId,
        string interactionId,
        string beatAnalysisId,
        string beatId,
        string pov,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(interactionId)
            || string.IsNullOrWhiteSpace(beatAnalysisId)
            || string.IsNullOrWhiteSpace(beatId)
            || string.IsNullOrWhiteSpace(pov))
        {
            throw new InvalidOperationException("Session, interaction, beat analysis, beat, and POV are required to load a generated scene image prompt.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
                   Status, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc
            FROM SceneImagePrompts
            WHERE SessionId = $sessionId
              AND InteractionId = $interactionId
              AND BeatAnalysisId = $beatAnalysisId
              AND json_extract(BeatSnapshotJson, '$.beatId') = $beatId
              AND Pov = $pov COLLATE NOCASE
              AND Status = 'Complete'
            ORDER BY UpdatedUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", interactionId.Trim());
        command.Parameters.AddWithValue("$beatAnalysisId", beatAnalysisId.Trim());
        command.Parameters.AddWithValue("$beatId", beatId.Trim());
        command.Parameters.AddWithValue("$pov", pov.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPrompt(reader) : null;
    }

    public async Task UpdatePromptOutputAsync(string promptId, string outputPrompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(promptId))
        {
            throw new InvalidOperationException("Prompt id is required to update an editable scene image prompt.");
        }
        if (string.IsNullOrWhiteSpace(outputPrompt))
        {
            throw new InvalidOperationException("A non-empty prompt is required to update an editable scene image prompt.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneImagePrompts
            SET OutputPrompt = $outputPrompt, UpdatedUtc = $updatedUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", promptId.Trim());
        command.Parameters.AddWithValue("$outputPrompt", outputPrompt.Trim());
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0)
        {
            throw new InvalidOperationException($"Scene image prompt record '{promptId}' was not found.");
        }
    }

    // ---------------- Image records ----------------

    public async Task InsertImageAsync(SceneImageRecord image, CancellationToken cancellationToken = default)
    {
        ValidateImage(image);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO SceneImages (
                Id, SessionId, InteractionId, PromptRecordId, PromptSnapshot, Status,
                Operation, SourceImageId, EditSessionId, EditCompilationAttemptId, EditPromptRevisionId, EditIntentSnapshot, EditCompilerProvenanceJson,
                FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style, SettingsJson,
                ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId)
            VALUES (
                $id, $sessionId, $interactionId, $promptRecordId, $promptSnapshot, $status,
                $operation, $sourceImageId, $editSessionId, $editCompilationAttemptId, $editPromptRevisionId, $editIntentSnapshot, $editCompilerProvenanceJson,
                $fileRelativePath, $modelIdentifier, $providerName, $contentPolicy, $imageSize, $style, $settingsJson,
                $errorMessage, $regenerateOfId, $beatId, $pov, $createdUtc, $startedUtc, $completedUtc, $updatedUtc, $renderMode, $identityPackId);
            """;
        command.Parameters.AddWithValue("$id", image.Id);
        command.Parameters.AddWithValue("$sessionId", image.SessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", image.InteractionId.Trim());
        command.Parameters.AddWithValue("$promptRecordId", image.PromptRecordId.Trim());
        command.Parameters.AddWithValue("$promptSnapshot", image.PromptSnapshot);
        command.Parameters.AddWithValue("$status", image.Status.ToString());
        command.Parameters.AddWithValue("$operation", image.Operation.ToString());
        command.Parameters.AddWithValue("$sourceImageId", (object?)image.SourceImageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$editSessionId", (object?)image.EditSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$editCompilationAttemptId", (object?)image.EditCompilationAttemptId ?? DBNull.Value);
        command.Parameters.AddWithValue("$editPromptRevisionId", (object?)image.EditPromptRevisionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$editIntentSnapshot", (object?)image.EditIntentSnapshot ?? DBNull.Value);
        command.Parameters.AddWithValue("$editCompilerProvenanceJson", (object?)image.EditCompilerProvenanceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileRelativePath", (object?)image.FileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelIdentifier", (object?)image.ModelIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerName", (object?)image.ProviderName ?? DBNull.Value);
        command.Parameters.AddWithValue("$contentPolicy", image.ContentPolicy.ToString());
        command.Parameters.AddWithValue("$imageSize", (object?)image.ImageSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$style", (object?)image.Style ?? DBNull.Value);
        command.Parameters.AddWithValue("$settingsJson", image.SettingsJson);
        command.Parameters.AddWithValue("$errorMessage", (object?)image.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$regenerateOfId", (object?)image.RegenerateOfId ?? DBNull.Value);
        command.Parameters.AddWithValue("$beatId", (object?)image.BeatId ?? DBNull.Value);
        command.Parameters.AddWithValue("$pov", (object?)image.Pov ?? DBNull.Value);
        command.Parameters.AddWithValue("$renderMode", image.RenderMode.ToString());
        command.Parameters.AddWithValue("$identityPackId", (object?)image.IdentityPackId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", image.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$startedUtc", image.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", image.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", image.UpdatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneImageRecord?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            throw new InvalidOperationException("Image id is required to load a scene image.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, PromptRecordId, PromptSnapshot, Status,
                 Operation, SourceImageId, EditSessionId, EditCompilationAttemptId, EditPromptRevisionId, EditIntentSnapshot, EditCompilerProvenanceJson,
                 FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style, SettingsJson,
                   ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId
            FROM SceneImages
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", imageId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadImage(reader);
    }

    public async Task<IReadOnlyList<SceneImageRecord>> ListImagesByInteractionAsync(
        string sessionId, string interactionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(interactionId))
        {
            throw new InvalidOperationException("Session id and interaction id are required to list scene images.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, PromptRecordId, PromptSnapshot, Status,
                 Operation, SourceImageId, EditSessionId, EditCompilationAttemptId, EditPromptRevisionId, EditIntentSnapshot, EditCompilerProvenanceJson,
                 FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style, SettingsJson,
                   ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId
            FROM SceneImages
            WHERE SessionId = $sessionId AND InteractionId = $interactionId
            ORDER BY CreatedUtc DESC;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", interactionId.Trim());

        var results = new List<SceneImageRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadImage(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<SceneImageRecord>> ListImagesBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to list scene images.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, PromptRecordId, PromptSnapshot, Status,
                 Operation, SourceImageId, EditSessionId, EditCompilationAttemptId, EditPromptRevisionId, EditIntentSnapshot, EditCompilerProvenanceJson,
                 FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style, SettingsJson,
                   ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId
            FROM SceneImages
            WHERE SessionId = $sessionId
            ORDER BY CreatedUtc DESC;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());

        var results = new List<SceneImageRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadImage(reader));
        }

        return results;
    }

    public async Task<Dictionary<string, int>> CountImagesByInteractionAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to count scene images.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT InteractionId, COUNT(*)
            FROM SceneImages
            WHERE SessionId = $sessionId AND Status = $status
            GROUP BY InteractionId;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$status", SceneImageStatus.Complete.ToString());

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    public async Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            throw new InvalidOperationException("Image id is required to delete a scene image.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var childReferenceCommand = connection.CreateCommand();
        childReferenceCommand.CommandText = "SELECT COUNT(*) FROM SceneImages WHERE SourceImageId = $id;";
        childReferenceCommand.Parameters.AddWithValue("$id", imageId.Trim());
        if (Convert.ToInt64(await childReferenceCommand.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            throw new InvalidOperationException($"Cannot delete scene image '{imageId}' because an edited image references it as its source.");
        }

        await using var editSessionTableCommand = connection.CreateCommand();
        editSessionTableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SceneImageEditSessions';";
        if (Convert.ToInt64(await editSessionTableCommand.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            await using var editSessionReferenceCommand = connection.CreateCommand();
            editSessionReferenceCommand.CommandText = "SELECT COUNT(*) FROM SceneImageEditSessions WHERE SourceImageId = $id;";
            editSessionReferenceCommand.Parameters.AddWithValue("$id", imageId.Trim());
            if (Convert.ToInt64(await editSessionReferenceCommand.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                throw new InvalidOperationException($"Cannot delete scene image '{imageId}' because an edit session references it as its source.");
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SceneImages WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", imageId.Trim());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ---------------- Readers ----------------

    private static SceneImagePromptRecord ReadPrompt(SqliteDataReader reader)
    {
        var sessionId = reader.GetString(1);
        var interactionId = reader.GetString(2);
        return new SceneImagePromptRecord
        {
            Id = reader.GetString(0),
            SessionId = sessionId,
            InteractionId = interactionId,
            BeatAnalysisId = reader.GetString(3),
            BeatSnapshotJson = reader.GetString(4),
            Pov = reader.GetString(5),
            SettingsJson = reader.GetString(6),
            InputExcerpt = reader.GetString(7),
            OutputPrompt = reader.GetString(8),
            RefineInstruction = reader.IsDBNull(9) ? null : reader.GetString(9),
            Status = ParseEnum<SceneImagePromptStatus>(reader.GetString(10), sessionId, interactionId, "SceneImagePrompts"),
            ModelIdentifier = reader.IsDBNull(11) ? null : reader.GetString(11),
            ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
            CreatedUtc = ParseUtc(reader.GetString(13), sessionId, interactionId, "CreatedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(14), sessionId, interactionId, "UpdatedUtc")
        };
    }

    private static SceneImageRecord ReadImage(SqliteDataReader reader)
    {
        var sessionId = reader.GetString(1);
        var interactionId = reader.GetString(2);
        return new SceneImageRecord
        {
            Id = reader.GetString(0),
            SessionId = sessionId,
            InteractionId = interactionId,
            PromptRecordId = reader.GetString(3),
            PromptSnapshot = reader.GetString(4),
            Status = ParseEnum<SceneImageStatus>(reader.GetString(5), sessionId, interactionId, "SceneImages"),
            Operation = ParseEnum<SceneImageOperation>(reader.GetString(6), sessionId, interactionId, "SceneImages"),
            SourceImageId = reader.IsDBNull(7) ? null : reader.GetString(7),
            EditSessionId = reader.IsDBNull(8) ? null : reader.GetString(8),
            EditCompilationAttemptId = reader.IsDBNull(9) ? null : reader.GetString(9),
            EditPromptRevisionId = reader.IsDBNull(10) ? null : reader.GetString(10),
            EditIntentSnapshot = reader.IsDBNull(11) ? null : reader.GetString(11),
            EditCompilerProvenanceJson = reader.IsDBNull(12) ? null : reader.GetString(12),
            FileRelativePath = reader.IsDBNull(13) ? null : reader.GetString(13),
            ModelIdentifier = reader.IsDBNull(14) ? null : reader.GetString(14),
            ProviderName = reader.IsDBNull(15) ? null : reader.GetString(15),
            ContentPolicy = ParseEnum<ImageContentPolicy>(reader.GetString(16), sessionId, interactionId, "SceneImages"),
            ImageSize = reader.IsDBNull(17) ? null : reader.GetString(17),
            Style = reader.IsDBNull(18) ? null : reader.GetString(18),
            SettingsJson = reader.IsDBNull(19) ? "{}" : reader.GetString(19),
            ErrorMessage = reader.IsDBNull(20) ? null : reader.GetString(20),
            RegenerateOfId = reader.IsDBNull(21) ? null : reader.GetString(21),
            BeatId = reader.IsDBNull(22) ? null : reader.GetString(22),
            Pov = reader.IsDBNull(23) ? null : reader.GetString(23),
            CreatedUtc = ParseUtc(reader.GetString(24), sessionId, interactionId, "CreatedUtc"),
            StartedUtc = reader.IsDBNull(25) ? null : ParseUtc(reader.GetString(25), sessionId, interactionId, "StartedUtc"),
            CompletedUtc = reader.IsDBNull(26) ? null : ParseUtc(reader.GetString(26), sessionId, interactionId, "CompletedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(27), sessionId, interactionId, "UpdatedUtc"),
            RenderMode = ParseEnum<SceneImageRenderMode>(reader.GetString(28), sessionId, interactionId, "SceneImages"),
            IdentityPackId = reader.IsDBNull(29) ? null : reader.GetString(29)
        };
    }

    // ---------------- Schema ----------------

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneImageBeatAnalyses (
                Id                  TEXT PRIMARY KEY,
                SessionId           TEXT NOT NULL,
                TurnId              TEXT NOT NULL,
                AnchorInteractionId TEXT NOT NULL,
                Status              TEXT NOT NULL,
                BeatsJson           TEXT NOT NULL,
                InputSnapshotJson   TEXT NOT NULL,
                RawModelResponse    TEXT NULL,
                ReasoningContent    TEXT NULL,
                ModelIdentifier     TEXT NULL,
                ErrorMessage        TEXT NULL,
                CreatedUtc          TEXT NOT NULL,
                UpdatedUtc          TEXT NOT NULL,
                UNIQUE (SessionId, TurnId)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImageBeatAnalyses_SessionTurn
                ON SceneImageBeatAnalyses (SessionId, TurnId);

            CREATE TABLE IF NOT EXISTS SceneImagePrompts (
                Id               TEXT PRIMARY KEY,
                SessionId        TEXT NOT NULL,
                InteractionId    TEXT NOT NULL,
                BeatAnalysisId   TEXT NOT NULL DEFAULT '',
                BeatSnapshotJson TEXT NOT NULL DEFAULT '{}',
                Pov              TEXT NOT NULL DEFAULT '',
                SettingsJson     TEXT NOT NULL,
                InputExcerpt     TEXT NOT NULL,
                OutputPrompt     TEXT NOT NULL,
                RefineInstruction TEXT NULL,
                Status           TEXT NOT NULL,
                ModelIdentifier  TEXT NULL,
                ErrorMessage     TEXT NULL,
                CreatedUtc       TEXT NOT NULL,
                UpdatedUtc       TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImagePrompts_SessionInteraction
                ON SceneImagePrompts (SessionId, InteractionId);

            CREATE TABLE IF NOT EXISTS SceneImages (
                Id               TEXT PRIMARY KEY,
                SessionId        TEXT NOT NULL,
                InteractionId    TEXT NOT NULL,
                PromptRecordId   TEXT NOT NULL,
                PromptSnapshot   TEXT NOT NULL,
                Status           TEXT NOT NULL,
                Operation        TEXT NOT NULL DEFAULT 'Generate',
                SourceImageId    TEXT NULL,
                EditSessionId    TEXT NULL,
                EditCompilationAttemptId TEXT NULL,
                EditPromptRevisionId TEXT NULL,
                EditIntentSnapshot TEXT NULL,
                EditCompilerProvenanceJson TEXT NULL,
                FileRelativePath TEXT NULL,
                ModelIdentifier  TEXT NULL,
                ProviderName     TEXT NULL,
                ContentPolicy    TEXT NOT NULL,
                ImageSize        TEXT NULL,
                Style            TEXT NULL,
                SettingsJson     TEXT NOT NULL DEFAULT '{}',
                ErrorMessage     TEXT NULL,
                RegenerateOfId   TEXT NULL,
                BeatId           TEXT NULL,
                Pov              TEXT NULL,
                CreatedUtc       TEXT NOT NULL,
                StartedUtc       TEXT NULL,
                CompletedUtc     TEXT NULL,
                UpdatedUtc       TEXT NOT NULL,
                RenderMode       TEXT NOT NULL DEFAULT 'PromptOnly',
                IdentityPackId   TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImages_Session
                ON SceneImages (SessionId);
            CREATE INDEX IF NOT EXISTS IX_SceneImages_Interaction
                ON SceneImages (SessionId, InteractionId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsurePromptBeatColumnsAsync(connection, cancellationToken);
        await EnsureImageEditColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsurePromptBeatColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (name, sql) in new[]
        {
            ("ReasoningContent", "ALTER TABLE SceneImageBeatAnalyses ADD COLUMN ReasoningContent TEXT NULL")
        })
        {
            await using var check = connection.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SceneImageBeatAnalyses') WHERE name = '{name}'";
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0;
            if (exists) continue;
            await using var alter = connection.CreateCommand();
            alter.CommandText = sql;
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (name, sql) in new[]
        {
            ("BeatAnalysisId", "ALTER TABLE SceneImagePrompts ADD COLUMN BeatAnalysisId TEXT NOT NULL DEFAULT ''"),
            ("BeatSnapshotJson", "ALTER TABLE SceneImagePrompts ADD COLUMN BeatSnapshotJson TEXT NOT NULL DEFAULT '{}'"),
            ("Pov", "ALTER TABLE SceneImagePrompts ADD COLUMN Pov TEXT NOT NULL DEFAULT ''")
        })
        {
            await using var check = connection.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SceneImagePrompts') WHERE name = '{name}'";
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0;
            if (exists) continue;
            await using var alter = connection.CreateCommand();
            alter.CommandText = sql;
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureImageEditColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (name, sql) in new[]
        {
            ("Operation", "ALTER TABLE SceneImages ADD COLUMN Operation TEXT NOT NULL DEFAULT 'Generate'"),
            ("SourceImageId", "ALTER TABLE SceneImages ADD COLUMN SourceImageId TEXT NULL"),
            ("EditSessionId", "ALTER TABLE SceneImages ADD COLUMN EditSessionId TEXT NULL"),
            ("EditCompilationAttemptId", "ALTER TABLE SceneImages ADD COLUMN EditCompilationAttemptId TEXT NULL"),
            ("EditPromptRevisionId", "ALTER TABLE SceneImages ADD COLUMN EditPromptRevisionId TEXT NULL"),
            ("EditIntentSnapshot", "ALTER TABLE SceneImages ADD COLUMN EditIntentSnapshot TEXT NULL"),
            ("EditCompilerProvenanceJson", "ALTER TABLE SceneImages ADD COLUMN EditCompilerProvenanceJson TEXT NULL"),
            ("RenderMode", "ALTER TABLE SceneImages ADD COLUMN RenderMode TEXT NOT NULL DEFAULT 'PromptOnly'"),
            ("IdentityPackId", "ALTER TABLE SceneImages ADD COLUMN IdentityPackId TEXT NULL")
        })
        {
            await using var check = connection.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SceneImages') WHERE name = '{name}'";
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0;
            if (exists) continue;
            await using var alter = connection.CreateCommand();
            alter.CommandText = sql;
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // ---------------- Validation / parsing ----------------

    private static void ValidateBeatAnalysis(SceneImageBeatAnalysisRecord analysis)
    {
        if (string.IsNullOrWhiteSpace(analysis.SessionId))
            throw new InvalidOperationException("Scene image beat analysis requires SessionId.");
        if (string.IsNullOrWhiteSpace(analysis.TurnId))
            throw new InvalidOperationException("Scene image beat analysis requires TurnId.");
        if (string.IsNullOrWhiteSpace(analysis.AnchorInteractionId))
            throw new InvalidOperationException("Scene image beat analysis requires AnchorInteractionId.");
        if (string.IsNullOrWhiteSpace(analysis.BeatsJson))
            throw new InvalidOperationException("Scene image beat analysis requires BeatsJson.");
        if (string.IsNullOrWhiteSpace(analysis.InputSnapshotJson))
            throw new InvalidOperationException("Scene image beat analysis requires InputSnapshotJson.");
    }

    private static void ValidatePrompt(SceneImagePromptRecord prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt.SessionId))
            throw new InvalidOperationException("Scene image prompt requires SessionId.");
        if (string.IsNullOrWhiteSpace(prompt.InteractionId))
            throw new InvalidOperationException("Scene image prompt requires InteractionId.");
        if (string.IsNullOrWhiteSpace(prompt.BeatAnalysisId))
            throw new InvalidOperationException("Scene image prompt requires BeatAnalysisId.");
        if (string.IsNullOrWhiteSpace(prompt.BeatSnapshotJson))
            throw new InvalidOperationException("Scene image prompt requires BeatSnapshotJson.");
        if (string.IsNullOrWhiteSpace(prompt.Pov))
            throw new InvalidOperationException("Scene image prompt requires Pov.");
    }

    private static void ValidateImage(SceneImageRecord image)
    {
        if (string.IsNullOrWhiteSpace(image.SessionId))
            throw new InvalidOperationException("Scene image requires SessionId.");
        if (string.IsNullOrWhiteSpace(image.InteractionId))
            throw new InvalidOperationException("Scene image requires InteractionId.");
        if (string.IsNullOrWhiteSpace(image.PromptRecordId))
            throw new InvalidOperationException("Scene image requires PromptRecordId.");
        if (string.IsNullOrWhiteSpace(image.PromptSnapshot))
            throw new InvalidOperationException("Scene image requires a non-empty PromptSnapshot.");
        if (image.Operation == SceneImageOperation.Edit && string.IsNullOrWhiteSpace(image.SourceImageId))
            throw new InvalidOperationException("An edited scene image requires a SourceImageId.");
    }

    private static T ParseEnum<T>(string value, string sessionId, string interactionId, string table)
        where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"{table} row for session '{sessionId}' interaction '{interactionId}' has invalid value '{value}' for {typeof(T).Name}.");
    }

    private static DateTime ParseUtc(string value, string sessionId, string interactionId, string columnName)
    {
        if (DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Scene image row for session '{sessionId}' interaction '{interactionId}' has invalid {columnName} value '{value}'.");
    }
}
