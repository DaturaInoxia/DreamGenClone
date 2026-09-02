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
                Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, ProductionGroupId, CompiledMediaBriefId,
                Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
                Status, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $sessionId, $interactionId, $beatAnalysisId, $beatSnapshotJson, $productionGroupId, $compiledMediaBriefId,
                $pov, $settingsJson, $inputExcerpt, $outputPrompt, $refineInstruction,
                $status, $modelIdentifier, $errorMessage, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                SessionId = excluded.SessionId,
                InteractionId = excluded.InteractionId,
                BeatAnalysisId = excluded.BeatAnalysisId,
                BeatSnapshotJson = excluded.BeatSnapshotJson,
                ProductionGroupId = excluded.ProductionGroupId,
                CompiledMediaBriefId = excluded.CompiledMediaBriefId,
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
        command.Parameters.AddWithValue("$productionGroupId", (object?)prompt.ProductionGroupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$compiledMediaBriefId", (object?)prompt.CompiledMediaBriefId ?? DBNull.Value);
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
             SELECT Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, ProductionGroupId, CompiledMediaBriefId,
                 Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
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
             SELECT Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, ProductionGroupId, CompiledMediaBriefId,
                 Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
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
             SELECT Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, ProductionGroupId, CompiledMediaBriefId,
                 Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
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

    public async Task<SceneImagePromptRecord?> GetLatestCompletedProductionPromptAsync(
        string sessionId,
        string interactionId,
        string productionGroupId,
        string compiledMediaBriefId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(interactionId)
            || string.IsNullOrWhiteSpace(productionGroupId)
            || string.IsNullOrWhiteSpace(compiledMediaBriefId))
        {
            throw new InvalidOperationException("Session, interaction, production group, and compiled media brief are required to load a production prompt.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, BeatAnalysisId, BeatSnapshotJson, ProductionGroupId, CompiledMediaBriefId,
                   Pov, SettingsJson, InputExcerpt, OutputPrompt, RefineInstruction,
                   Status, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc
            FROM SceneImagePrompts
            WHERE SessionId = $sessionId
              AND InteractionId = $interactionId
              AND ProductionGroupId = $productionGroupId
              AND CompiledMediaBriefId = $compiledMediaBriefId
              AND Status = 'Complete'
            ORDER BY UpdatedUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", interactionId.Trim());
        command.Parameters.AddWithValue("$productionGroupId", productionGroupId.Trim());
        command.Parameters.AddWithValue("$compiledMediaBriefId", compiledMediaBriefId.Trim());

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
                ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId, IdentityPacksJson,
                ProductionGroupId, CompiledMediaBriefId, ProductionStage, Disposition, CatalogueId,
                BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                MomentId, MomentEnrichmentId, MomentEnrichmentRevision, TypedReferenceSnapshotJson,
                Sha256, BytesPurgedUtc, DispositionUpdatedUtc, RequestedModelId)
            VALUES (
                $id, $sessionId, $interactionId, $promptRecordId, $promptSnapshot, $status,
                $operation, $sourceImageId, $editSessionId, $editCompilationAttemptId, $editPromptRevisionId, $editIntentSnapshot, $editCompilerProvenanceJson,
                $fileRelativePath, $modelIdentifier, $providerName, $contentPolicy, $imageSize, $style, $settingsJson,
                $errorMessage, $regenerateOfId, $beatId, $pov, $createdUtc, $startedUtc, $completedUtc, $updatedUtc, $renderMode, $identityPackId, $identityPacksJson,
                $productionGroupId, $compiledMediaBriefId, $productionStage, $disposition, $catalogueId,
                $beatProductionPlanId, $beatProductionPlanVersion, $momentSetId, $momentSetVersion,
                $momentId, $momentEnrichmentId, $momentEnrichmentRevision, $typedReferenceSnapshotJson,
                $sha256, $bytesPurgedUtc, $dispositionUpdatedUtc, $requestedModelId);
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
        command.Parameters.AddWithValue("$requestedModelId", (object?)image.RequestedModelId ?? DBNull.Value);
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
        command.Parameters.AddWithValue("$identityPacksJson", (object?)image.IdentityPacksJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$productionGroupId", (object?)image.ProductionGroupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$compiledMediaBriefId", (object?)image.CompiledMediaBriefId ?? DBNull.Value);
        command.Parameters.AddWithValue("$productionStage", image.ProductionStage?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$disposition", image.Disposition?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$catalogueId", (object?)image.CatalogueId ?? DBNull.Value);
        command.Parameters.AddWithValue("$beatProductionPlanId", (object?)image.BeatProductionPlanId ?? DBNull.Value);
        command.Parameters.AddWithValue("$beatProductionPlanVersion", (object?)image.BeatProductionPlanVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$momentSetId", (object?)image.MomentSetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$momentSetVersion", (object?)image.MomentSetVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$momentId", (object?)image.MomentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$momentEnrichmentId", (object?)image.MomentEnrichmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$momentEnrichmentRevision", (object?)image.MomentEnrichmentRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$typedReferenceSnapshotJson", (object?)image.TypedReferenceSnapshotJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$sha256", (object?)image.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$bytesPurgedUtc", image.BytesPurgedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$dispositionUpdatedUtc", image.DispositionUpdatedUtc?.ToString("O") ?? (object)DBNull.Value);
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
                     ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId, IdentityPacksJson,
                     ProductionGroupId, CompiledMediaBriefId, ProductionStage, Disposition, CatalogueId,
                     BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                     MomentId, MomentEnrichmentId, MomentEnrichmentRevision, TypedReferenceSnapshotJson, Sha256, BytesPurgedUtc, DispositionUpdatedUtc, RequestedModelId
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
                     ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId, IdentityPacksJson,
                     ProductionGroupId, CompiledMediaBriefId, ProductionStage, Disposition, CatalogueId,
                     BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                     MomentId, MomentEnrichmentId, MomentEnrichmentRevision, TypedReferenceSnapshotJson, Sha256, BytesPurgedUtc, DispositionUpdatedUtc, RequestedModelId
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

    public async Task<IReadOnlyList<SceneImageRecord>> ListImagesByProductionGroupAsync(
        string productionGroupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productionGroupId))
        {
            throw new InvalidOperationException("Production group id is required to list scene images.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, PromptRecordId, PromptSnapshot, Status,
                 Operation, SourceImageId, EditSessionId, EditCompilationAttemptId, EditPromptRevisionId, EditIntentSnapshot, EditCompilerProvenanceJson,
                 FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style, SettingsJson,
                   ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId, IdentityPacksJson,
                   ProductionGroupId, CompiledMediaBriefId, ProductionStage, Disposition, CatalogueId,
                   BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                   MomentId, MomentEnrichmentId, MomentEnrichmentRevision, TypedReferenceSnapshotJson, Sha256, BytesPurgedUtc, DispositionUpdatedUtc, RequestedModelId
            FROM SceneImages
            WHERE ProductionGroupId = $productionGroupId
            ORDER BY CreatedUtc DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$productionGroupId", productionGroupId.Trim());

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
                     ErrorMessage, RegenerateOfId, BeatId, Pov, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, RenderMode, IdentityPackId, IdentityPacksJson,
                     ProductionGroupId, CompiledMediaBriefId, ProductionStage, Disposition, CatalogueId,
                     BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                     MomentId, MomentEnrichmentId, MomentEnrichmentRevision, TypedReferenceSnapshotJson, Sha256, BytesPurgedUtc, DispositionUpdatedUtc, RequestedModelId
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

    public async Task<bool> TrySetDispositionAsync(
        string imageId,
        string productionGroupId,
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition,
        DateTime updatedUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId) || string.IsNullOrWhiteSpace(productionGroupId))
            throw new InvalidOperationException("Image id and production group id are required to update disposition.");
        if (!IsAllowedDispositionTransition(expectedDisposition, nextDisposition))
            throw new InvalidOperationException($"Scene image disposition transition from {expectedDisposition} to {nextDisposition} is not allowed.");
        if (updatedUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Disposition update timestamp must be UTC.");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneImages
            SET Disposition = $nextDisposition, DispositionUpdatedUtc = $updatedUtc, UpdatedUtc = $updatedUtc
            WHERE Id = $imageId
              AND ProductionGroupId = $productionGroupId
              AND Disposition = $expectedDisposition;
            """;
        command.Parameters.AddWithValue("$imageId", imageId.Trim());
        command.Parameters.AddWithValue("$productionGroupId", productionGroupId.Trim());
        command.Parameters.AddWithValue("$expectedDisposition", expectedDisposition.ToString());
        command.Parameters.AddWithValue("$nextDisposition", nextDisposition.ToString());
        command.Parameters.AddWithValue("$updatedUtc", updatedUtc.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<SceneImageBytePurgeReservation> ReserveRejectedBytesPurgeAsync(
        string imageId,
        DateTime reservedUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId))
            throw new InvalidOperationException("Image id is required to purge rejected bytes.");
        if (reservedUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Purge reservation timestamp must be UTC.");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        string status;
        string? productionGroupId;
        string? disposition;
        string? path;
        string? sha256;
        string? bytesPurgedUtc;
        string? dispositionUpdatedUtc;
        await using (var load = connection.CreateCommand())
        {
            load.Transaction = transaction;
            load.CommandText = "SELECT Status, ProductionGroupId, Disposition, FileRelativePath, Sha256, BytesPurgedUtc, DispositionUpdatedUtc FROM SceneImages WHERE Id = $id;";
            load.Parameters.AddWithValue("$id", imageId.Trim());
            await using var reader = await load.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException($"Scene image '{imageId}' was not found.");
            status = reader.GetString(0);
            productionGroupId = reader.IsDBNull(1) ? null : reader.GetString(1);
            disposition = reader.IsDBNull(2) ? null : reader.GetString(2);
            path = reader.IsDBNull(3) ? null : reader.GetString(3);
            sha256 = reader.IsDBNull(4) ? null : reader.GetString(4);
            bytesPurgedUtc = reader.IsDBNull(5) ? null : reader.GetString(5);
            dispositionUpdatedUtc = reader.IsDBNull(6) ? null : reader.GetString(6);
        }

        if (status is not ("Complete" or "Failed"))
            throw new InvalidOperationException($"Scene image '{imageId}' must be Complete or Failed before rejected bytes can be purged.");
        if (string.IsNullOrWhiteSpace(productionGroupId))
            throw new InvalidOperationException($"Scene image '{imageId}' is not a production-group attempt.");
        if (!string.Equals(disposition, "Rejected", StringComparison.Ordinal))
            throw new InvalidOperationException($"Scene image '{imageId}' must have Rejected disposition before bytes can be purged.");
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(sha256))
            throw new InvalidOperationException($"Scene image '{imageId}' has no purgeable file path and checksum.");
        if (bytesPurgedUtc is not null)
            throw new InvalidOperationException($"Scene image '{imageId}' bytes are already purged or reserved for purge.");

        await EnforceRetentionPolicyAsync(connection, transaction, imageId, dispositionUpdatedUtc, reservedUtc, cancellationToken);
        await EnforcePurgeProtectionAsync(connection, transaction, imageId.Trim(), path, cancellationToken);

        await using var reserve = connection.CreateCommand();
        reserve.Transaction = transaction;
        reserve.CommandText = """
            UPDATE SceneImages SET BytesPurgedUtc = $reservedUtc, UpdatedUtc = $reservedUtc
            WHERE Id = $id AND Status IN ('Complete', 'Failed') AND ProductionGroupId = $groupId
              AND Disposition = 'Rejected' AND FileRelativePath = $path AND Sha256 = $sha256
              AND BytesPurgedUtc IS NULL;
            """;
        reserve.Parameters.AddWithValue("$reservedUtc", reservedUtc.ToString("O"));
        reserve.Parameters.AddWithValue("$id", imageId.Trim());
        reserve.Parameters.AddWithValue("$groupId", productionGroupId);
        reserve.Parameters.AddWithValue("$path", path);
        reserve.Parameters.AddWithValue("$sha256", sha256);
        if (await reserve.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Scene image '{imageId}' changed concurrently before purge reservation.");
        await transaction.CommitAsync(cancellationToken);
        return new SceneImageBytePurgeReservation(imageId.Trim(), path, reservedUtc);
    }

    public async Task CompleteRejectedBytesPurgeAsync(
        SceneImageBytePurgeReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneImages SET FileRelativePath = NULL, UpdatedUtc = $reservedUtc
            WHERE Id = $id AND FileRelativePath = $path AND Disposition = 'Rejected'
              AND BytesPurgedUtc = $reservedUtc;
            """;
        AddReservationParameters(command, reservation);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Scene image '{reservation.ImageId}' purge completion no longer matches its reservation.");
    }

    public async Task ReleaseRejectedBytesPurgeAsync(
        SceneImageBytePurgeReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneImages SET BytesPurgedUtc = NULL, UpdatedUtc = $reservedUtc
            WHERE Id = $id AND FileRelativePath = $path AND Disposition = 'Rejected'
              AND BytesPurgedUtc = $reservedUtc;
            """;
        AddReservationParameters(command, reservation);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Scene image '{reservation.ImageId}' purge reservation could not be released.");
    }

    private static void AddReservationParameters(SqliteCommand command, SceneImageBytePurgeReservation reservation)
    {
        command.Parameters.AddWithValue("$id", reservation.ImageId);
        command.Parameters.AddWithValue("$path", reservation.FileRelativePath);
        command.Parameters.AddWithValue("$reservedUtc", reservation.ReservedUtc.ToString("O"));
    }

    private static async Task EnforceRetentionPolicyAsync(
        SqliteConnection connection, SqliteTransaction transaction, string imageId,
        string? dispositionUpdatedUtc, DateTime reservedUtc, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Mode, RejectedRetentionDays FROM SceneImageAttemptRetentionPolicies WHERE Id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Scene image attempt retention policy is not configured.");
        var mode = reader.GetString(0);
        if (mode == "Manual") return;
        if (mode != "Automatic" || reader.IsDBNull(1))
            throw new InvalidOperationException("Persisted scene image attempt retention policy is invalid.");
        if (dispositionUpdatedUtc is null)
            throw new InvalidOperationException($"Scene image '{imageId}' has no persisted rejection timestamp required by Automatic retention.");
        var rejectedUtc = DateTime.Parse(dispositionUpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var days = reader.GetInt32(1);
        if (rejectedUtc > reservedUtc.AddDays(-days))
            throw new InvalidOperationException($"Scene image '{imageId}' has not reached the configured {days}-day rejected retention age.");
    }

    private static async Task EnforcePurgeProtectionAsync(
        SqliteConnection connection, SqliteTransaction transaction, string imageId, string path,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(connection, transaction, "ApprovedSceneFrameDecisions", cancellationToken)
            && await CountAsync(connection, transaction,
                "SELECT COUNT(*) FROM ApprovedSceneFrameDecisions WHERE SceneImageId = $imageId", imageId, path, cancellationToken) > 0)
            throw new InvalidOperationException($"Scene image '{imageId}' is protected by approval decision history.");

        if (await TableExistsAsync(connection, transaction, "SceneImageProductionGroups", cancellationToken)
            && await TableExistsAsync(connection, transaction, "ApprovedSceneFrameDecisions", cancellationToken))
        {
            const string ancestorSql = """
                WITH RECURSIVE Protected(Id) AS (
                    SELECT decision.SceneImageId
                    FROM SceneImageProductionGroups productionGroup
                    JOIN ApprovedSceneFrameDecisions decision ON decision.Id = productionGroup.CurrentApprovedDecisionId
                    WHERE productionGroup.CurrentApprovedDecisionId IS NOT NULL
                    UNION
                    SELECT image.SourceImageId FROM SceneImages image JOIN Protected ON image.Id = Protected.Id
                    WHERE image.SourceImageId IS NOT NULL)
                SELECT COUNT(*) FROM Protected WHERE Id = $imageId;
                """;
            if (await CountAsync(connection, transaction, ancestorSql, imageId, path, cancellationToken) > 0)
                throw new InvalidOperationException($"Scene image '{imageId}' is protected as an ancestor of a current approval.");
        }

        const string descendantSql = """
            WITH RECURSIVE Descendants(Id) AS (
                SELECT Id FROM SceneImages WHERE SourceImageId = $imageId
                UNION SELECT image.Id FROM SceneImages image JOIN Descendants ON image.SourceImageId = Descendants.Id)
            SELECT COUNT(*) FROM SceneImages image JOIN Descendants ON image.Id = Descendants.Id
            WHERE image.FileRelativePath IS NOT NULL AND image.BytesPurgedUtc IS NULL;
            """;
        if (await CountAsync(connection, transaction, descendantSql, imageId, path, cancellationToken) > 0)
            throw new InvalidOperationException($"Scene image '{imageId}' is protected by an unpurged descendant source-image reference.");
        if (await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM SceneImages WHERE Id <> $imageId AND FileRelativePath = $path AND BytesPurgedUtc IS NULL",
            imageId, path, cancellationToken) > 0)
            throw new InvalidOperationException($"Scene image '{imageId}' is protected because another scene image shares its file path.");

        if (await TableExistsAsync(connection, transaction, "SceneImageEditSessions", cancellationToken)
            && await CountAsync(connection, transaction,
                "SELECT COUNT(*) FROM SceneImageEditSessions WHERE SourceImageId = $imageId", imageId, path, cancellationToken) > 0)
            throw new InvalidOperationException($"Scene image '{imageId}' is protected by an edit-session source reference.");
        if (await TableExistsAsync(connection, transaction, "SceneAssets", cancellationToken)
            && await CountAsync(connection, transaction,
                "SELECT COUNT(*) FROM SceneAssets WHERE FileRelativePath = $path OR SourceSceneImageId = $imageId", imageId, path, cancellationToken) > 0)
            throw new InvalidOperationException($"Scene image '{imageId}' is protected by a reusable scene asset.");
        if (await TableExistsAsync(connection, transaction, "SceneImageReferenceAssets", cancellationToken)
            && await CountAsync(connection, transaction,
                "SELECT COUNT(*) FROM SceneImageReferenceAssets WHERE FileRelativePath = $path", imageId, path, cancellationToken) > 0)
            throw new InvalidOperationException($"Scene image '{imageId}' is protected by an identity reference asset.");
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection, SqliteTransaction transaction, string sql, string imageId, string path,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$imageId", imageId);
        command.Parameters.AddWithValue("$path", path);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }

    private static bool IsAllowedDispositionTransition(
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition)
        => (expectedDisposition, nextDisposition) switch
        {
            (SceneImageAttemptDisposition.Active, SceneImageAttemptDisposition.Shortlisted) => true,
            (SceneImageAttemptDisposition.Shortlisted, SceneImageAttemptDisposition.Active) => true,
            (SceneImageAttemptDisposition.Active, SceneImageAttemptDisposition.Rejected) => true,
            (SceneImageAttemptDisposition.Shortlisted, SceneImageAttemptDisposition.Rejected) => true,
            (SceneImageAttemptDisposition.Rejected, SceneImageAttemptDisposition.Archived) => true,
            _ => false
        };

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
            ProductionGroupId = reader.IsDBNull(5) ? null : reader.GetString(5),
            CompiledMediaBriefId = reader.IsDBNull(6) ? null : reader.GetString(6),
            Pov = reader.GetString(7),
            SettingsJson = reader.GetString(8),
            InputExcerpt = reader.GetString(9),
            OutputPrompt = reader.GetString(10),
            RefineInstruction = reader.IsDBNull(11) ? null : reader.GetString(11),
            Status = ParseEnum<SceneImagePromptStatus>(reader.GetString(12), sessionId, interactionId, "SceneImagePrompts"),
            ModelIdentifier = reader.IsDBNull(13) ? null : reader.GetString(13),
            ErrorMessage = reader.IsDBNull(14) ? null : reader.GetString(14),
            CreatedUtc = ParseUtc(reader.GetString(15), sessionId, interactionId, "CreatedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(16), sessionId, interactionId, "UpdatedUtc")
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
            IdentityPackId = reader.IsDBNull(29) ? null : reader.GetString(29),
            IdentityPacksJson = reader.IsDBNull(30) ? null : reader.GetString(30),
            ProductionGroupId = reader.IsDBNull(31) ? null : reader.GetString(31),
            CompiledMediaBriefId = reader.IsDBNull(32) ? null : reader.GetString(32),
            ProductionStage = reader.IsDBNull(33) ? null : ParseEnum<SceneImageProductionStage>(reader.GetString(33), sessionId, interactionId, "SceneImages"),
            Disposition = reader.IsDBNull(34) ? null : ParseEnum<SceneImageAttemptDisposition>(reader.GetString(34), sessionId, interactionId, "SceneImages"),
            CatalogueId = reader.IsDBNull(35) ? null : reader.GetString(35),
            BeatProductionPlanId = reader.IsDBNull(36) ? null : reader.GetString(36),
            BeatProductionPlanVersion = reader.IsDBNull(37) ? null : reader.GetInt32(37),
            MomentSetId = reader.IsDBNull(38) ? null : reader.GetString(38),
            MomentSetVersion = reader.IsDBNull(39) ? null : reader.GetInt32(39),
            MomentId = reader.IsDBNull(40) ? null : reader.GetString(40),
            MomentEnrichmentId = reader.IsDBNull(41) ? null : reader.GetString(41),
            MomentEnrichmentRevision = reader.IsDBNull(42) ? null : reader.GetInt32(42),
            TypedReferenceSnapshotJson = reader.IsDBNull(43) ? null : reader.GetString(43),
            Sha256 = reader.IsDBNull(44) ? null : reader.GetString(44),
            BytesPurgedUtc = reader.IsDBNull(45) ? null : ParseUtc(reader.GetString(45), sessionId, interactionId, "BytesPurgedUtc"),
            DispositionUpdatedUtc = reader.IsDBNull(46) ? null : ParseUtc(reader.GetString(46), sessionId, interactionId, "DispositionUpdatedUtc"),
            RequestedModelId = reader.IsDBNull(47) ? null : reader.GetString(47)
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
                ProductionGroupId TEXT NULL,
                CompiledMediaBriefId TEXT NULL,
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
                IdentityPackId   TEXT NULL,
                IdentityPacksJson TEXT NULL,
                ProductionGroupId TEXT NULL,
                CompiledMediaBriefId TEXT NULL,
                ProductionStage TEXT NULL,
                Disposition TEXT NULL,
                CatalogueId TEXT NULL,
                BeatProductionPlanId TEXT NULL,
                BeatProductionPlanVersion INTEGER NULL,
                MomentSetId TEXT NULL,
                MomentSetVersion INTEGER NULL,
                MomentId TEXT NULL,
                MomentEnrichmentId TEXT NULL,
                MomentEnrichmentRevision INTEGER NULL,
                TypedReferenceSnapshotJson TEXT NULL,
                Sha256 TEXT NULL,
                BytesPurgedUtc TEXT NULL,
                DispositionUpdatedUtc TEXT NULL,
                RequestedModelId TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImages_Session
                ON SceneImages (SessionId);
            CREATE INDEX IF NOT EXISTS IX_SceneImages_Interaction
                ON SceneImages (SessionId, InteractionId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsurePromptBeatColumnsAsync(connection, cancellationToken);
        await EnsureImageEditColumnsAsync(connection, cancellationToken);
        await EnsureImageProductionColumnsAsync(connection, cancellationToken);
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
            ("ProductionGroupId", "ALTER TABLE SceneImagePrompts ADD COLUMN ProductionGroupId TEXT NULL"),
            ("CompiledMediaBriefId", "ALTER TABLE SceneImagePrompts ADD COLUMN CompiledMediaBriefId TEXT NULL"),
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
            ("IdentityPackId", "ALTER TABLE SceneImages ADD COLUMN IdentityPackId TEXT NULL"),
            ("IdentityPacksJson", "ALTER TABLE SceneImages ADD COLUMN IdentityPacksJson TEXT NULL")
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

    private static async Task EnsureImageProductionColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (name, sql) in new[]
        {
            ("ProductionGroupId", "ALTER TABLE SceneImages ADD COLUMN ProductionGroupId TEXT NULL"),
            ("CompiledMediaBriefId", "ALTER TABLE SceneImages ADD COLUMN CompiledMediaBriefId TEXT NULL"),
            ("ProductionStage", "ALTER TABLE SceneImages ADD COLUMN ProductionStage TEXT NULL"),
            ("Disposition", "ALTER TABLE SceneImages ADD COLUMN Disposition TEXT NULL"),
            ("CatalogueId", "ALTER TABLE SceneImages ADD COLUMN CatalogueId TEXT NULL"),
            ("BeatProductionPlanId", "ALTER TABLE SceneImages ADD COLUMN BeatProductionPlanId TEXT NULL"),
            ("BeatProductionPlanVersion", "ALTER TABLE SceneImages ADD COLUMN BeatProductionPlanVersion INTEGER NULL"),
            ("MomentSetId", "ALTER TABLE SceneImages ADD COLUMN MomentSetId TEXT NULL"),
            ("MomentSetVersion", "ALTER TABLE SceneImages ADD COLUMN MomentSetVersion INTEGER NULL"),
            ("MomentId", "ALTER TABLE SceneImages ADD COLUMN MomentId TEXT NULL"),
            ("MomentEnrichmentId", "ALTER TABLE SceneImages ADD COLUMN MomentEnrichmentId TEXT NULL"),
            ("MomentEnrichmentRevision", "ALTER TABLE SceneImages ADD COLUMN MomentEnrichmentRevision INTEGER NULL"),
            ("TypedReferenceSnapshotJson", "ALTER TABLE SceneImages ADD COLUMN TypedReferenceSnapshotJson TEXT NULL"),
            ("Sha256", "ALTER TABLE SceneImages ADD COLUMN Sha256 TEXT NULL"),
            ("BytesPurgedUtc", "ALTER TABLE SceneImages ADD COLUMN BytesPurgedUtc TEXT NULL"),
            ("DispositionUpdatedUtc", "ALTER TABLE SceneImages ADD COLUMN DispositionUpdatedUtc TEXT NULL"),
            ("RequestedModelId", "ALTER TABLE SceneImages ADD COLUMN RequestedModelId TEXT NULL")
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

        await using var index = connection.CreateCommand();
        index.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_SceneImages_ProductionGroup
                ON SceneImages (ProductionGroupId, CreatedUtc DESC);
            CREATE TABLE IF NOT EXISTS SceneImageAttemptRetentionPolicies (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Mode TEXT NOT NULL CHECK (Mode IN ('Manual', 'Automatic')),
                RejectedRetentionDays INTEGER NULL,
                UpdatedBy TEXT NOT NULL CHECK (length(trim(UpdatedBy)) > 0),
                UpdatedUtc TEXT NOT NULL,
                Version INTEGER NOT NULL CHECK (Version > 0),
                CHECK ((Mode = 'Manual' AND RejectedRetentionDays IS NULL)
                    OR (Mode = 'Automatic' AND RejectedRetentionDays BETWEEN 1 AND 3650))
            );
            """;
        await index.ExecuteNonQueryAsync(cancellationToken);
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
        var hasLegacyAnalysis = !string.IsNullOrWhiteSpace(prompt.BeatAnalysisId);
        var hasLegacySnapshot = !string.IsNullOrWhiteSpace(prompt.BeatSnapshotJson)
            && !string.Equals(prompt.BeatSnapshotJson.Trim(), "{}", StringComparison.Ordinal);
        var hasProductionGroup = !string.IsNullOrWhiteSpace(prompt.ProductionGroupId);
        var hasCompiledBrief = !string.IsNullOrWhiteSpace(prompt.CompiledMediaBriefId);
        var hasLegacyLineage = hasLegacyAnalysis && hasLegacySnapshot;
        var hasCanonicalLineage = hasProductionGroup && hasCompiledBrief;
        if (hasLegacyAnalysis != hasLegacySnapshot || hasProductionGroup != hasCompiledBrief
            || hasLegacyLineage == hasCanonicalLineage)
        {
            throw new InvalidOperationException(
                "Scene image prompt writes require exactly one complete lineage mode: ProductionGroupId with CompiledMediaBriefId, or legacy BeatAnalysisId with BeatSnapshotJson.");
        }
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
