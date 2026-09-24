using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>SQLite persistence for character identity builds and their per-step records.</summary>
public sealed class CharacterIdentityBuildRepository : ICharacterIdentityBuildRepository
{
    private readonly string _connectionString;

    public CharacterIdentityBuildRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
    }

    public async Task UpsertBuildAsync(CharacterIdentityBuild build, CancellationToken cancellationToken = default)
    {
        Require(build.Id, "Build id");
        Require(build.CharacterTemplateId, "Character profile id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityBuilds (Id, CharacterProfileId, BatchId, FrontContainerAssetId, CanonicalFrontAssetId, ProducedIdentityPackId, CurrentStep, Status, CreatedUtc, UpdatedUtc, TargetKind)
            VALUES ($id, $characterProfileId, $batchId, $frontContainerAssetId, $canonicalFrontAssetId, $producedIdentityPackId, $currentStep, $status, $createdUtc, $updatedUtc, $targetKind)
            ON CONFLICT(Id) DO UPDATE SET
                CharacterProfileId = excluded.CharacterProfileId,
                BatchId = excluded.BatchId,
                FrontContainerAssetId = excluded.FrontContainerAssetId,
                CanonicalFrontAssetId = excluded.CanonicalFrontAssetId,
                ProducedIdentityPackId = excluded.ProducedIdentityPackId,
                CurrentStep = excluded.CurrentStep,
                Status = excluded.Status,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", build.Id.Trim());
        command.Parameters.AddWithValue("$characterProfileId", build.CharacterTemplateId.Trim());
        command.Parameters.AddWithValue("$batchId", (object?)build.BatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$frontContainerAssetId", (object?)build.FrontContainerAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$canonicalFrontAssetId", (object?)build.CanonicalFrontAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$producedIdentityPackId", (object?)build.ProducedIdentityPackId ?? DBNull.Value);
        command.Parameters.AddWithValue("$currentStep", (int)build.CurrentStep);
        command.Parameters.AddWithValue("$status", (int)build.Status);
        command.Parameters.AddWithValue("$createdUtc", build.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedUtc", build.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$targetKind", (int)build.TargetKind);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CharacterIdentityBuild?> GetBuildAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Build id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CharacterProfileId, BatchId, FrontContainerAssetId, CurrentStep, Status, CreatedUtc, UpdatedUtc, CanonicalFrontAssetId, ProducedIdentityPackId, TargetKind
            FROM CharacterIdentityBuilds WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBuild(reader) : null;
    }

    public async Task<IReadOnlyList<CharacterIdentityBuild>> ListBuildsAsync(string characterProfileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CharacterProfileId, BatchId, FrontContainerAssetId, CurrentStep, Status, CreatedUtc, UpdatedUtc, CanonicalFrontAssetId, ProducedIdentityPackId, TargetKind
            FROM CharacterIdentityBuilds WHERE CharacterProfileId = $characterProfileId
            ORDER BY CreatedUtc DESC;
            """;
        command.Parameters.AddWithValue("$characterProfileId", characterProfileId.Trim());
        var results = new List<CharacterIdentityBuild>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadBuild(reader));
        }

        return results;
    }

    public async Task UpsertStepAsync(CharacterIdentityBuildStepRecord step, CancellationToken cancellationToken = default)
    {
        Require(step.Id, "Step id");
        Require(step.BuildId, "Build id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityBuildSteps (
                Id, BuildId, Step, Status, InputArtifactId, OutputArtifactId, ResolvedPromptText,
                ResolvedModelId, FailureReason, MirrorDerived, ManualOverrideApplied, ManualOverrideReason,
                ManualOverrideAuthor, ManualOverrideUtc, MeasurementJson, RawToolOutput,
                CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $buildId, $step, $status, $inputArtifactId, $outputArtifactId, $resolvedPromptText,
                $resolvedModelId, $failureReason, $mirrorDerived, $manualOverrideApplied, $manualOverrideReason,
                $manualOverrideAuthor, $manualOverrideUtc, $measurementJson, $rawToolOutput,
                $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status,
                InputArtifactId = excluded.InputArtifactId,
                OutputArtifactId = excluded.OutputArtifactId,
                ResolvedPromptText = excluded.ResolvedPromptText,
                ResolvedModelId = excluded.ResolvedModelId,
                FailureReason = excluded.FailureReason,
                MirrorDerived = excluded.MirrorDerived,
                ManualOverrideApplied = excluded.ManualOverrideApplied,
                ManualOverrideReason = excluded.ManualOverrideReason,
                ManualOverrideAuthor = excluded.ManualOverrideAuthor,
                ManualOverrideUtc = excluded.ManualOverrideUtc,
                MeasurementJson = excluded.MeasurementJson,
                RawToolOutput = excluded.RawToolOutput,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", step.Id.Trim());
        command.Parameters.AddWithValue("$buildId", step.BuildId.Trim());
        command.Parameters.AddWithValue("$step", (int)step.Step);
        command.Parameters.AddWithValue("$status", (int)step.Status);
        command.Parameters.AddWithValue("$inputArtifactId", (object?)step.InputArtifactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputArtifactId", (object?)step.OutputArtifactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedPromptText", (object?)step.ResolvedPromptText ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedModelId", (object?)step.ResolvedModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$failureReason", (object?)step.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$mirrorDerived", step.MirrorDerived ? 1 : 0);
        command.Parameters.AddWithValue("$manualOverrideApplied", step.ManualOverrideApplied ? 1 : 0);
        command.Parameters.AddWithValue("$manualOverrideReason", (object?)step.ManualOverrideReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualOverrideAuthor", (object?)step.ManualOverrideAuthor ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualOverrideUtc", step.ManualOverrideUtc is null
            ? DBNull.Value
            : step.ManualOverrideUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$measurementJson", (object?)step.MeasurementJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$rawToolOutput", (object?)step.RawToolOutput ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", step.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedUtc", step.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CharacterIdentityBuildStepRecord?> GetStepAsync(string id, CancellationToken cancellationToken = default)
    {
        Require(id, "Step id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BuildId, Step, Status, InputArtifactId, OutputArtifactId, ResolvedPromptText,
                   ResolvedModelId, FailureReason, MirrorDerived, ManualOverrideApplied, ManualOverrideReason,
                   ManualOverrideAuthor, ManualOverrideUtc, MeasurementJson, RawToolOutput,
                   CreatedUtc, UpdatedUtc
            FROM CharacterIdentityBuildSteps WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadStep(reader) : null;
    }

    public async Task<IReadOnlyList<CharacterIdentityBuildStepRecord>> ListStepsAsync(string buildId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BuildId, Step, Status, InputArtifactId, OutputArtifactId, ResolvedPromptText,
                   ResolvedModelId, FailureReason, MirrorDerived, ManualOverrideApplied, ManualOverrideReason,
                   ManualOverrideAuthor, ManualOverrideUtc, MeasurementJson, RawToolOutput,
                   CreatedUtc, UpdatedUtc
            FROM CharacterIdentityBuildSteps WHERE BuildId = $buildId ORDER BY Step;
            """;
        command.Parameters.AddWithValue("$buildId", buildId.Trim());
        var results = new List<CharacterIdentityBuildStepRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStep(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<CharacterIdentityStepDefinition>> ListStepPlanAsync(
        CharacterIdentityTargetKind kind, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Kind, Step, OrderIndex, HandlerKey, TemplateKey
            FROM CharacterIdentityStepPlans WHERE Kind = $kind ORDER BY OrderIndex;
            """;
        command.Parameters.AddWithValue("$kind", (int)kind);
        var results = new List<CharacterIdentityStepDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CharacterIdentityStepDefinition(
                (CharacterIdentityTargetKind)reader.GetInt32(0),
                reader.GetInt32(2),
                (CharacterIdentityBuildStep)reader.GetInt32(1),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return results;
    }

    public async Task<IReadOnlyList<CharacterIdentityBodyView>> ListBodyViewsAsync(
        string buildId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BuildId, State, BodyView, RotationDeg, PositionKey, Status, InputArtifactId, OutputArtifactId,
                   ResolvedPromptText, ResolvedModelId, FailureReason, ManualOverrideApplied, ManualOverrideReason,
                   ManualOverrideAuthor, ManualOverrideUtc, CreatedUtc, UpdatedUtc,
                   ShapeVerdict, TattooVerdict, SkinVerdict, AnatomyVerdict, FindingsNote, ReviewedBy, ReviewedUtc,
                   QualityRating, QualityNotes
            FROM CharacterIdentityBodyViews WHERE BuildId = $buildId
            ORDER BY State, BodyView IS NULL, coalesce(BodyView, 0), coalesce(RotationDeg, 0), coalesce(PositionKey, '');
            """;
        command.Parameters.AddWithValue("$buildId", buildId.Trim());
        var results = new List<CharacterIdentityBodyView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CharacterIdentityBodyView
            {
                Id = reader.GetString(0),
                BuildId = reader.GetString(1),
                State = (SceneImageReferenceBodyState)reader.GetInt32(2),
                View = reader.IsDBNull(3) ? null : (SceneImageReferenceBodyView)reader.GetInt32(3),
                RotationDeg = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                PositionKey = reader.IsDBNull(5) ? null : reader.GetString(5),
                Status = (CharacterIdentityAngleStatus)reader.GetInt32(6),
                InputArtifactId = reader.IsDBNull(7) ? null : reader.GetString(7),
                OutputArtifactId = reader.IsDBNull(8) ? null : reader.GetString(8),
                ResolvedPromptText = reader.IsDBNull(9) ? null : reader.GetString(9),
                ResolvedModelId = reader.IsDBNull(10) ? null : reader.GetString(10),
                FailureReason = reader.IsDBNull(11) ? null : reader.GetString(11),
                ManualOverrideApplied = reader.GetInt32(12) != 0,
                ManualOverrideReason = reader.IsDBNull(13) ? null : reader.GetString(13),
                ManualOverrideAuthor = reader.IsDBNull(14) ? null : reader.GetString(14),
                ManualOverrideUtc = reader.IsDBNull(15) ? null : ParseUtc(reader.GetString(15)),
                CreatedUtc = ParseUtc(reader.GetString(16)),
                UpdatedUtc = ParseUtc(reader.GetString(17)),
                Findings = new CharacterIdentityBodyFindings
                {
                    BodyShapeConsistency = (CharacterIdentityBodyCheckVerdict)reader.GetInt32(18),
                    TattoosAndMarks = (CharacterIdentityBodyCheckVerdict)reader.GetInt32(19),
                    SkinBodyAndPubicHair = (CharacterIdentityBodyCheckVerdict)reader.GetInt32(20),
                    AnatomyReview = (CharacterIdentityBodyCheckVerdict)reader.GetInt32(21),
                    Note = reader.IsDBNull(22) ? null : reader.GetString(22),
                    ReviewedBy = reader.IsDBNull(23) ? null : reader.GetString(23),
                    ReviewedUtc = reader.IsDBNull(24) ? null : ParseUtc(reader.GetString(24))
                },
                QualityRating = (SceneImageReferenceQuality)reader.GetInt32(25),
                QualityNotes = reader.IsDBNull(26) ? null : reader.GetString(26)
            });
        }

        return results;
    }

    public async Task UpsertBodyViewAsync(CharacterIdentityBodyView view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        Require(view.Id, "Body view id");
        Require(view.BuildId, "Build id");

        // The axis contract is enforced on the way in: a row whose state/view nobody can interpret could not be
        // promoted with an unambiguous tag, so it is rejected here rather than stored.
        view.Key().Validate();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityBodyViews
                (Id, BuildId, State, BodyView, RotationDeg, PositionKey, Status, InputArtifactId, OutputArtifactId,
                 ResolvedPromptText, ResolvedModelId, FailureReason, ManualOverrideApplied, ManualOverrideReason,
                 ManualOverrideAuthor, ManualOverrideUtc, CreatedUtc, UpdatedUtc,
                 ShapeVerdict, TattooVerdict, SkinVerdict, AnatomyVerdict, FindingsNote, ReviewedBy, ReviewedUtc,
                 QualityRating, QualityNotes)
            VALUES
                ($id, $buildId, $state, $view, $rotationDeg, $positionKey, $status, $inputArtifactId, $outputArtifactId,
                 $resolvedPromptText, $resolvedModelId, $failureReason, $manualOverrideApplied, $manualOverrideReason,
                 $manualOverrideAuthor, $manualOverrideUtc, $createdUtc, $updatedUtc,
                 $shapeVerdict, $tattooVerdict, $skinVerdict, $anatomyVerdict, $findingsNote, $reviewedBy, $reviewedUtc,
                 $qualityRating, $qualityNotes)
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status, InputArtifactId = excluded.InputArtifactId,
                OutputArtifactId = excluded.OutputArtifactId, ResolvedPromptText = excluded.ResolvedPromptText,
                ResolvedModelId = excluded.ResolvedModelId, FailureReason = excluded.FailureReason,
                ManualOverrideApplied = excluded.ManualOverrideApplied,
                ManualOverrideReason = excluded.ManualOverrideReason,
                ManualOverrideAuthor = excluded.ManualOverrideAuthor,
                ManualOverrideUtc = excluded.ManualOverrideUtc, UpdatedUtc = excluded.UpdatedUtc,
                ShapeVerdict = excluded.ShapeVerdict, TattooVerdict = excluded.TattooVerdict,
                SkinVerdict = excluded.SkinVerdict, AnatomyVerdict = excluded.AnatomyVerdict,
                FindingsNote = excluded.FindingsNote, ReviewedBy = excluded.ReviewedBy,
                ReviewedUtc = excluded.ReviewedUtc, QualityRating = excluded.QualityRating,
                QualityNotes = excluded.QualityNotes;
            """;
        command.Parameters.AddWithValue("$id", view.Id.Trim());
        command.Parameters.AddWithValue("$buildId", view.BuildId.Trim());
        command.Parameters.AddWithValue("$state", (int)view.State);
        command.Parameters.AddWithValue("$view", (object?)(view.View is { } canonical ? (int)canonical : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("$rotationDeg", (object?)view.RotationDeg ?? DBNull.Value);
        command.Parameters.AddWithValue("$positionKey", (object?)view.PositionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)view.Status);
        command.Parameters.AddWithValue("$inputArtifactId", (object?)view.InputArtifactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputArtifactId", (object?)view.OutputArtifactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedPromptText", (object?)view.ResolvedPromptText ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedModelId", (object?)view.ResolvedModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$failureReason", (object?)view.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualOverrideApplied", view.ManualOverrideApplied ? 1 : 0);
        command.Parameters.AddWithValue("$manualOverrideReason", (object?)view.ManualOverrideReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualOverrideAuthor", (object?)view.ManualOverrideAuthor ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$manualOverrideUtc",
            view.ManualOverrideUtc is { } overrideUtc
                ? overrideUtc.ToString("O", CultureInfo.InvariantCulture)
                : (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", view.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedUtc", view.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$shapeVerdict", (int)view.Findings.BodyShapeConsistency);
        command.Parameters.AddWithValue("$tattooVerdict", (int)view.Findings.TattoosAndMarks);
        command.Parameters.AddWithValue("$skinVerdict", (int)view.Findings.SkinBodyAndPubicHair);
        command.Parameters.AddWithValue("$anatomyVerdict", (int)view.Findings.AnatomyReview);
        command.Parameters.AddWithValue("$findingsNote", (object?)view.Findings.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$reviewedBy", (object?)view.Findings.ReviewedBy ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$reviewedUtc",
            view.Findings.ReviewedUtc is { } reviewedUtc
                ? reviewedUtc.ToString("O", CultureInfo.InvariantCulture)
                : (object)DBNull.Value);
        command.Parameters.AddWithValue("$qualityRating", (int)view.QualityRating);
        command.Parameters.AddWithValue("$qualityNotes", (object?)view.QualityNotes ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAngleAsync(CharacterIdentityAngleRecord angle, CancellationToken cancellationToken = default)
    {
        Require(angle.Id, "Angle id");
        Require(angle.BuildId, "Build id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityAngles
                (Id, BuildId, View, Status, InputArtifactId, OutputArtifactId, AcceptedAttemptId, ResolvedPromptText,
                 ResolvedModelId, FailureReason, MirrorDerived, ManualConfirmationRequired, ManualConfirmed,
                 CreatedUtc, UpdatedUtc)
            VALUES
                ($id, $buildId, $view, $status, $inputArtifactId, $outputArtifactId, $acceptedAttemptId, $resolvedPromptText,
                 $resolvedModelId, $failureReason, $mirrorDerived, $manualConfirmationRequired, $manualConfirmed,
                 $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status, InputArtifactId = excluded.InputArtifactId,
                OutputArtifactId = excluded.OutputArtifactId, AcceptedAttemptId = excluded.AcceptedAttemptId, ResolvedPromptText = excluded.ResolvedPromptText,
                ResolvedModelId = excluded.ResolvedModelId, FailureReason = excluded.FailureReason,
                MirrorDerived = excluded.MirrorDerived, ManualConfirmationRequired = excluded.ManualConfirmationRequired,
                ManualConfirmed = excluded.ManualConfirmed, UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", angle.Id.Trim());
        command.Parameters.AddWithValue("$buildId", angle.BuildId.Trim());
        command.Parameters.AddWithValue("$view", (int)angle.View);
        command.Parameters.AddWithValue("$status", (int)angle.Status);
        command.Parameters.AddWithValue("$inputArtifactId", (object?)angle.InputArtifactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputArtifactId", (object?)angle.OutputArtifactId ?? DBNull.Value);
        command.Parameters.AddWithValue("$acceptedAttemptId", (object?)angle.AcceptedAttemptId ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedPromptText", (object?)angle.ResolvedPromptText ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedModelId", (object?)angle.ResolvedModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$failureReason", (object?)angle.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$mirrorDerived", angle.MirrorDerived ? 1 : 0);
        command.Parameters.AddWithValue("$manualConfirmationRequired", angle.ManualConfirmationRequired ? 1 : 0);
        command.Parameters.AddWithValue("$manualConfirmed", angle.ManualConfirmed ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", angle.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedUtc", angle.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterIdentityAngleRecord>> ListAnglesAsync(string buildId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BuildId, View, Status, InputArtifactId, OutputArtifactId, AcceptedAttemptId, ResolvedPromptText,
                   ResolvedModelId, FailureReason, MirrorDerived, ManualConfirmationRequired, ManualConfirmed,
                   CreatedUtc, UpdatedUtc
            FROM CharacterIdentityAngles WHERE BuildId = $buildId ORDER BY View;
            """;
        command.Parameters.AddWithValue("$buildId", buildId.Trim());
        var results = new List<CharacterIdentityAngleRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CharacterIdentityAngleRecord
            {
                Id = reader.GetString(0), BuildId = reader.GetString(1), View = (CharacterIdentityAngleView)reader.GetInt32(2),
                Status = (CharacterIdentityAngleStatus)reader.GetInt32(3), InputArtifactId = reader.IsDBNull(4) ? null : reader.GetString(4),
                OutputArtifactId = reader.IsDBNull(5) ? null : reader.GetString(5), AcceptedAttemptId = reader.IsDBNull(6) ? null : reader.GetString(6), ResolvedPromptText = reader.IsDBNull(7) ? null : reader.GetString(7),
                ResolvedModelId = reader.IsDBNull(8) ? null : reader.GetString(8), FailureReason = reader.IsDBNull(9) ? null : reader.GetString(9),
                MirrorDerived = reader.GetInt32(10) != 0, ManualConfirmationRequired = reader.GetInt32(11) != 0,
                ManualConfirmed = reader.GetInt32(12) != 0, CreatedUtc = ParseUtc(reader.GetString(13)), UpdatedUtc = ParseUtc(reader.GetString(14))
            });
        }
        return results;
    }

    public async Task UpsertAngleAttemptAsync(CharacterIdentityAngleAttempt attempt, CancellationToken cancellationToken = default)
    {
        Require(attempt.Id, "Angle attempt id");
        Require(attempt.AngleId, "Angle id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityAngleAttempts
                (Id, AngleId, AttemptNumber, InputArtifactId, OutputArtifactId, PromptText, ResolvedModelId,
                 Status, MirrorDerived, ManualOverrideApplied, ManualOverrideReason, ManualOverrideAuthor, ManualOverrideUtc,
                 FailureReason, CreatedUtc, UpdatedUtc, MeasurementJson)
            VALUES ($id, $angleId, $attemptNumber, $inputArtifactId, $outputArtifactId, $promptText,
                    $resolvedModelId, $status, $mirrorDerived, $manualOverrideApplied, $manualOverrideReason,
                    $manualOverrideAuthor, $manualOverrideUtc, $failureReason, $createdUtc, $updatedUtc, $measurementJson)
            ON CONFLICT(Id) DO UPDATE SET
                OutputArtifactId = excluded.OutputArtifactId, PromptText = excluded.PromptText,
                ResolvedModelId = excluded.ResolvedModelId, Status = excluded.Status,
                MirrorDerived = excluded.MirrorDerived, ManualOverrideApplied = excluded.ManualOverrideApplied,
                ManualOverrideReason = excluded.ManualOverrideReason, ManualOverrideAuthor = excluded.ManualOverrideAuthor,
                ManualOverrideUtc = excluded.ManualOverrideUtc, FailureReason = excluded.FailureReason,
                MeasurementJson = excluded.MeasurementJson,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", attempt.Id.Trim());
        command.Parameters.AddWithValue("$angleId", attempt.AngleId.Trim());
        command.Parameters.AddWithValue("$attemptNumber", attempt.AttemptNumber);
        command.Parameters.AddWithValue("$inputArtifactId", attempt.InputArtifactId.Trim());
        command.Parameters.AddWithValue("$outputArtifactId", attempt.OutputArtifactId.Trim());
        command.Parameters.AddWithValue("$promptText", (object?)attempt.PromptText ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolvedModelId", (object?)attempt.ResolvedModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)attempt.Status);
        command.Parameters.AddWithValue("$mirrorDerived", attempt.MirrorDerived ? 1 : 0);
        command.Parameters.AddWithValue("$manualOverrideApplied", attempt.ManualOverrideApplied ? 1 : 0);
        command.Parameters.AddWithValue("$manualOverrideReason", (object?)attempt.ManualOverrideReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualOverrideAuthor", (object?)attempt.ManualOverrideAuthor ?? DBNull.Value);
        command.Parameters.AddWithValue("$manualOverrideUtc", attempt.ManualOverrideUtc is null ? DBNull.Value : attempt.ManualOverrideUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$failureReason", (object?)attempt.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$measurementJson", (object?)attempt.MeasurementJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", attempt.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedUtc", attempt.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterIdentityAngleAttempt>> ListAngleAttemptsAsync(string angleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                 SELECT Id, AngleId, AttemptNumber, InputArtifactId, OutputArtifactId, PromptText, ResolvedModelId,
                     Status, MirrorDerived, ManualOverrideApplied, ManualOverrideReason, ManualOverrideAuthor, ManualOverrideUtc,
                     FailureReason, CreatedUtc, UpdatedUtc, MeasurementJson
            FROM CharacterIdentityAngleAttempts WHERE AngleId = $angleId ORDER BY AttemptNumber;
            """;
        command.Parameters.AddWithValue("$angleId", angleId.Trim());
        var results = new List<CharacterIdentityAngleAttempt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CharacterIdentityAngleAttempt
            {
                Id = reader.GetString(0), AngleId = reader.GetString(1), AttemptNumber = reader.GetInt32(2),
                InputArtifactId = reader.GetString(3), OutputArtifactId = reader.GetString(4),
                PromptText = reader.IsDBNull(5) ? null : reader.GetString(5),
                ResolvedModelId = reader.IsDBNull(6) ? null : reader.GetString(6),
                Status = (CharacterIdentityAngleStatus)reader.GetInt32(7), MirrorDerived = reader.GetInt32(8) != 0,
                ManualOverrideApplied = reader.GetInt32(9) != 0, ManualOverrideReason = reader.IsDBNull(10) ? null : reader.GetString(10),
                ManualOverrideAuthor = reader.IsDBNull(11) ? null : reader.GetString(11), ManualOverrideUtc = reader.IsDBNull(12) ? null : ParseUtc(reader.GetString(12)),
                FailureReason = reader.IsDBNull(13) ? null : reader.GetString(13), CreatedUtc = ParseUtc(reader.GetString(14)), UpdatedUtc = ParseUtc(reader.GetString(15)),
                MeasurementJson = reader.IsDBNull(16) ? null : reader.GetString(16)
            });
        }
        return results;
    }

    public async Task DeleteAngleAttemptAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Angle attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CharacterIdentityAngleAttempts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", attemptId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordAngleAttemptOverrideAsync(string attemptId, string reason, string author, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Angle attempt id");
        Require(reason, "Override reason");
        Require(author, "Override author");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CharacterIdentityAngleAttempts SET ManualOverrideApplied = 1, ManualOverrideReason = $reason, ManualOverrideAuthor = $author, ManualOverrideUtc = $utc, UpdatedUtc = $utc WHERE Id = $id;";
        var utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.AddWithValue("$id", attemptId.Trim());
        command.Parameters.AddWithValue("$reason", reason.Trim());
        command.Parameters.AddWithValue("$author", author.Trim());
        command.Parameters.AddWithValue("$utc", utc);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Angle attempt '{attemptId}' was not found.");
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
            CREATE TABLE IF NOT EXISTS CharacterIdentityBuilds (
                Id TEXT PRIMARY KEY,
                CharacterProfileId TEXT NOT NULL,
                BatchId TEXT NULL,
                FrontContainerAssetId TEXT NULL,
                CanonicalFrontAssetId TEXT NULL,
                ProducedIdentityPackId TEXT NULL,
                CurrentStep INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityBuilds_Character
                ON CharacterIdentityBuilds (CharacterProfileId);

            CREATE TABLE IF NOT EXISTS CharacterIdentityBuildSteps (
                Id TEXT PRIMARY KEY,
                BuildId TEXT NOT NULL,
                Step INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                InputArtifactId TEXT NULL,
                OutputArtifactId TEXT NULL,
                ResolvedPromptText TEXT NULL,
                ResolvedModelId TEXT NULL,
                FailureReason TEXT NULL,
                MirrorDerived INTEGER NOT NULL DEFAULT 0,
                ManualOverrideApplied INTEGER NOT NULL DEFAULT 0,
                ManualOverrideReason TEXT NULL,
                ManualOverrideAuthor TEXT NULL,
                ManualOverrideUtc TEXT NULL,
                MeasurementJson TEXT NULL,
                RawToolOutput TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityBuildSteps_Build
                ON CharacterIdentityBuildSteps (BuildId, Step);

            CREATE TABLE IF NOT EXISTS CharacterIdentityAngles (
                Id TEXT PRIMARY KEY, BuildId TEXT NOT NULL, View INTEGER NOT NULL, Status INTEGER NOT NULL,
                InputArtifactId TEXT NULL, OutputArtifactId TEXT NULL, AcceptedAttemptId TEXT NULL, ResolvedPromptText TEXT NULL,
                ResolvedModelId TEXT NULL, FailureReason TEXT NULL, MirrorDerived INTEGER NOT NULL DEFAULT 0,
                ManualConfirmationRequired INTEGER NOT NULL DEFAULT 0, ManualConfirmed INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityAngles_Build ON CharacterIdentityAngles (BuildId, View);

            CREATE TABLE IF NOT EXISTS CharacterIdentityAngleAttempts (
                Id TEXT PRIMARY KEY, AngleId TEXT NOT NULL, AttemptNumber INTEGER NOT NULL,
                InputArtifactId TEXT NOT NULL, OutputArtifactId TEXT NOT NULL, PromptText TEXT NULL,
                ResolvedModelId TEXT NULL, Status INTEGER NOT NULL, MirrorDerived INTEGER NOT NULL DEFAULT 0,
                ManualOverrideApplied INTEGER NOT NULL DEFAULT 0, ManualOverrideReason TEXT NULL,
                ManualOverrideAuthor TEXT NULL, ManualOverrideUtc TEXT NULL,
                FailureReason TEXT NULL, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityAngleAttempts_Angle ON CharacterIdentityAngleAttempts (AngleId, AttemptNumber);

            CREATE TABLE IF NOT EXISTS CharacterIdentityBodyViews (
                Id TEXT PRIMARY KEY, BuildId TEXT NOT NULL, State INTEGER NOT NULL, BodyView INTEGER NULL,
                RotationDeg INTEGER NULL, PositionKey TEXT NULL, Status INTEGER NOT NULL,
                InputArtifactId TEXT NULL, OutputArtifactId TEXT NULL, ResolvedPromptText TEXT NULL,
                ResolvedModelId TEXT NULL, FailureReason TEXT NULL, ManualOverrideApplied INTEGER NOT NULL DEFAULT 0,
                ManualOverrideReason TEXT NULL, ManualOverrideAuthor TEXT NULL, ManualOverrideUtc TEXT NULL,
                CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CharacterIdentityBodyViews_Build ON CharacterIdentityBodyViews (BuildId, State);

            CREATE TABLE IF NOT EXISTS CharacterIdentityStepPlans (
                Kind INTEGER NOT NULL,
                Step INTEGER NOT NULL,
                OrderIndex INTEGER NOT NULL,
                HandlerKey TEXT NOT NULL,
                TemplateKey TEXT NULL,
                PRIMARY KEY (Kind, Step)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // B-122 B122-011/012: the reviewer's findings and the shared quality result attach to the body view's own
        // row. Additive columns, because a database created by Section B already has the table without them.
        var bodyViewColumns = await QueryColumnsAsync(connection, "CharacterIdentityBodyViews", cancellationToken);
        foreach (var (column, definition) in new[]
                 {
                     ("ShapeVerdict", "INTEGER NOT NULL DEFAULT 0"),
                     ("TattooVerdict", "INTEGER NOT NULL DEFAULT 0"),
                     ("SkinVerdict", "INTEGER NOT NULL DEFAULT 0"),
                     ("AnatomyVerdict", "INTEGER NOT NULL DEFAULT 0"),
                     ("FindingsNote", "TEXT NULL"),
                     ("ReviewedBy", "TEXT NULL"),
                     ("ReviewedUtc", "TEXT NULL"),
                     ("QualityRating", "INTEGER NOT NULL DEFAULT 0"),
                     ("QualityNotes", "TEXT NULL")
                 })
        {
            await AddBodyViewColumnIfMissingAsync(
                connection, bodyViewColumns, column, definition, cancellationToken);
        }

        var buildColumns = await QueryColumnsAsync(connection, "CharacterIdentityBuilds", cancellationToken);
        if (!buildColumns.Contains("FrontContainerAssetId"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE CharacterIdentityBuilds ADD COLUMN FrontContainerAssetId TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!buildColumns.Contains("CanonicalFrontAssetId"))
        {
            await using var alterCanonical = connection.CreateCommand();
            alterCanonical.CommandText = "ALTER TABLE CharacterIdentityBuilds ADD COLUMN CanonicalFrontAssetId TEXT NULL;";
            await alterCanonical.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!buildColumns.Contains("ProducedIdentityPackId"))
        {
            await using var alterPack = connection.CreateCommand();
            alterPack.CommandText = "ALTER TABLE CharacterIdentityBuilds ADD COLUMN ProducedIdentityPackId TEXT NULL;";
            await alterPack.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!buildColumns.Contains("TargetKind"))
        {
            // 1 = Face: the pipeline this machinery was built for, written into the column rather than
            // assumed at read time.
            await using var alterTargetKind = connection.CreateCommand();
            alterTargetKind.CommandText = "ALTER TABLE CharacterIdentityBuilds ADD COLUMN TargetKind INTEGER NOT NULL DEFAULT 1;";
            await alterTargetKind.ExecuteNonQueryAsync(cancellationToken);
        }

        await SeedStepPlansAsync(connection, cancellationToken);
        var attemptColumns = await QueryColumnsAsync(connection, "CharacterIdentityAngleAttempts", cancellationToken);
        await AddAngleAttemptColumnIfMissingAsync(connection, attemptColumns, "ManualOverrideApplied", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddAngleAttemptColumnIfMissingAsync(connection, attemptColumns, "ManualOverrideReason", "TEXT NULL", cancellationToken);
        await AddAngleAttemptColumnIfMissingAsync(connection, attemptColumns, "ManualOverrideAuthor", "TEXT NULL", cancellationToken);
        await AddAngleAttemptColumnIfMissingAsync(connection, attemptColumns, "ManualOverrideUtc", "TEXT NULL", cancellationToken);
        await AddAngleAttemptColumnIfMissingAsync(connection, attemptColumns, "MeasurementJson", "TEXT NULL", cancellationToken);

        var stepColumns = await QueryColumnsAsync(connection, "CharacterIdentityBuildSteps", cancellationToken);
        await AddStepColumnIfMissingAsync(connection, stepColumns, "ManualOverrideAuthor", "TEXT NULL", cancellationToken);
        await AddStepColumnIfMissingAsync(connection, stepColumns, "ManualOverrideUtc", "TEXT NULL", cancellationToken);
        await AddStepColumnIfMissingAsync(connection, stepColumns, "MeasurementJson", "TEXT NULL", cancellationToken);
        await AddStepColumnIfMissingAsync(connection, stepColumns, "RawToolOutput", "TEXT NULL", cancellationToken);

        var angleColumns = await QueryColumnsAsync(connection, "CharacterIdentityAngles", cancellationToken);
        if (!angleColumns.Contains("AcceptedAttemptId"))
        {
            await using var alterAngle = connection.CreateCommand();
            alterAngle.CommandText = "ALTER TABLE CharacterIdentityAngles ADD COLUMN AcceptedAttemptId TEXT NULL;";
            await alterAngle.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Writes the step plans this app ships. Unlike a prompt template — whose body the user owns, so the seed
    /// only inserts — a step plan is structure the app must be able to run: it names a handler and a step, and
    /// a stale row would leave a build walking a step the code no longer implements, so the shipped plan wins.
    /// </summary>
    private static async Task SeedStepPlansAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // The Face plan: the pipeline this machinery was built for. Seed data for the rows — never the runtime
        // order, which is always the persisted plan of the build's target kind.
        (int Order, CharacterIdentityBuildStep Step, string HandlerKey, string? TemplateKey)[] facePlan =
        [
            (1, CharacterIdentityBuildStep.Front, CharacterIdentityBuildHandlers.Front, "identity.front.generate"),
            (2, CharacterIdentityBuildStep.Validate, CharacterIdentityBuildHandlers.ValidateEye, null),
            (3, CharacterIdentityBuildStep.GarmentRemoval, CharacterIdentityBuildHandlers.GarmentRemoval, "identity.garment.remove"),
            (4, CharacterIdentityBuildStep.Crop, CharacterIdentityBuildHandlers.Crop, null),
            (5, CharacterIdentityBuildStep.Enhance, CharacterIdentityBuildHandlers.Enhance, null),
            (6, CharacterIdentityBuildStep.Angles, CharacterIdentityBuildHandlers.AngleFace, null),
            (7, CharacterIdentityBuildStep.Promote, CharacterIdentityBuildHandlers.PromoteFacePack, null)
        ];

        foreach (var (order, step, handlerKey, templateKey) in facePlan)
        {
            await UpsertStepPlanRowAsync(connection, CharacterIdentityTargetKind.Face, order, step, handlerKey, templateKey, cancellationToken);
        }

        // The Body plan (B-122 Phase 0): acquire the base → validate the base → one requested view → validate the
        // views → promote. Same machinery, same step records; only the plan, the handlers and the template keys
        // differ, which is the whole point of B-121's target-extensible step set (FR21-035). The view step's
        // template key is null because each view resolves its own key, exactly as the face angle step does.
        (int Order, CharacterIdentityBuildStep Step, string HandlerKey, string? TemplateKey)[] bodyPlan =
        [
            (1, CharacterIdentityBuildStep.Front, CharacterIdentityBuildHandlers.FrontBody, CharacterBodyWorkflowKeys.ClothedAcquire),
            (2, CharacterIdentityBuildStep.Validate, CharacterIdentityBuildHandlers.ValidateBodyBase, null),
            (3, CharacterIdentityBuildStep.Angles, CharacterIdentityBuildHandlers.AngleBody, null),
            (4, CharacterIdentityBuildStep.ValidateView, CharacterIdentityBuildHandlers.ValidateBodyView, null),
            (5, CharacterIdentityBuildStep.Promote, CharacterIdentityBuildHandlers.PromoteBodyPack, null)
        ];

        foreach (var (order, step, handlerKey, templateKey) in bodyPlan)
        {
            await UpsertStepPlanRowAsync(connection, CharacterIdentityTargetKind.Body, order, step, handlerKey, templateKey, cancellationToken);
        }
    }

    private static async Task UpsertStepPlanRowAsync(
        SqliteConnection connection,
        CharacterIdentityTargetKind kind,
        int order,
        CharacterIdentityBuildStep step,
        string handlerKey,
        string? templateKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterIdentityStepPlans (Kind, Step, OrderIndex, HandlerKey, TemplateKey)
            VALUES ($kind, $step, $orderIndex, $handlerKey, $templateKey)
            ON CONFLICT(Kind, Step) DO UPDATE SET
                OrderIndex = excluded.OrderIndex,
                HandlerKey = excluded.HandlerKey,
                TemplateKey = excluded.TemplateKey;
            """;
        command.Parameters.AddWithValue("$kind", (int)kind);
        command.Parameters.AddWithValue("$step", (int)step);
        command.Parameters.AddWithValue("$orderIndex", order);
        command.Parameters.AddWithValue("$handlerKey", handlerKey);
        command.Parameters.AddWithValue("$templateKey", (object?)templateKey ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AddStepColumnIfMissingAsync(
        SqliteConnection connection,
        HashSet<string> existingColumns,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        if (existingColumns.Contains(column))
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE CharacterIdentityBuildSteps ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AddBodyViewColumnIfMissingAsync(
        SqliteConnection connection, HashSet<string> existingColumns, string column, string definition,
        CancellationToken cancellationToken)
    {
        if (existingColumns.Contains(column))
            return;
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE CharacterIdentityBodyViews ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AddAngleAttemptColumnIfMissingAsync(
        SqliteConnection connection, HashSet<string> existingColumns, string column, string definition,
        CancellationToken cancellationToken)
    {
        if (existingColumns.Contains(column))
            return;
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE CharacterIdentityAngleAttempts ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CharacterIdentityBuild ReadBuild(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        CharacterTemplateId = reader.GetString(1),
        BatchId = reader.IsDBNull(2) ? null : reader.GetString(2),
        FrontContainerAssetId = reader.IsDBNull(3) ? null : reader.GetString(3),
    CanonicalFrontAssetId = reader.IsDBNull(8) ? null : reader.GetString(8),
    ProducedIdentityPackId = reader.IsDBNull(9) ? null : reader.GetString(9),
        CurrentStep = (CharacterIdentityBuildStep)reader.GetInt32(4),
        Status = (CharacterIdentityBuildStatus)reader.GetInt32(5),
        CreatedUtc = ParseUtc(reader.GetString(6)),
        UpdatedUtc = ParseUtc(reader.GetString(7)),
        TargetKind = (CharacterIdentityTargetKind)reader.GetInt32(10)
    };

    private static CharacterIdentityBuildStepRecord ReadStep(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        BuildId = reader.GetString(1),
        Step = (CharacterIdentityBuildStep)reader.GetInt32(2),
        Status = (CharacterIdentityBuildStepStatus)reader.GetInt32(3),
        InputArtifactId = reader.IsDBNull(4) ? null : reader.GetString(4),
        OutputArtifactId = reader.IsDBNull(5) ? null : reader.GetString(5),
        ResolvedPromptText = reader.IsDBNull(6) ? null : reader.GetString(6),
        ResolvedModelId = reader.IsDBNull(7) ? null : reader.GetString(7),
        FailureReason = reader.IsDBNull(8) ? null : reader.GetString(8),
        MirrorDerived = reader.GetInt32(9) != 0,
        ManualOverrideApplied = reader.GetInt32(10) != 0,
        ManualOverrideReason = reader.IsDBNull(11) ? null : reader.GetString(11),
        ManualOverrideAuthor = reader.IsDBNull(12) ? null : reader.GetString(12),
        ManualOverrideUtc = reader.IsDBNull(13) ? null : ParseUtc(reader.GetString(13)),
        MeasurementJson = reader.IsDBNull(14) ? null : reader.GetString(14),
        RawToolOutput = reader.IsDBNull(15) ? null : reader.GetString(15),
        CreatedUtc = ParseUtc(reader.GetString(16)),
        UpdatedUtc = ParseUtc(reader.GetString(17))
    };

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
