using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneImageProductionGroupRepository : ISceneImageProductionGroupRepository
{
    private readonly string _connectionString;

    public SceneImageProductionGroupRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<SceneImageAttemptRetentionPolicy?> GetRetentionPolicyAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Mode, RejectedRetentionDays, UpdatedBy, UpdatedUtc, Version FROM SceneImageAttemptRetentionPolicies WHERE Id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SceneImageAttemptRetentionPolicy
        {
            Mode = Enum.Parse<SceneImageAttemptRetentionMode>(reader.GetString(0)),
            RejectedRetentionDays = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            UpdatedBy = reader.GetString(2),
            UpdatedUtc = ParseUtc(reader.GetString(3)),
            Version = reader.GetInt64(4)
        };
    }

    public async Task<SceneImageAttemptRetentionPolicy> SaveRetentionPolicyAsync(
        SceneImageAttemptRetentionPolicy policy,
        long? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateRetentionPolicy(policy);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (expectedVersion is null)
        {
            command.CommandText = """
                INSERT INTO SceneImageAttemptRetentionPolicies
                    (Id, Mode, RejectedRetentionDays, UpdatedBy, UpdatedUtc, Version)
                VALUES (1, $mode, $days, $updatedBy, $updatedUtc, 1)
                ON CONFLICT(Id) DO NOTHING;
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE SceneImageAttemptRetentionPolicies
                SET Mode = $mode, RejectedRetentionDays = $days, UpdatedBy = $updatedBy,
                    UpdatedUtc = $updatedUtc, Version = Version + 1
                WHERE Id = 1 AND Version = $expectedVersion;
                """;
            command.Parameters.AddWithValue("$expectedVersion", expectedVersion.Value);
        }
        command.Parameters.AddWithValue("$mode", policy.Mode.ToString());
        command.Parameters.AddWithValue("$days", (object?)policy.RejectedRetentionDays ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedBy", policy.UpdatedBy.Trim());
        command.Parameters.AddWithValue("$updatedUtc", FormatUtc(policy.UpdatedUtc));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Scene image retention policy changed concurrently or already exists.");
        await transaction.CommitAsync(cancellationToken);
        return (await GetRetentionPolicyAsync(cancellationToken))!;
    }

    public async Task CreateAsync(
        SceneImageProductionGroup group,
        CancellationToken cancellationToken = default)
    {
        Validate(group);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        await using (var verify = connection.CreateCommand())
        {
            verify.Transaction = transaction;
            verify.CommandText = """
                SELECT CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                       MomentSetId, MomentSetVersion, MomentId, Revision
                FROM SceneMomentEnrichments enrichment
                WHERE enrichment.Id = $enrichmentId
                  AND enrichment.Status = 'Complete'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM SceneMomentEnrichments current
                      WHERE current.MomentSetId = enrichment.MomentSetId
                        AND current.MomentId = enrichment.MomentId
                        AND current.Id <> enrichment.Id
                        AND current.Status NOT IN ('Superseded', 'Cancelled'));
                """;
            verify.Parameters.AddWithValue("$enrichmentId", group.MomentEnrichmentId.Trim());

            await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Moment Enrichment '{group.MomentEnrichmentId}' must exist, be complete, and be current before an image production group can be created.");
            }

            EnsureEqual(group.CatalogueId, reader.GetString(0), "CatalogueId");
            EnsureEqual(group.BeatId, reader.GetString(1), "BeatId");
            EnsureEqual(group.BeatProductionPlanId, reader.GetString(2), "BeatProductionPlanId");
            EnsureEqual(group.BeatProductionPlanVersion, reader.GetInt32(3), "BeatProductionPlanVersion");
            EnsureEqual(group.MomentSetId, reader.GetString(4), "MomentSetId");
            EnsureEqual(group.MomentSetVersion, reader.GetInt32(5), "MomentSetVersion");
            EnsureEqual(group.MomentId, reader.GetString(6), "MomentId");
            EnsureEqual(group.MomentEnrichmentRevision, reader.GetInt32(7), "MomentEnrichmentRevision");
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO SceneImageProductionGroups (
                Id, SessionId, InteractionId, CatalogueId, BeatId,
                BeatProductionPlanId, BeatProductionPlanVersion,
                MomentSetId, MomentSetVersion, MomentId,
                MomentEnrichmentId, MomentEnrichmentRevision, Pov,
                CameraIntentSnapshotJson, Status, IdentityPolicy, IdentitySkipReason,
                CurrentApprovedDecisionId, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $sessionId, $interactionId, $catalogueId, $beatId,
                $planId, $planVersion, $momentSetId, $momentSetVersion, $momentId,
                $enrichmentId, $enrichmentRevision, $pov,
                $cameraIntent, $status, $identityPolicy, $identitySkipReason,
                $currentDecisionId, $createdUtc, $updatedUtc);
            """;
        AddGroupParameters(insert, group);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SceneImageProductionGroup?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        Require(id, "Production group id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateSelect(connection);
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.Trim());
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<SceneImageProductionGroup?> GetCurrentAsync(
        string momentEnrichmentId,
        string pov,
        CancellationToken cancellationToken = default)
    {
        Require(momentEnrichmentId, "Moment Enrichment id");
        Require(pov, "POV");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateSelect(connection);
        command.CommandText += """
             WHERE MomentEnrichmentId = $enrichmentId
               AND Pov = $pov COLLATE NOCASE
               AND Status <> 'Archived'
             LIMIT 1;
            """;
        command.Parameters.AddWithValue("$enrichmentId", momentEnrichmentId.Trim());
        command.Parameters.AddWithValue("$pov", pov.Trim());
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<SceneImageProductionGroup>> ListByInteractionAsync(
        string sessionId,
        string interactionId,
        CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Session id");
        Require(interactionId, "Interaction id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateSelect(connection);
        command.CommandText += """
             WHERE SessionId = $sessionId AND InteractionId = $interactionId
             ORDER BY CreatedUtc DESC;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", interactionId.Trim());

        var groups = new List<SceneImageProductionGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            groups.Add(Read(reader));
        }

        return groups;
    }

    public async Task<ApprovedSceneFrameDecision?> GetApprovalDecisionAsync(
        string decisionId,
        CancellationToken cancellationToken = default)
    {
        Require(decisionId, "Approval decision id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateDecisionSelect(connection);
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", decisionId.Trim());
        return await ReadDecisionAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovedSceneFrameDecision>> ListApprovalDecisionsAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        Require(groupId, "Production group id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateDecisionSelect(connection);
        command.CommandText += " WHERE ProductionGroupId = $groupId ORDER BY Version ASC;";
        command.Parameters.AddWithValue("$groupId", groupId.Trim());

        var decisions = new List<ApprovedSceneFrameDecision>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) decisions.Add(ReadDecision(reader));
        return decisions;
    }

    public async Task<ApprovedSceneFrameDecision> ApproveAsync(
        string groupId,
        string imageId,
        string sha256,
        string decidedBy,
        string? note,
        DateTime decisionUtc,
        CancellationToken cancellationToken = default)
    {
        Require(groupId, "Production group id");
        Require(imageId, "Scene image id");
        Require(sha256, "Scene image SHA-256");
        Require(decidedBy, "Approval decision actor");
        _ = FormatUtc(decisionUtc);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var group = await LoadGroupForDecisionAsync(connection, transaction, groupId, cancellationToken);
        if (group.Status == SceneImageProductionGroupStatus.Archived)
            throw new InvalidOperationException($"Production group '{group.Id}' is archived and cannot be approved.");

        var image = await LoadApprovalImageAsync(connection, transaction, imageId, cancellationToken);
        ValidateApprovalImage(group, image, sha256);
        var version = await GetNextDecisionVersionAsync(connection, transaction, group.Id, cancellationToken);
        await SupersedeCurrentDecisionAsync(connection, transaction, group, cancellationToken);

        var decision = CreateDecision(group, image.Id, image.Sha256!, version, ApprovedSceneFrameDecisionState.Approved, decidedBy, note, decisionUtc);
        await InsertDecisionAsync(connection, transaction, decision, cancellationToken);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE SceneImageProductionGroups
            SET CurrentApprovedDecisionId = $decisionId, Status = 'Approved', UpdatedUtc = $updatedUtc
            WHERE Id = $groupId
              AND Status <> 'Archived'
              AND (($expectedDecisionId IS NULL AND CurrentApprovedDecisionId IS NULL) OR CurrentApprovedDecisionId = $expectedDecisionId);
            """;
        update.Parameters.AddWithValue("$decisionId", decision.Id);
        update.Parameters.AddWithValue("$updatedUtc", FormatUtc(decisionUtc));
        update.Parameters.AddWithValue("$groupId", group.Id);
        update.Parameters.AddWithValue("$expectedDecisionId", (object?)group.CurrentApprovedDecisionId ?? DBNull.Value);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Production group '{group.Id}' approval changed concurrently.");

        await transaction.CommitAsync(cancellationToken);
        return decision;
    }

    public async Task<ApprovedSceneFrameDecision> RevokeCurrentApprovalAsync(
        string groupId,
        string decidedBy,
        string? note,
        DateTime decisionUtc,
        CancellationToken cancellationToken = default)
    {
        Require(groupId, "Production group id");
        Require(decidedBy, "Approval decision actor");
        _ = FormatUtc(decisionUtc);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var group = await LoadGroupForDecisionAsync(connection, transaction, groupId, cancellationToken);
        if (group.Status == SceneImageProductionGroupStatus.Archived)
            throw new InvalidOperationException($"Production group '{group.Id}' is archived and cannot revoke approval.");
        if (string.IsNullOrWhiteSpace(group.CurrentApprovedDecisionId))
            throw new InvalidOperationException($"Production group '{group.Id}' has no current approval to revoke.");

        var current = await LoadDecisionAsync(connection, transaction, group.CurrentApprovedDecisionId, cancellationToken);
        if (current.Decision != ApprovedSceneFrameDecisionState.Approved)
            throw new InvalidOperationException($"Production group '{group.Id}' current approval is not Approved.");
        await SupersedeCurrentDecisionAsync(connection, transaction, group, cancellationToken);
        var version = await GetNextDecisionVersionAsync(connection, transaction, group.Id, cancellationToken);
        var revoked = CreateDecision(group, current.SceneImageId, current.Sha256, version, ApprovedSceneFrameDecisionState.Revoked, decidedBy, note, decisionUtc);
        await InsertDecisionAsync(connection, transaction, revoked, cancellationToken);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE SceneImageProductionGroups
            SET CurrentApprovedDecisionId = NULL, Status = 'Review', UpdatedUtc = $updatedUtc
            WHERE Id = $groupId AND CurrentApprovedDecisionId = $expectedDecisionId AND Status = 'Approved';
            """;
        update.Parameters.AddWithValue("$updatedUtc", FormatUtc(decisionUtc));
        update.Parameters.AddWithValue("$groupId", group.Id);
        update.Parameters.AddWithValue("$expectedDecisionId", group.CurrentApprovedDecisionId);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Production group '{group.Id}' approval changed concurrently.");

        await transaction.CommitAsync(cancellationToken);
        return revoked;
    }

    private static async Task<SceneImageProductionGroup> LoadGroupForDecisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, string groupId, CancellationToken cancellationToken)
    {
        await using var command = CreateSelect(connection);
        command.Transaction = transaction;
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", groupId.Trim());
        return await ReadAsync(command, cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{groupId}' was not found.");
    }

    private static async Task<SceneImageRecord> LoadApprovalImageAsync(
        SqliteConnection connection, SqliteTransaction transaction, string imageId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, Status, ProductionGroupId, Disposition, Sha256,
                   CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                   MomentSetId, MomentSetVersion, MomentId, MomentEnrichmentId, MomentEnrichmentRevision
            FROM SceneImages WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", imageId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Scene image '{imageId}' was not found.");
        return new SceneImageRecord
        {
            Id = reader.GetString(0),
            Status = Enum.Parse<SceneImageStatus>(reader.GetString(1)),
            ProductionGroupId = reader.IsDBNull(2) ? null : reader.GetString(2),
            Disposition = reader.IsDBNull(3) ? null : Enum.Parse<SceneImageAttemptDisposition>(reader.GetString(3)),
            Sha256 = reader.IsDBNull(4) ? null : reader.GetString(4),
            CatalogueId = reader.IsDBNull(5) ? null : reader.GetString(5),
            BeatId = reader.IsDBNull(6) ? null : reader.GetString(6),
            BeatProductionPlanId = reader.IsDBNull(7) ? null : reader.GetString(7),
            BeatProductionPlanVersion = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            MomentSetId = reader.IsDBNull(9) ? null : reader.GetString(9),
            MomentSetVersion = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            MomentId = reader.IsDBNull(11) ? null : reader.GetString(11),
            MomentEnrichmentId = reader.IsDBNull(12) ? null : reader.GetString(12),
            MomentEnrichmentRevision = reader.IsDBNull(13) ? null : reader.GetInt32(13)
        };
    }

    private static void ValidateApprovalImage(SceneImageProductionGroup group, SceneImageRecord image, string sha256)
    {
        if (image.Status != SceneImageStatus.Complete)
            throw new InvalidOperationException("Only a completed scene image can be approved.");
        if (image.Disposition is SceneImageAttemptDisposition.Rejected or SceneImageAttemptDisposition.Archived)
            throw new InvalidOperationException("Rejected or archived scene images cannot be approved.");
        EnsureEqual(image.ProductionGroupId ?? string.Empty, group.Id, "ProductionGroupId");
        if (string.IsNullOrWhiteSpace(image.Sha256) || !string.Equals(image.Sha256, sha256.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("Approval SHA-256 must exactly match the persisted scene image checksum.");
        EnsureEqual(image.CatalogueId ?? string.Empty, group.CatalogueId, "CatalogueId");
        EnsureEqual(image.BeatId ?? string.Empty, group.BeatId, "BeatId");
        EnsureEqual(image.BeatProductionPlanId ?? string.Empty, group.BeatProductionPlanId, "BeatProductionPlanId");
        EnsureEqual(image.BeatProductionPlanVersion ?? 0, group.BeatProductionPlanVersion, "BeatProductionPlanVersion");
        EnsureEqual(image.MomentSetId ?? string.Empty, group.MomentSetId, "MomentSetId");
        EnsureEqual(image.MomentSetVersion ?? 0, group.MomentSetVersion, "MomentSetVersion");
        EnsureEqual(image.MomentId ?? string.Empty, group.MomentId, "MomentId");
        EnsureEqual(image.MomentEnrichmentId ?? string.Empty, group.MomentEnrichmentId, "MomentEnrichmentId");
        EnsureEqual(image.MomentEnrichmentRevision ?? 0, group.MomentEnrichmentRevision, "MomentEnrichmentRevision");
    }

    private static async Task<int> GetNextDecisionVersionAsync(
        SqliteConnection connection, SqliteTransaction transaction, string groupId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) + 1 FROM ApprovedSceneFrameDecisions WHERE ProductionGroupId = $groupId;";
        command.Parameters.AddWithValue("$groupId", groupId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task SupersedeCurrentDecisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, SceneImageProductionGroup group, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(group.CurrentApprovedDecisionId)) return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE ApprovedSceneFrameDecisions SET Decision = 'Superseded' WHERE Id = $id AND ProductionGroupId = $groupId AND Decision = 'Approved';";
        command.Parameters.AddWithValue("$id", group.CurrentApprovedDecisionId);
        command.Parameters.AddWithValue("$groupId", group.Id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Production group '{group.Id}' current approval is inconsistent.");
    }

    private static ApprovedSceneFrameDecision CreateDecision(
        SceneImageProductionGroup group, string imageId, string sha256, int version,
        ApprovedSceneFrameDecisionState state, string decidedBy, string? note, DateTime decisionUtc)
        => new()
        {
            ProductionGroupId = group.Id,
            Version = version,
            SceneImageId = imageId,
            Sha256 = sha256,
            CatalogueId = group.CatalogueId,
            BeatId = group.BeatId,
            BeatProductionPlanId = group.BeatProductionPlanId,
            BeatProductionPlanVersion = group.BeatProductionPlanVersion,
            MomentSetId = group.MomentSetId,
            MomentSetVersion = group.MomentSetVersion,
            MomentId = group.MomentId,
            MomentEnrichmentId = group.MomentEnrichmentId,
            MomentEnrichmentRevision = group.MomentEnrichmentRevision,
            Decision = state,
            DecidedBy = decidedBy.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            DecisionUtc = decisionUtc
        };

    private static async Task InsertDecisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, ApprovedSceneFrameDecision decision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ApprovedSceneFrameDecisions (
                Id, ProductionGroupId, Version, SceneImageId, Sha256, CatalogueId, BeatId,
                BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                MomentId, MomentEnrichmentId, MomentEnrichmentRevision, Decision, DecidedBy, Note, DecisionUtc)
            VALUES ($id, $groupId, $version, $imageId, $sha256, $catalogueId, $beatId,
                $planId, $planVersion, $momentSetId, $momentSetVersion,
                $momentId, $enrichmentId, $enrichmentRevision, $decision, $decidedBy, $note, $decisionUtc);
            """;
        command.Parameters.AddWithValue("$id", decision.Id);
        command.Parameters.AddWithValue("$groupId", decision.ProductionGroupId);
        command.Parameters.AddWithValue("$version", decision.Version);
        command.Parameters.AddWithValue("$imageId", decision.SceneImageId);
        command.Parameters.AddWithValue("$sha256", decision.Sha256);
        command.Parameters.AddWithValue("$catalogueId", decision.CatalogueId);
        command.Parameters.AddWithValue("$beatId", decision.BeatId);
        command.Parameters.AddWithValue("$planId", decision.BeatProductionPlanId);
        command.Parameters.AddWithValue("$planVersion", decision.BeatProductionPlanVersion);
        command.Parameters.AddWithValue("$momentSetId", decision.MomentSetId);
        command.Parameters.AddWithValue("$momentSetVersion", decision.MomentSetVersion);
        command.Parameters.AddWithValue("$momentId", decision.MomentId);
        command.Parameters.AddWithValue("$enrichmentId", decision.MomentEnrichmentId);
        command.Parameters.AddWithValue("$enrichmentRevision", decision.MomentEnrichmentRevision);
        command.Parameters.AddWithValue("$decision", decision.Decision.ToString());
        command.Parameters.AddWithValue("$decidedBy", decision.DecidedBy);
        command.Parameters.AddWithValue("$note", (object?)decision.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$decisionUtc", FormatUtc(decision.DecisionUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateDecisionSelect(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProductionGroupId, Version, SceneImageId, Sha256, CatalogueId, BeatId,
                   BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion,
                   MomentId, MomentEnrichmentId, MomentEnrichmentRevision, Decision, DecidedBy, Note, DecisionUtc
            FROM ApprovedSceneFrameDecisions
            """;
        return command;
    }

    private static async Task<ApprovedSceneFrameDecision?> ReadDecisionAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDecision(reader) : null;
    }

    private static async Task<ApprovedSceneFrameDecision> LoadDecisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, string decisionId, CancellationToken cancellationToken)
    {
        await using var command = CreateDecisionSelect(connection);
        command.Transaction = transaction;
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", decisionId);
        return await ReadDecisionAsync(command, cancellationToken)
            ?? throw new InvalidOperationException($"Approval decision '{decisionId}' was not found.");
    }

    private static ApprovedSceneFrameDecision ReadDecision(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetString(0), ProductionGroupId = reader.GetString(1), Version = reader.GetInt32(2),
            SceneImageId = reader.GetString(3), Sha256 = reader.GetString(4), CatalogueId = reader.GetString(5),
            BeatId = reader.GetString(6), BeatProductionPlanId = reader.GetString(7), BeatProductionPlanVersion = reader.GetInt32(8),
            MomentSetId = reader.GetString(9), MomentSetVersion = reader.GetInt32(10), MomentId = reader.GetString(11),
            MomentEnrichmentId = reader.GetString(12), MomentEnrichmentRevision = reader.GetInt32(13),
            Decision = Enum.Parse<ApprovedSceneFrameDecisionState>(reader.GetString(14)), DecidedBy = reader.GetString(15),
            Note = reader.IsDBNull(16) ? null : reader.GetString(16), DecisionUtc = ParseUtc(reader.GetString(17))
        };

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static SqliteCommand CreateSelect(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, CatalogueId, BeatId,
                   BeatProductionPlanId, BeatProductionPlanVersion,
                   MomentSetId, MomentSetVersion, MomentId,
                   MomentEnrichmentId, MomentEnrichmentRevision, Pov,
                   CameraIntentSnapshotJson, Status, IdentityPolicy, IdentitySkipReason,
                   CurrentApprovedDecisionId, CreatedUtc, UpdatedUtc
            FROM SceneImageProductionGroups
            """;
        return command;
    }

    private static async Task<SceneImageProductionGroup?> ReadAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static SceneImageProductionGroup Read(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetString(0),
            SessionId = reader.GetString(1),
            InteractionId = reader.GetString(2),
            CatalogueId = reader.GetString(3),
            BeatId = reader.GetString(4),
            BeatProductionPlanId = reader.GetString(5),
            BeatProductionPlanVersion = reader.GetInt32(6),
            MomentSetId = reader.GetString(7),
            MomentSetVersion = reader.GetInt32(8),
            MomentId = reader.GetString(9),
            MomentEnrichmentId = reader.GetString(10),
            MomentEnrichmentRevision = reader.GetInt32(11),
            Pov = reader.GetString(12),
            CameraIntentSnapshotJson = reader.IsDBNull(13) ? null : reader.GetString(13),
            Status = Enum.Parse<SceneImageProductionGroupStatus>(reader.GetString(14)),
            IdentityPolicy = Enum.Parse<SceneImageIdentityPolicy>(reader.GetString(15)),
            IdentitySkipReason = reader.IsDBNull(16) ? null : reader.GetString(16),
            CurrentApprovedDecisionId = reader.IsDBNull(17) ? null : reader.GetString(17),
            CreatedUtc = ParseUtc(reader.GetString(18)),
            UpdatedUtc = ParseUtc(reader.GetString(19))
        };

    private static void AddGroupParameters(SqliteCommand command, SceneImageProductionGroup group)
    {
        command.Parameters.AddWithValue("$id", group.Id.Trim());
        command.Parameters.AddWithValue("$sessionId", group.SessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", group.InteractionId.Trim());
        command.Parameters.AddWithValue("$catalogueId", group.CatalogueId.Trim());
        command.Parameters.AddWithValue("$beatId", group.BeatId.Trim());
        command.Parameters.AddWithValue("$planId", group.BeatProductionPlanId.Trim());
        command.Parameters.AddWithValue("$planVersion", group.BeatProductionPlanVersion);
        command.Parameters.AddWithValue("$momentSetId", group.MomentSetId.Trim());
        command.Parameters.AddWithValue("$momentSetVersion", group.MomentSetVersion);
        command.Parameters.AddWithValue("$momentId", group.MomentId.Trim());
        command.Parameters.AddWithValue("$enrichmentId", group.MomentEnrichmentId.Trim());
        command.Parameters.AddWithValue("$enrichmentRevision", group.MomentEnrichmentRevision);
        command.Parameters.AddWithValue("$pov", group.Pov.Trim());
        command.Parameters.AddWithValue("$cameraIntent", (object?)group.CameraIntentSnapshotJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", group.Status.ToString());
        command.Parameters.AddWithValue("$identityPolicy", group.IdentityPolicy.ToString());
        command.Parameters.AddWithValue("$identitySkipReason", (object?)group.IdentitySkipReason?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$currentDecisionId", (object?)group.CurrentApprovedDecisionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(group.CreatedUtc));
        command.Parameters.AddWithValue("$updatedUtc", FormatUtc(group.UpdatedUtc));
    }

    private static void Validate(SceneImageProductionGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Require(group.Id, "Production group id");
        Require(group.SessionId, "Session id");
        Require(group.InteractionId, "Interaction id");
        Require(group.CatalogueId, "Catalogue id");
        Require(group.BeatId, "Beat id");
        Require(group.BeatProductionPlanId, "Beat Production Plan id");
        Require(group.MomentSetId, "Moment Set id");
        Require(group.MomentId, "Moment id");
        Require(group.MomentEnrichmentId, "Moment Enrichment id");
        Require(group.Pov, "POV");

        if (group.BeatProductionPlanVersion < 1
            || group.MomentSetVersion < 1
            || group.MomentEnrichmentRevision < 1)
        {
            throw new InvalidOperationException("Production group lineage versions must be positive.");
        }

        if (!Enum.IsDefined(group.Status))
            throw new InvalidOperationException("Production group status is invalid.");
        if (!Enum.IsDefined(group.IdentityPolicy))
            throw new InvalidOperationException("Production group identity policy is invalid.");
        if (group.IdentityPolicy == SceneImageIdentityPolicy.Required
            && group.IdentitySkipReason is not null)
        {
            throw new InvalidOperationException("Required identity policy cannot have an identity skip reason.");
        }
        if (group.IdentityPolicy == SceneImageIdentityPolicy.SkippedByUser
            && string.IsNullOrWhiteSpace(group.IdentitySkipReason))
        {
            throw new InvalidOperationException("Skipped-by-user identity policy requires a nonblank reason.");
        }

        _ = FormatUtc(group.CreatedUtc);
        _ = FormatUtc(group.UpdatedUtc);
    }

    private static void ValidateRetentionPolicy(SceneImageAttemptRetentionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!Enum.IsDefined(policy.Mode))
            throw new InvalidOperationException("Scene image retention mode is invalid.");
        if (policy.Mode == SceneImageAttemptRetentionMode.Manual && policy.RejectedRetentionDays is not null)
            throw new InvalidOperationException("Manual retention policy requires RejectedRetentionDays to be null.");
        if (policy.Mode == SceneImageAttemptRetentionMode.Automatic
            && (policy.RejectedRetentionDays is null or < 1 or > 3650))
            throw new InvalidOperationException("Automatic retention policy requires explicit RejectedRetentionDays from 1 through 3650.");
        Require(policy.UpdatedBy, "Retention policy actor");
        _ = FormatUtc(policy.UpdatedUtc);
    }

    private static void EnsureEqual(string actual, string expected, string field)
    {
        if (!string.Equals(actual.Trim(), expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Production group {field} does not match the Moment Enrichment lineage.");
    }

    private static void EnsureEqual(int actual, int expected, string field)
    {
        if (actual != expected)
            throw new InvalidOperationException($"Production group {field} does not match the Moment Enrichment lineage.");
    }

    private static string FormatUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value.ToString("O", CultureInfo.InvariantCulture)
            : throw new InvalidOperationException("Persistence timestamps must be UTC.");

    private static DateTime ParseUtc(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
    }

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SceneImageProductionGroups (
            Id TEXT PRIMARY KEY,
            SessionId TEXT NOT NULL,
            InteractionId TEXT NOT NULL,
            CatalogueId TEXT NOT NULL,
            BeatId TEXT NOT NULL,
            BeatProductionPlanId TEXT NOT NULL,
            BeatProductionPlanVersion INTEGER NOT NULL CHECK (BeatProductionPlanVersion > 0),
            MomentSetId TEXT NOT NULL,
            MomentSetVersion INTEGER NOT NULL CHECK (MomentSetVersion > 0),
            MomentId TEXT NOT NULL,
            MomentEnrichmentId TEXT NOT NULL,
            MomentEnrichmentRevision INTEGER NOT NULL CHECK (MomentEnrichmentRevision > 0),
            Pov TEXT NOT NULL CHECK (length(trim(Pov)) > 0),
            CameraIntentSnapshotJson TEXT NULL,
            Status TEXT NOT NULL CHECK (Status IN ('Draft', 'InProgress', 'Review', 'Approved', 'Archived')),
            IdentityPolicy TEXT NOT NULL CHECK (IdentityPolicy IN ('Required', 'SkippedByUser')),
            IdentitySkipReason TEXT NULL,
            CurrentApprovedDecisionId TEXT NULL,
            CreatedUtc TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL,
            CHECK (
                (IdentityPolicy = 'Required' AND IdentitySkipReason IS NULL)
                OR (IdentityPolicy = 'SkippedByUser' AND length(trim(IdentitySkipReason)) > 0)),
            FOREIGN KEY (MomentEnrichmentId) REFERENCES SceneMomentEnrichments(Id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_SceneImageProductionGroups_CurrentMomentPov
            ON SceneImageProductionGroups (MomentEnrichmentId, Pov COLLATE NOCASE)
            WHERE Status <> 'Archived';
        CREATE INDEX IF NOT EXISTS IX_SceneImageProductionGroups_SessionInteraction
            ON SceneImageProductionGroups (SessionId, InteractionId, CreatedUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_SceneImageProductionGroups_Lineage
            ON SceneImageProductionGroups (
                CatalogueId, BeatProductionPlanId, BeatProductionPlanVersion,
                MomentSetId, MomentSetVersion, MomentId,
                MomentEnrichmentId, MomentEnrichmentRevision);

        CREATE TABLE IF NOT EXISTS ApprovedSceneFrameDecisions (
            Id TEXT PRIMARY KEY,
            ProductionGroupId TEXT NOT NULL,
            Version INTEGER NOT NULL CHECK (Version > 0),
            SceneImageId TEXT NOT NULL,
            Sha256 TEXT NOT NULL CHECK (length(trim(Sha256)) > 0),
            CatalogueId TEXT NOT NULL,
            BeatId TEXT NOT NULL,
            BeatProductionPlanId TEXT NOT NULL,
            BeatProductionPlanVersion INTEGER NOT NULL CHECK (BeatProductionPlanVersion > 0),
            MomentSetId TEXT NOT NULL,
            MomentSetVersion INTEGER NOT NULL CHECK (MomentSetVersion > 0),
            MomentId TEXT NOT NULL,
            MomentEnrichmentId TEXT NOT NULL,
            MomentEnrichmentRevision INTEGER NOT NULL CHECK (MomentEnrichmentRevision > 0),
            Decision TEXT NOT NULL CHECK (Decision IN ('Approved', 'Superseded', 'Revoked')),
            DecidedBy TEXT NOT NULL CHECK (length(trim(DecidedBy)) > 0),
            Note TEXT NULL,
            DecisionUtc TEXT NOT NULL,
            UNIQUE (ProductionGroupId, Version),
            FOREIGN KEY (ProductionGroupId) REFERENCES SceneImageProductionGroups(Id),
            FOREIGN KEY (SceneImageId) REFERENCES SceneImages(Id),
            FOREIGN KEY (MomentEnrichmentId) REFERENCES SceneMomentEnrichments(Id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_ApprovedSceneFrameDecisions_CurrentApproved
            ON ApprovedSceneFrameDecisions (ProductionGroupId)
            WHERE Decision = 'Approved';
        CREATE INDEX IF NOT EXISTS IX_ApprovedSceneFrameDecisions_SceneImage
            ON ApprovedSceneFrameDecisions (SceneImageId);
        CREATE INDEX IF NOT EXISTS IX_ApprovedSceneFrameDecisions_Lineage
            ON ApprovedSceneFrameDecisions (
                CatalogueId, BeatProductionPlanId, BeatProductionPlanVersion,
                MomentSetId, MomentSetVersion, MomentId,
                MomentEnrichmentId, MomentEnrichmentRevision);

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
}
