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
                Id, SessionId, InteractionId, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
                Status, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $sessionId, $interactionId, $settingsJson, $inputExcerpt, $outputPrompt, $refineInstruction,
                $status, $modelIdentifier, $errorMessage, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                SessionId = excluded.SessionId,
                InteractionId = excluded.InteractionId,
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
            SELECT Id, SessionId, InteractionId, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
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
            SELECT Id, SessionId, InteractionId, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
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
                FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style,
                ErrorMessage, RegenerateOfId, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
            VALUES (
                $id, $sessionId, $interactionId, $promptRecordId, $promptSnapshot, $status,
                $fileRelativePath, $modelIdentifier, $providerName, $contentPolicy, $imageSize, $style,
                $errorMessage, $regenerateOfId, $createdUtc, $startedUtc, $completedUtc, $updatedUtc);
            """;
        command.Parameters.AddWithValue("$id", image.Id);
        command.Parameters.AddWithValue("$sessionId", image.SessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", image.InteractionId.Trim());
        command.Parameters.AddWithValue("$promptRecordId", image.PromptRecordId.Trim());
        command.Parameters.AddWithValue("$promptSnapshot", image.PromptSnapshot);
        command.Parameters.AddWithValue("$status", image.Status.ToString());
        command.Parameters.AddWithValue("$fileRelativePath", (object?)image.FileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelIdentifier", (object?)image.ModelIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerName", (object?)image.ProviderName ?? DBNull.Value);
        command.Parameters.AddWithValue("$contentPolicy", image.ContentPolicy.ToString());
        command.Parameters.AddWithValue("$imageSize", (object?)image.ImageSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$style", (object?)image.Style ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)image.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$regenerateOfId", (object?)image.RegenerateOfId ?? DBNull.Value);
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
                   FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style,
                   ErrorMessage, RegenerateOfId, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
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
                   FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style,
                   ErrorMessage, RegenerateOfId, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
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
                   FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style,
                   ErrorMessage, RegenerateOfId, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
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
            SettingsJson = reader.GetString(3),
            InputExcerpt = reader.GetString(4),
            OutputPrompt = reader.GetString(5),
            RefineInstruction = reader.IsDBNull(6) ? null : reader.GetString(6),
            Status = ParseEnum<SceneImagePromptStatus>(reader.GetString(7), sessionId, interactionId, "SceneImagePrompts"),
            ModelIdentifier = reader.IsDBNull(8) ? null : reader.GetString(8),
            ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedUtc = ParseUtc(reader.GetString(10), sessionId, interactionId, "CreatedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(11), sessionId, interactionId, "UpdatedUtc")
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
            FileRelativePath = reader.IsDBNull(6) ? null : reader.GetString(6),
            ModelIdentifier = reader.IsDBNull(7) ? null : reader.GetString(7),
            ProviderName = reader.IsDBNull(8) ? null : reader.GetString(8),
            ContentPolicy = ParseEnum<ImageContentPolicy>(reader.GetString(9), sessionId, interactionId, "SceneImages"),
            ImageSize = reader.IsDBNull(10) ? null : reader.GetString(10),
            Style = reader.IsDBNull(11) ? null : reader.GetString(11),
            ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
            RegenerateOfId = reader.IsDBNull(13) ? null : reader.GetString(13),
            CreatedUtc = ParseUtc(reader.GetString(14), sessionId, interactionId, "CreatedUtc"),
            StartedUtc = reader.IsDBNull(15) ? null : ParseUtc(reader.GetString(15), sessionId, interactionId, "StartedUtc"),
            CompletedUtc = reader.IsDBNull(16) ? null : ParseUtc(reader.GetString(16), sessionId, interactionId, "CompletedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(17), sessionId, interactionId, "UpdatedUtc")
        };
    }

    // ---------------- Schema ----------------

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneImagePrompts (
                Id               TEXT PRIMARY KEY,
                SessionId        TEXT NOT NULL,
                InteractionId    TEXT NOT NULL,
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
                FileRelativePath TEXT NULL,
                ModelIdentifier  TEXT NULL,
                ProviderName     TEXT NULL,
                ContentPolicy    TEXT NOT NULL,
                ImageSize        TEXT NULL,
                Style            TEXT NULL,
                ErrorMessage     TEXT NULL,
                RegenerateOfId   TEXT NULL,
                CreatedUtc       TEXT NOT NULL,
                StartedUtc       TEXT NULL,
                CompletedUtc     TEXT NULL,
                UpdatedUtc       TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImages_Session
                ON SceneImages (SessionId);
            CREATE INDEX IF NOT EXISTS IX_SceneImages_Interaction
                ON SceneImages (SessionId, InteractionId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ---------------- Validation / parsing ----------------

    private static void ValidatePrompt(SceneImagePromptRecord prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt.SessionId))
            throw new InvalidOperationException("Scene image prompt requires SessionId.");
        if (string.IsNullOrWhiteSpace(prompt.InteractionId))
            throw new InvalidOperationException("Scene image prompt requires InteractionId.");
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
