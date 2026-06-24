using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RolePlayStateRepository : IRolePlayStateRepository
{
    private readonly string _connectionString;

    public RolePlayStateRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<RolePlayTurn> StartTurnAsync(
        string sessionId,
        string turnKind,
        string triggerSource,
        string? initiatedByActorName,
        string? inputInteractionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to start a role-play turn.");
        }

        if (string.IsNullOrWhiteSpace(turnKind))
        {
            throw new InvalidOperationException("Turn kind is required to start a role-play turn.");
        }

        if (string.IsNullOrWhiteSpace(triggerSource))
        {
            throw new InvalidOperationException("Trigger source is required to start a role-play turn.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureTurnSchemaAsync(connection, cancellationToken);

        var turnId = Guid.NewGuid().ToString("N");
        var startedUtc = DateTime.UtcNow;
        var nextIndex = 1;

        await using (var nextIndexCommand = connection.CreateCommand())
        {
            nextIndexCommand.CommandText = "SELECT COALESCE(MAX(TurnIndex), 0) + 1 FROM RolePlayV2Turns WHERE SessionId = $sessionId;";
            nextIndexCommand.Parameters.AddWithValue("$sessionId", sessionId);
            nextIndex = Convert.ToInt32(await nextIndexCommand.ExecuteScalarAsync(cancellationToken));
        }

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText = """
                INSERT INTO RolePlayV2Turns (
                    TurnId, SessionId, TurnIndex, TurnKind, TriggerSource, InitiatedByActorName,
                    InputInteractionId, OutputInteractionIdsJson, OutputInteractionCount,
                    StartedUtc, CompletedUtc, Status, FailureReason, UpdatedUtc)
                VALUES (
                    $turnId, $sessionId, $turnIndex, $turnKind, $triggerSource, $initiatedByActorName,
                    $inputInteractionId, $outputInteractionIdsJson, $outputInteractionCount,
                    $startedUtc, NULL, $status, NULL, $updatedUtc);
                """;
            insertCommand.Parameters.AddWithValue("$turnId", turnId);
            insertCommand.Parameters.AddWithValue("$sessionId", sessionId);
            insertCommand.Parameters.AddWithValue("$turnIndex", nextIndex);
            insertCommand.Parameters.AddWithValue("$turnKind", turnKind.Trim());
            insertCommand.Parameters.AddWithValue("$triggerSource", triggerSource.Trim());
            insertCommand.Parameters.AddWithValue("$initiatedByActorName", (object?)initiatedByActorName ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$inputInteractionId", (object?)inputInteractionId ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$outputInteractionIdsJson", "[]");
            insertCommand.Parameters.AddWithValue("$outputInteractionCount", 0);
            insertCommand.Parameters.AddWithValue("$startedUtc", startedUtc.ToString("O"));
            insertCommand.Parameters.AddWithValue("$status", RolePlayTurnStatus.Started.ToString());
            insertCommand.Parameters.AddWithValue("$updatedUtc", startedUtc.ToString("O"));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return new RolePlayTurn
        {
            TurnId = turnId,
            SessionId = sessionId,
            TurnIndex = nextIndex,
            TurnKind = turnKind.Trim(),
            TriggerSource = triggerSource.Trim(),
            InitiatedByActorName = initiatedByActorName,
            InputInteractionId = inputInteractionId,
            OutputInteractionIds = [],
            StartedUtc = startedUtc,
            Status = RolePlayTurnStatus.Started
        };
    }

    public async Task CompleteTurnAsync(
        string sessionId,
        string turnId,
        IReadOnlyList<string> outputInteractionIds,
        bool succeeded,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to complete a role-play turn.");
        }

        if (string.IsNullOrWhiteSpace(turnId))
        {
            throw new InvalidOperationException("Turn id is required to complete a role-play turn.");
        }

        outputInteractionIds ??= [];
        var completedUtc = DateTime.UtcNow;
        var status = succeeded ? RolePlayTurnStatus.Completed : RolePlayTurnStatus.Failed;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureTurnSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE RolePlayV2Turns
            SET OutputInteractionIdsJson = $outputInteractionIdsJson,
                OutputInteractionCount = $outputInteractionCount,
                CompletedUtc = $completedUtc,
                Status = $status,
                FailureReason = $failureReason,
                UpdatedUtc = $updatedUtc
            WHERE SessionId = $sessionId AND TurnId = $turnId;
            """;
        command.Parameters.AddWithValue("$outputInteractionIdsJson", JsonSerializer.Serialize(outputInteractionIds));
        command.Parameters.AddWithValue("$outputInteractionCount", outputInteractionIds.Count);
        command.Parameters.AddWithValue("$completedUtc", completedUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$failureReason", succeeded ? (object)DBNull.Value : (object?)(string.IsNullOrWhiteSpace(failureReason) ? "Turn execution failed." : failureReason.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", completedUtc.ToString("O"));
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$turnId", turnId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException($"Unable to complete turn '{turnId}' for session '{sessionId}'.");
        }
    }

    public async Task<IReadOnlyList<RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to load role-play turns.");
        }

        var turns = new List<RolePlayTurn>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureTurnSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TurnId, SessionId, TurnIndex, TurnKind, TriggerSource, InitiatedByActorName,
                   InputInteractionId, OutputInteractionIdsJson, StartedUtc, CompletedUtc, Status, FailureReason
            FROM RolePlayV2Turns
            WHERE SessionId = $sessionId
            ORDER BY TurnIndex DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 500));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var statusRaw = reader.GetString(10);
            if (!Enum.TryParse<RolePlayTurnStatus>(statusRaw, out var status))
            {
                throw new InvalidOperationException($"Unknown role-play turn status '{statusRaw}' for session '{sessionId}'.");
            }

            var outputJson = reader.GetString(7);
            var outputInteractionIds = JsonSerializer.Deserialize<List<string>>(outputJson)
                ?? throw new InvalidOperationException($"Invalid turn output interaction payload for session '{sessionId}'.");

            turns.Add(new RolePlayTurn
            {
                TurnId = reader.GetString(0),
                SessionId = reader.GetString(1),
                TurnIndex = reader.GetInt32(2),
                TurnKind = reader.GetString(3),
                TriggerSource = reader.GetString(4),
                InitiatedByActorName = reader.IsDBNull(5) ? null : reader.GetString(5),
                InputInteractionId = reader.IsDBNull(6) ? null : reader.GetString(6),
                OutputInteractionIds = outputInteractionIds,
                StartedUtc = DateTime.TryParse(reader.GetString(8), out var startedUtc)
                    ? startedUtc
                    : throw new InvalidOperationException($"Invalid turn start timestamp for session '{sessionId}'."),
                CompletedUtc = reader.IsDBNull(9)
                    ? null
                    : (DateTime.TryParse(reader.GetString(9), out var completedUtc)
                        ? completedUtc
                        : throw new InvalidOperationException($"Invalid turn completion timestamp for session '{sessionId}'.")),
                Status = status,
                FailureReason = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }

        turns.Reverse();
        return turns;
    }

    public async Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
    {
        // Ensure the CharacterSnapshots list is in sync with the runtime CharacterStats dictionary
        // before serialising to JSON.
        state.SyncCharacterSnapshots();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureAdaptiveStateSchemaAsync(connection, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO RolePlayV2AdaptiveStates (
                SessionId, ActiveScenarioId, CurrentPhase, InteractionCountInPhase, ConsecutiveLeadCount,
                LastEvaluationUtc, CycleIndex, ActiveFormulaVersion, ActiveVariantId,
                SelectedWillingnessProfileId, SelectedNarrativeGateProfileId, CharacterEncounterProfileIdsJson,
                PhaseOverrideFloor, PhaseOverrideScenarioId, PhaseOverrideCycleIndex, PhaseOverrideSource, PhaseOverrideAppliedUtc,
                CurrentSceneLocation,
                CharacterLocationsJson, CharacterLocationPerceptionsJson, CharacterSnapshotsJson, ThemeMachineSnapshotJson,
                CurrentBeatCode, TurnsInCurrentBeat,
                CompletedScenarios, InteractionsSinceCommitment, InteractionsInApproaching, ScenarioCommitmentTimeUtc,
                SemanticStepSucceeded, SemanticDeltaBreakdownsJson, SemanticStatDeltaBreakdownsJson,
                CurrentEncounterNumber, InteractionsInCurrentEncounter, TimeSkipPending,
                UpdatedUtc)
            VALUES (
                $sessionId, $activeScenarioId, $currentPhase, $interactionCountInPhase, $consecutiveLeadCount,
                $lastEvaluationUtc, $cycleIndex, $activeFormulaVersion, $activeVariantId,
                $selectedWillingnessProfileId, $selectedNarrativeGateProfileId, $characterEncounterProfileIdsJson,
                $phaseOverrideFloor, $phaseOverrideScenarioId, $phaseOverrideCycleIndex, $phaseOverrideSource, $phaseOverrideAppliedUtc,
                $currentSceneLocation,
                $characterLocationsJson, $characterLocationPerceptionsJson, $characterSnapshotsJson, $themeMachineSnapshotJson,
                $currentBeatCode, $turnsInCurrentBeat,
                $completedScenarios, $interactionsSinceCommitment, $interactionsInApproaching, $scenarioCommitmentTimeUtc,
                $semanticStepSucceeded, $semanticDeltaBreakdownsJson, $semanticStatDeltaBreakdownsJson,
                $currentEncounterNumber, $interactionsInCurrentEncounter, $timeSkipPending,
                $updatedUtc)
            ON CONFLICT(SessionId) DO UPDATE SET
                ActiveScenarioId = excluded.ActiveScenarioId,
                CurrentPhase = excluded.CurrentPhase,
                InteractionCountInPhase = excluded.InteractionCountInPhase,
                ConsecutiveLeadCount = excluded.ConsecutiveLeadCount,
                LastEvaluationUtc = excluded.LastEvaluationUtc,
                CycleIndex = excluded.CycleIndex,
                ActiveFormulaVersion = excluded.ActiveFormulaVersion,
                ActiveVariantId = excluded.ActiveVariantId,
                SelectedWillingnessProfileId = excluded.SelectedWillingnessProfileId,
                SelectedNarrativeGateProfileId = excluded.SelectedNarrativeGateProfileId,
                CharacterEncounterProfileIdsJson = excluded.CharacterEncounterProfileIdsJson,
                PhaseOverrideFloor = excluded.PhaseOverrideFloor,
                PhaseOverrideScenarioId = excluded.PhaseOverrideScenarioId,
                PhaseOverrideCycleIndex = excluded.PhaseOverrideCycleIndex,
                PhaseOverrideSource = excluded.PhaseOverrideSource,
                PhaseOverrideAppliedUtc = excluded.PhaseOverrideAppliedUtc,
                CurrentSceneLocation = excluded.CurrentSceneLocation,
                CharacterLocationsJson = excluded.CharacterLocationsJson,
                CharacterLocationPerceptionsJson = excluded.CharacterLocationPerceptionsJson,
                CharacterSnapshotsJson = excluded.CharacterSnapshotsJson,
                ThemeMachineSnapshotJson = excluded.ThemeMachineSnapshotJson,
                CurrentBeatCode = excluded.CurrentBeatCode,
                TurnsInCurrentBeat = excluded.TurnsInCurrentBeat,
                CompletedScenarios = excluded.CompletedScenarios,
                InteractionsSinceCommitment = excluded.InteractionsSinceCommitment,
                InteractionsInApproaching = excluded.InteractionsInApproaching,
                ScenarioCommitmentTimeUtc = excluded.ScenarioCommitmentTimeUtc,
                SemanticStepSucceeded = excluded.SemanticStepSucceeded,
                SemanticDeltaBreakdownsJson = excluded.SemanticDeltaBreakdownsJson,
                SemanticStatDeltaBreakdownsJson = excluded.SemanticStatDeltaBreakdownsJson,
                CurrentEncounterNumber = excluded.CurrentEncounterNumber,
                InteractionsInCurrentEncounter = excluded.InteractionsInCurrentEncounter,
                TimeSkipPending = excluded.TimeSkipPending,
                UpdatedUtc = excluded.UpdatedUtc;
            """;

        var nowUtc = DateTime.UtcNow;
        command.Parameters.AddWithValue("$sessionId", state.SessionId);
        command.Parameters.AddWithValue("$activeScenarioId", (object?)state.ActiveScenarioId ?? DBNull.Value);
        command.Parameters.AddWithValue("$currentPhase", state.CurrentPhase.ToString());
        command.Parameters.AddWithValue("$interactionCountInPhase", state.InteractionCountInPhase);
        command.Parameters.AddWithValue("$consecutiveLeadCount", state.ConsecutiveLeadCount);
        command.Parameters.AddWithValue("$lastEvaluationUtc", state.LastEvaluationUtc.ToString("O"));
        command.Parameters.AddWithValue("$cycleIndex", state.CycleIndex);
        command.Parameters.AddWithValue("$activeFormulaVersion", state.ActiveFormulaVersion);
        command.Parameters.AddWithValue("$activeVariantId", (object?)state.ActiveVariantId ?? DBNull.Value);
        command.Parameters.AddWithValue("$selectedWillingnessProfileId", (object?)state.SelectedWillingnessProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$selectedNarrativeGateProfileId", (object?)state.SelectedNarrativeGateProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$characterEncounterProfileIdsJson",
            state.CharacterEncounterProfileIds.Count == 0
                ? (object)DBNull.Value
                : JsonSerializer.Serialize(state.CharacterEncounterProfileIds));
        command.Parameters.AddWithValue("$phaseOverrideFloor", state.PhaseOverrideFloor?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideScenarioId", (object?)state.PhaseOverrideScenarioId ?? DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideCycleIndex", state.PhaseOverrideCycleIndex ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideSource", (object?)state.PhaseOverrideSource ?? DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideAppliedUtc", state.PhaseOverrideAppliedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$currentSceneLocation", (object?)state.CurrentSceneLocation ?? DBNull.Value);
        command.Parameters.AddWithValue("$characterLocationsJson", JsonSerializer.Serialize(state.CharacterLocations));
        command.Parameters.AddWithValue("$characterLocationPerceptionsJson", JsonSerializer.Serialize(state.CharacterLocationPerceptions));
        command.Parameters.AddWithValue("$characterSnapshotsJson", JsonSerializer.Serialize(state.CharacterSnapshots));
        command.Parameters.AddWithValue(
            "$themeMachineSnapshotJson",
            state.ThemeMachineSnapshot is null
                ? (object)DBNull.Value
                : JsonSerializer.Serialize(state.ThemeMachineSnapshot));
        command.Parameters.AddWithValue("$currentBeatCode", (object?)state.CurrentBeatCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$turnsInCurrentBeat", state.TurnsInCurrentBeat);
        command.Parameters.AddWithValue("$completedScenarios", state.CompletedScenarios);
        command.Parameters.AddWithValue("$interactionsSinceCommitment", state.InteractionsSinceCommitment);
        command.Parameters.AddWithValue("$interactionsInApproaching", state.InteractionsInApproaching);
        command.Parameters.AddWithValue("$scenarioCommitmentTimeUtc", state.ScenarioCommitmentTimeUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$semanticStepSucceeded", state.SemanticStepSucceeded ? 1 : 0);
        command.Parameters.AddWithValue("$semanticDeltaBreakdownsJson", JsonSerializer.Serialize(state.SemanticDeltaBreakdowns));
        command.Parameters.AddWithValue("$semanticStatDeltaBreakdownsJson", JsonSerializer.Serialize(state.SemanticStatDeltaBreakdowns));
        command.Parameters.AddWithValue("$currentEncounterNumber", state.CurrentEncounterNumber);
        command.Parameters.AddWithValue("$interactionsInCurrentEncounter", state.InteractionsInCurrentEncounter);
        command.Parameters.AddWithValue("$timeSkipPending", state.TimeSkipPending ? 1 : 0);
        command.Parameters.AddWithValue("$updatedUtc", nowUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await ReplaceThemeScoresAsync(connection, transaction, state, nowUtc, cancellationToken);
        await ReplaceThemeTrackerMetaAsync(connection, transaction, state, nowUtc, cancellationToken);
        await ReplaceScenarioHistoryAsync(connection, transaction, state, cancellationToken);
        await ReplacePairwiseStatsAsync(connection, transaction, state, nowUtc, cancellationToken);
        await ReplaceSemanticEventsAsync(connection, transaction, state, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ReplaceThemeScoresAsync(SqliteConnection connection, SqliteTransaction transaction, AdaptiveScenarioState state, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using (var del = connection.CreateCommand())
        {
            del.Transaction = transaction;
            del.CommandText = "DELETE FROM RolePlayV2ThemeScores WHERE SessionId = $sessionId";
            del.Parameters.AddWithValue("$sessionId", state.SessionId);
            await del.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var (themeId, score) in state.ThemeScores)
        {
            await using var ins = connection.CreateCommand();
            ins.Transaction = transaction;
            ins.CommandText = """
                INSERT INTO RolePlayV2ThemeScores (
                    SessionId, ThemeId, ThemeName, Intensity, Score, Blocked, SuppressedHitCount,
                    IsScenarioCandidate, NarrativeFitScore, LastCandidateEvaluationTimeUtc,
                    CompletionCooldownInteractions, BreakdownJson, UpdatedUtc)
                VALUES (
                    $sessionId, $themeId, $themeName, $intensity, $score, $blocked, $suppressedHitCount,
                    $isScenarioCandidate, $narrativeFitScore, $lastCandidateEvalUtc,
                    $completionCooldown, $breakdownJson, $updatedUtc);
                """;
            ins.Parameters.AddWithValue("$sessionId", state.SessionId);
            ins.Parameters.AddWithValue("$themeId", themeId);
            ins.Parameters.AddWithValue("$themeName", score.ThemeName);
            ins.Parameters.AddWithValue("$intensity", score.Intensity);
            ins.Parameters.AddWithValue("$score", score.Score);
            ins.Parameters.AddWithValue("$blocked", score.Blocked ? 1 : 0);
            ins.Parameters.AddWithValue("$suppressedHitCount", score.SuppressedHitCount);
            ins.Parameters.AddWithValue("$isScenarioCandidate", score.IsScenarioCandidate ? 1 : 0);
            ins.Parameters.AddWithValue("$narrativeFitScore", score.NarrativeFitScore);
            ins.Parameters.AddWithValue("$lastCandidateEvalUtc", score.LastCandidateEvaluationTimeUtc?.ToString("O") ?? (object)DBNull.Value);
            ins.Parameters.AddWithValue("$completionCooldown", score.CompletionCooldownInteractions);
            ins.Parameters.AddWithValue("$breakdownJson", JsonSerializer.Serialize(score.Breakdown));
            ins.Parameters.AddWithValue("$updatedUtc", (score.UpdatedUtc == default ? nowUtc : score.UpdatedUtc).ToString("O"));
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceThemeTrackerMetaAsync(SqliteConnection connection, SqliteTransaction transaction, AdaptiveScenarioState state, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO RolePlayV2ThemeTrackerMeta (
                SessionId, PrimaryThemeId, SecondaryThemeId, ThemeSelectionRule,
                ObservedTurnCount, SelectionMinimumTurns, RecentEvidenceJson, UpdatedUtc)
            VALUES (
                $sessionId, $primaryThemeId, $secondaryThemeId, $themeSelectionRule,
                $observedTurnCount, $selectionMinimumTurns, $recentEvidenceJson, $updatedUtc)
            ON CONFLICT(SessionId) DO UPDATE SET
                PrimaryThemeId = excluded.PrimaryThemeId,
                SecondaryThemeId = excluded.SecondaryThemeId,
                ThemeSelectionRule = excluded.ThemeSelectionRule,
                ObservedTurnCount = excluded.ObservedTurnCount,
                SelectionMinimumTurns = excluded.SelectionMinimumTurns,
                RecentEvidenceJson = excluded.RecentEvidenceJson,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        cmd.Parameters.AddWithValue("$sessionId", state.SessionId);
        cmd.Parameters.AddWithValue("$primaryThemeId", (object?)state.PrimaryThemeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$secondaryThemeId", (object?)state.SecondaryThemeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$themeSelectionRule", state.ThemeSelectionRule);
        cmd.Parameters.AddWithValue("$observedTurnCount", state.ObservedTurnCount);
        cmd.Parameters.AddWithValue("$selectionMinimumTurns", state.SelectionMinimumTurns);
        cmd.Parameters.AddWithValue("$recentEvidenceJson", JsonSerializer.Serialize(state.RecentEvidence));
        cmd.Parameters.AddWithValue("$updatedUtc", (state.ThemeTrackerUpdatedUtc == default ? nowUtc : state.ThemeTrackerUpdatedUtc).ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceScenarioHistoryAsync(SqliteConnection connection, SqliteTransaction transaction, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        await using (var del = connection.CreateCommand())
        {
            del.Transaction = transaction;
            del.CommandText = "DELETE FROM RolePlayV2ScenarioHistory WHERE SessionId = $sessionId";
            del.Parameters.AddWithValue("$sessionId", state.SessionId);
            await del.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var entry in state.ScenarioHistory)
        {
            await using var ins = connection.CreateCommand();
            ins.Transaction = transaction;
            ins.CommandText = """
                INSERT INTO RolePlayV2ScenarioHistory (
                    Id, SessionId, ScenarioId, CompletedAtUtc, InteractionCount,
                    PeakThemeScore, PeakDesireLevel, AverageRestraintLevel, Notes)
                VALUES (
                    $id, $sessionId, $scenarioId, $completedAtUtc, $interactionCount,
                    $peakThemeScore, $peakDesireLevel, $averageRestraintLevel, $notes);
                """;
            ins.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString() : entry.Id);
            ins.Parameters.AddWithValue("$sessionId", state.SessionId);
            ins.Parameters.AddWithValue("$scenarioId", entry.ScenarioId);
            ins.Parameters.AddWithValue("$completedAtUtc", entry.CompletedAtUtc.ToString("O"));
            ins.Parameters.AddWithValue("$interactionCount", entry.InteractionCount);
            ins.Parameters.AddWithValue("$peakThemeScore", entry.PeakThemeScore);
            ins.Parameters.AddWithValue("$peakDesireLevel", entry.PeakDesireLevel);
            ins.Parameters.AddWithValue("$averageRestraintLevel", entry.AverageRestraintLevel);
            ins.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplacePairwiseStatsAsync(SqliteConnection connection, SqliteTransaction transaction, AdaptiveScenarioState state, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using (var del = connection.CreateCommand())
        {
            del.Transaction = transaction;
            del.CommandText = "DELETE FROM RolePlayV2PairwiseStats WHERE SessionId = $sessionId";
            del.Parameters.AddWithValue("$sessionId", state.SessionId);
            await del.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var pair in state.PairwiseStats)
        {
            await using var ins = connection.CreateCommand();
            ins.Transaction = transaction;
            ins.CommandText = """
                INSERT INTO RolePlayV2PairwiseStats (
                    SessionId, SourceCharacterId, TargetCharacterId, StatsJson, UpdatedUtc)
                VALUES (
                    $sessionId, $sourceCharacterId, $targetCharacterId, $statsJson, $updatedUtc);
                """;
            ins.Parameters.AddWithValue("$sessionId", state.SessionId);
            ins.Parameters.AddWithValue("$sourceCharacterId", pair.SourceCharacterId);
            ins.Parameters.AddWithValue("$targetCharacterId", pair.TargetCharacterId);
            ins.Parameters.AddWithValue("$statsJson", JsonSerializer.Serialize(pair.Stats));
            ins.Parameters.AddWithValue("$updatedUtc", (pair.UpdatedUtc == default ? nowUtc : pair.UpdatedUtc).ToString("O"));
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceSemanticEventsAsync(SqliteConnection connection, SqliteTransaction transaction, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        await using (var del = connection.CreateCommand())
        {
            del.Transaction = transaction;
            del.CommandText = "DELETE FROM RolePlayV2SemanticEvents WHERE SessionId = $sessionId";
            del.Parameters.AddWithValue("$sessionId", state.SessionId);
            await del.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var ev in state.SemanticEvents)
        {
            await using var ins = connection.CreateCommand();
            ins.Transaction = transaction;
            ins.CommandText = """
                INSERT INTO RolePlayV2SemanticEvents (
                    SessionId, InteractionId, EventId, Confidence, MappingId, Direction,
                    ThemeTargetsJson, ProcessedUtc)
                VALUES (
                    $sessionId, $interactionId, $eventId, $confidence, $mappingId, $direction,
                    $themeTargetsJson, $processedUtc);
                """;
            ins.Parameters.AddWithValue("$sessionId", state.SessionId);
            ins.Parameters.AddWithValue("$interactionId", ev.InteractionId);
            ins.Parameters.AddWithValue("$eventId", ev.EventId);
            ins.Parameters.AddWithValue("$confidence", ev.Confidence);
            ins.Parameters.AddWithValue("$mappingId", ev.MappingId);
            ins.Parameters.AddWithValue("$direction", ev.Direction);
            ins.Parameters.AddWithValue("$themeTargetsJson", JsonSerializer.Serialize(ev.ThemeTargets));
            ins.Parameters.AddWithValue("$processedUtc", ev.ProcessedUtc.ToString("O"));
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureAdaptiveStateSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SessionId, ActiveScenarioId, CurrentPhase, InteractionCountInPhase, ConsecutiveLeadCount,
                 LastEvaluationUtc, CycleIndex, ActiveFormulaVersion, ActiveVariantId,
                                SelectedWillingnessProfileId, SelectedNarrativeGateProfileId, HusbandAwarenessProfileId,
                                PhaseOverrideFloor,
                                PhaseOverrideScenarioId, PhaseOverrideCycleIndex, PhaseOverrideSource, PhaseOverrideAppliedUtc,
                              CurrentSceneLocation, CharacterLocationsJson, CharacterLocationPerceptionsJson, CharacterSnapshotsJson,
                              ThemeMachineSnapshotJson, CurrentBeatCode, TurnsInCurrentBeat,
                              CompletedScenarios, InteractionsSinceCommitment, InteractionsInApproaching, ScenarioCommitmentTimeUtc,
                              SemanticStepSucceeded, SemanticDeltaBreakdownsJson, SemanticStatDeltaBreakdownsJson,
                              CharacterEncounterProfileIdsJson,
                              CurrentEncounterNumber, InteractionsInCurrentEncounter, TimeSkipPending
            FROM RolePlayV2AdaptiveStates
            WHERE SessionId = $sessionId;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        // Capture legacy field before building state (T014 backward compat)
        var legacyHusbandAwarenessProfileId = reader.IsDBNull(11) ? null : reader.GetString(11);

        var state = new AdaptiveScenarioState
        {
            SessionId = reader.GetString(0),
            ActiveScenarioId = reader.IsDBNull(1) ? null : reader.GetString(1),
            CurrentPhase = ParseNarrativePhase(reader.GetString(2), reader.GetString(0)),
            InteractionCountInPhase = reader.GetInt32(3),
            ConsecutiveLeadCount = reader.GetInt32(4),
            LastEvaluationUtc = ParseUtcTimestamp(reader.GetString(5), reader.GetString(0)),
            CycleIndex = reader.GetInt32(6),
            ActiveFormulaVersion = reader.GetString(7),
            ActiveVariantId = reader.IsDBNull(8) ? null : reader.GetString(8),
            SelectedWillingnessProfileId = reader.IsDBNull(9) ? null : reader.GetString(9),
            SelectedNarrativeGateProfileId = reader.IsDBNull(10) ? null : reader.GetString(10),
            // ordinal 11 = HusbandAwarenessProfileId (legacy column, kept in SELECT for backward compat — not mapped)
            PhaseOverrideFloor = reader.IsDBNull(12)
                ? null
                : (Enum.TryParse<NarrativePhase>(reader.GetString(12), out var overrideFloor) ? overrideFloor : null),
            PhaseOverrideScenarioId = reader.IsDBNull(13) ? null : reader.GetString(13),
            PhaseOverrideCycleIndex = reader.IsDBNull(14) ? null : reader.GetInt32(14),
            PhaseOverrideSource = reader.IsDBNull(15) ? null : reader.GetString(15),
            PhaseOverrideAppliedUtc = reader.IsDBNull(16)
                ? null
                : (DateTime.TryParse(reader.GetString(16), out var overrideAppliedUtc) ? overrideAppliedUtc : null),
            CurrentSceneLocation = reader.IsDBNull(17) ? null : reader.GetString(17),
            CharacterLocations = reader.IsDBNull(18)
                ? []
                : (JsonSerializer.Deserialize<List<CharacterLocationState>>(reader.GetString(18)) ?? []),
            CharacterLocationPerceptions = reader.IsDBNull(19)
                ? []
                : (JsonSerializer.Deserialize<List<CharacterLocationPerceptionState>>(reader.GetString(19)) ?? []),
            CharacterSnapshots = JsonSerializer.Deserialize<List<CharacterStatProfileV2>>(reader.GetString(20)) ?? [],
            ThemeMachineSnapshot = reader.IsDBNull(21)
                ? null
                : DeserializeThemeMachineSnapshot(reader.GetString(21), reader.GetString(0)),
            CurrentBeatCode = reader.IsDBNull(22) ? null : reader.GetString(22),
            TurnsInCurrentBeat = reader.IsDBNull(23) ? 0 : reader.GetInt32(23),
            CompletedScenarios = reader.GetInt32(24),
            InteractionsSinceCommitment = reader.GetInt32(25),
            InteractionsInApproaching = reader.GetInt32(26),
            ScenarioCommitmentTimeUtc = reader.IsDBNull(27)
                ? null
                : ParseUtcTimestamp(reader.GetString(27), reader.GetString(0)),
            SemanticStepSucceeded = reader.GetInt32(28) != 0,
            SemanticDeltaBreakdowns = reader.IsDBNull(29)
                ? []
                : (JsonSerializer.Deserialize<List<SemanticThemeDeltaBreakdown>>(reader.GetString(29)) ?? []),
            SemanticStatDeltaBreakdowns = reader.IsDBNull(30)
                ? []
                : (JsonSerializer.Deserialize<List<SemanticStatDeltaRecord>>(reader.GetString(30)) ?? []),
            CharacterEncounterProfileIds = reader.IsDBNull(31)
                ? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(
                    JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(31)) ?? [],
                    StringComparer.OrdinalIgnoreCase),
            CurrentEncounterNumber = reader.IsDBNull(32) ? 0 : reader.GetInt32(32),
            InteractionsInCurrentEncounter = reader.IsDBNull(33) ? 0 : reader.GetInt32(33),
            TimeSkipPending = reader.IsDBNull(34) ? false : reader.GetInt32(34) != 0
        };
        await reader.CloseAsync();

        await LoadThemeScoresAsync(connection, state, cancellationToken);
        await LoadThemeTrackerMetaAsync(connection, state, cancellationToken);
        await LoadScenarioHistoryAsync(connection, state, cancellationToken);
        await LoadPairwiseStatsAsync(connection, state, cancellationToken);
        await LoadSemanticEventsAsync(connection, state, cancellationToken);
        await LoadEncounterSummariesAsync(connection, state, cancellationToken);

        // Rebuild the runtime CharacterStats dictionary so callers can use dict-style access
        // without re-parsing CharacterSnapshots themselves.
        state.RebuildCharacterStatsCache();

        // T014 backward compat: synthesize CharacterEncounterProfileIds from legacy HusbandAwarenessProfileId
        // for sessions saved before B-042. Once the state is re-saved, this branch will not fire again.
        if (state.CharacterEncounterProfileIds.Count == 0 && legacyHusbandAwarenessProfileId is not null)
        {
            var husbandCharId = await TryFindHusbandCharacterIdAsync(connection, sessionId, cancellationToken);
            if (husbandCharId is not null)
            {
                state.CharacterEncounterProfileIds[husbandCharId] = legacyHusbandAwarenessProfileId;
            }
        }

        return state;
    }

    private static async Task LoadThemeScoresAsync(SqliteConnection connection, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ThemeId, ThemeName, Intensity, Score, Blocked, SuppressedHitCount,
                   IsScenarioCandidate, NarrativeFitScore, LastCandidateEvaluationTimeUtc,
                   CompletionCooldownInteractions, BreakdownJson, UpdatedUtc
            FROM RolePlayV2ThemeScores WHERE SessionId = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$sessionId", state.SessionId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
        {
            var themeId = rdr.GetString(0);
            var score = new ThemeScoreState
            {
                ThemeId = themeId,
                ThemeName = rdr.GetString(1),
                Intensity = rdr.GetString(2),
                Score = rdr.GetDouble(3),
                Blocked = rdr.GetInt32(4) != 0,
                SuppressedHitCount = rdr.GetInt32(5),
                IsScenarioCandidate = rdr.GetInt32(6) != 0,
                NarrativeFitScore = rdr.GetDouble(7),
                LastCandidateEvaluationTimeUtc = rdr.IsDBNull(8)
                    ? null
                    : (DateTime.TryParse(rdr.GetString(8), out var lastEval) ? lastEval : null),
                CompletionCooldownInteractions = rdr.GetInt32(9),
                Breakdown = JsonSerializer.Deserialize<ThemeScoreBreakdownV2>(rdr.GetString(10)) ?? new ThemeScoreBreakdownV2(),
                UpdatedUtc = ParseUtcTimestamp(rdr.GetString(11), state.SessionId)
            };
            state.ThemeScores[themeId] = score;
        }
    }

    private static async Task LoadThemeTrackerMetaAsync(SqliteConnection connection, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT PrimaryThemeId, SecondaryThemeId, ThemeSelectionRule, ObservedTurnCount,
                   SelectionMinimumTurns, RecentEvidenceJson, UpdatedUtc
            FROM RolePlayV2ThemeTrackerMeta WHERE SessionId = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$sessionId", state.SessionId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rdr.ReadAsync(cancellationToken))
        {
            return;
        }
        state.PrimaryThemeId = rdr.IsDBNull(0) ? null : rdr.GetString(0);
        state.SecondaryThemeId = rdr.IsDBNull(1) ? null : rdr.GetString(1);
        state.ThemeSelectionRule = rdr.GetString(2);
        state.ObservedTurnCount = rdr.GetInt32(3);
        state.SelectionMinimumTurns = rdr.GetInt32(4);
        state.RecentEvidence = JsonSerializer.Deserialize<List<ThemeEvidenceRecord>>(rdr.GetString(5)) ?? [];
        state.ThemeTrackerUpdatedUtc = ParseUtcTimestamp(rdr.GetString(6), state.SessionId);
    }

    private static async Task LoadScenarioHistoryAsync(SqliteConnection connection, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ScenarioId, CompletedAtUtc, InteractionCount, PeakThemeScore,
                   PeakDesireLevel, AverageRestraintLevel, Notes
            FROM RolePlayV2ScenarioHistory WHERE SessionId = $sessionId
            ORDER BY CompletedAtUtc ASC;
            """;
        cmd.Parameters.AddWithValue("$sessionId", state.SessionId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
        {
            state.ScenarioHistory.Add(new ScenarioHistoryEntry
            {
                Id = rdr.GetString(0),
                ScenarioId = rdr.GetString(1),
                CompletedAtUtc = ParseUtcTimestamp(rdr.GetString(2), state.SessionId),
                InteractionCount = rdr.GetInt32(3),
                PeakThemeScore = rdr.GetInt32(4),
                PeakDesireLevel = rdr.GetInt32(5),
                AverageRestraintLevel = rdr.GetDouble(6),
                Notes = rdr.IsDBNull(7) ? null : rdr.GetString(7)
            });
        }
    }

    private static async Task LoadPairwiseStatsAsync(SqliteConnection connection, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT SourceCharacterId, TargetCharacterId, StatsJson, UpdatedUtc
            FROM RolePlayV2PairwiseStats WHERE SessionId = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$sessionId", state.SessionId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
        {
            state.PairwiseStats.Add(new PairwiseStatRecord
            {
                SourceCharacterId = rdr.GetString(0),
                TargetCharacterId = rdr.GetString(1),
                Stats = JsonSerializer.Deserialize<Dictionary<string, int>>(rdr.GetString(2)) ?? new(StringComparer.OrdinalIgnoreCase),
                UpdatedUtc = ParseUtcTimestamp(rdr.GetString(3), state.SessionId)
            });
        }
    }

    private static async Task LoadSemanticEventsAsync(SqliteConnection connection, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT InteractionId, EventId, Confidence, MappingId, Direction, ThemeTargetsJson, ProcessedUtc
            FROM RolePlayV2SemanticEvents WHERE SessionId = $sessionId
            ORDER BY ProcessedUtc ASC;
            """;
        cmd.Parameters.AddWithValue("$sessionId", state.SessionId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
        {
            state.SemanticEvents.Add(new SemanticEventRecord
            {
                InteractionId = rdr.GetString(0),
                EventId = rdr.GetString(1),
                Confidence = (decimal)rdr.GetDouble(2),
                MappingId = rdr.GetString(3),
                Direction = rdr.GetString(4),
                ThemeTargets = JsonSerializer.Deserialize<List<string>>(rdr.GetString(5)) ?? [],
                ProcessedUtc = ParseUtcTimestamp(rdr.GetString(6), state.SessionId)
            });
        }
    }

    private static async Task LoadEncounterSummariesAsync(SqliteConnection connection, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CharacterId, SummaryType, CycleIndex, FromPhase, ToPhase,
                   OccurredUtc, InteractionCountInPhase, SceneLocation, ActiveThemeId,
                   FinishingMoveId, PositionIdsJson, CharacterStatsSnapshotJson,
                   TemplateSummary, LlmSummary, LlmEnhancedUtc
            FROM RolePlayV2EncounterSummaries
            WHERE SessionId = $sessionId
            ORDER BY OccurredUtc ASC;
            """;
        cmd.Parameters.AddWithValue("$sessionId", state.SessionId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
        {
            state.EncounterSummaries.Add(new EncounterSummaryRecord
            {
                Id           = rdr.GetString(0),
                SessionId    = state.SessionId,
                CharacterId  = rdr.GetString(1),
                SummaryType  = Enum.Parse<EncounterSummaryType>(rdr.GetString(2)),
                CycleIndex   = rdr.GetInt32(3),
                FromPhase    = Enum.Parse<NarrativePhase>(rdr.GetString(4)),
                ToPhase      = Enum.Parse<NarrativePhase>(rdr.GetString(5)),
                OccurredUtc  = ParseUtcTimestamp(rdr.GetString(6), state.SessionId),
                InteractionCountInPhase = rdr.GetInt32(7),
                SceneLocation           = rdr.IsDBNull(8) ? null : rdr.GetString(8),
                ActiveThemeId           = rdr.IsDBNull(9) ? null : rdr.GetString(9),
                FinishingMoveId         = rdr.IsDBNull(10) ? null : rdr.GetString(10),
                PositionIdsJson         = rdr.IsDBNull(11) ? "[]" : rdr.GetString(11),
                CharacterStatsSnapshotJson = rdr.IsDBNull(12) ? "{}" : rdr.GetString(12),
                TemplateSummary         = rdr.IsDBNull(13) ? string.Empty : rdr.GetString(13),
                LlmSummary              = rdr.IsDBNull(14) ? null : rdr.GetString(14),
                LlmEnhancedUtc          = rdr.IsDBNull(15) ? null : ParseUtcTimestamp(rdr.GetString(15), state.SessionId)
            });
        }
    }

    private static ThemeMachineSessionSnapshot DeserializeThemeMachineSnapshot(string payloadJson, string sessionId)
    {
        ThemeMachineSessionSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ThemeMachineSessionSnapshot>(payloadJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"RolePlayV2AdaptiveStates row for session '{sessionId}' has invalid ThemeMachineSnapshotJson payload.",
                ex);
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"RolePlayV2AdaptiveStates row for session '{sessionId}' has null ThemeMachineSnapshotJson payload.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.MachineKey))
        {
            throw new InvalidOperationException($"RolePlayV2AdaptiveStates row for session '{sessionId}' has ThemeMachineSnapshotJson with missing MachineKey.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.ThemeId))
        {
            throw new InvalidOperationException($"RolePlayV2AdaptiveStates row for session '{sessionId}' has ThemeMachineSnapshotJson with missing ThemeId.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.DefinitionId))
        {
            throw new InvalidOperationException($"RolePlayV2AdaptiveStates row for session '{sessionId}' has ThemeMachineSnapshotJson with missing DefinitionId.");
        }

        if (snapshot.DefinitionVersion <= 0)
        {
            throw new InvalidOperationException($"RolePlayV2AdaptiveStates row for session '{sessionId}' has ThemeMachineSnapshotJson with non-positive DefinitionVersion.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.CurrentStateCode))
        {
            throw new InvalidOperationException($"RolePlayV2AdaptiveStates row for session '{sessionId}' has ThemeMachineSnapshotJson with missing CurrentStateCode.");
        }

        if (snapshot.TurnsInCurrentState < 0)
        {
            throw new InvalidOperationException($"RolePlayV2AdaptiveStates row for session '{sessionId}' has ThemeMachineSnapshotJson with negative TurnsInCurrentState.");
        }

        if (snapshot.LastEvaluatedUtc == default)
        {
            throw new InvalidOperationException($"RolePlayV2AdaptiveStates row for session '{sessionId}' has ThemeMachineSnapshotJson with missing LastEvaluatedUtc.");
        }

        return snapshot;
    }

    private static NarrativePhase ParseNarrativePhase(string value, string sessionId)
    {
        if (Enum.TryParse<NarrativePhase>(value, out var phase))
            return phase;
        throw new InvalidOperationException(
            $"RolePlayV2AdaptiveStates row for session '{sessionId}' has unrecognized CurrentPhase value '{value}'. " +
            "Database state is corrupt; failing fast instead of silently defaulting.");
    }

    private static DateTime ParseUtcTimestamp(string value, string sessionId)
    {
        if (DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        throw new InvalidOperationException(
            $"RolePlayV2AdaptiveStates row for session '{sessionId}' has unparseable LastEvaluationUtc value '{value}'. " +
            "Database state is corrupt; failing fast instead of silently defaulting.");
    }

    private static async Task EnsureAdaptiveStateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "SelectedNarrativeGateProfileId", cancellationToken))
        {
            await using var addNarrativeGateProfile = connection.CreateCommand();
            addNarrativeGateProfile.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN SelectedNarrativeGateProfileId TEXT NULL";
            await addNarrativeGateProfile.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "CurrentSceneLocation", cancellationToken))
        {
            await using var addCurrentSceneLocation = connection.CreateCommand();
            addCurrentSceneLocation.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CurrentSceneLocation TEXT NULL";
            await addCurrentSceneLocation.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "PhaseOverrideFloor", cancellationToken))
        {
            await using var addPhaseOverrideFloor = connection.CreateCommand();
            addPhaseOverrideFloor.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideFloor TEXT NULL";
            await addPhaseOverrideFloor.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "PhaseOverrideScenarioId", cancellationToken))
        {
            await using var addPhaseOverrideScenarioId = connection.CreateCommand();
            addPhaseOverrideScenarioId.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideScenarioId TEXT NULL";
            await addPhaseOverrideScenarioId.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "PhaseOverrideCycleIndex", cancellationToken))
        {
            await using var addPhaseOverrideCycleIndex = connection.CreateCommand();
            addPhaseOverrideCycleIndex.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideCycleIndex INTEGER NULL";
            await addPhaseOverrideCycleIndex.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "PhaseOverrideSource", cancellationToken))
        {
            await using var addPhaseOverrideSource = connection.CreateCommand();
            addPhaseOverrideSource.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideSource TEXT NULL";
            await addPhaseOverrideSource.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "PhaseOverrideAppliedUtc", cancellationToken))
        {
            await using var addPhaseOverrideAppliedUtc = connection.CreateCommand();
            addPhaseOverrideAppliedUtc.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideAppliedUtc TEXT NULL";
            await addPhaseOverrideAppliedUtc.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "CharacterLocationsJson", cancellationToken))
        {
            await using var addCharacterLocations = connection.CreateCommand();
            addCharacterLocations.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CharacterLocationsJson TEXT NOT NULL DEFAULT '[]'";
            await addCharacterLocations.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "CharacterLocationPerceptionsJson", cancellationToken))
        {
            await using var addCharacterLocationPerceptions = connection.CreateCommand();
            addCharacterLocationPerceptions.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CharacterLocationPerceptionsJson TEXT NOT NULL DEFAULT '[]'";
            await addCharacterLocationPerceptions.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "ThemeMachineSnapshotJson", cancellationToken))
        {
            await using var addThemeMachineSnapshot = connection.CreateCommand();
            addThemeMachineSnapshot.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN ThemeMachineSnapshotJson TEXT NULL";
            await addThemeMachineSnapshot.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "CurrentBeatCode", cancellationToken))
        {
            await using var addCurrentBeatCode = connection.CreateCommand();
            addCurrentBeatCode.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CurrentBeatCode TEXT NULL";
            await addCurrentBeatCode.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "TurnsInCurrentBeat", cancellationToken))
        {
            await using var addTurnsInCurrentBeat = connection.CreateCommand();
            addTurnsInCurrentBeat.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN TurnsInCurrentBeat INTEGER NOT NULL DEFAULT 0";
            await addTurnsInCurrentBeat.ExecuteNonQueryAsync(cancellationToken);
        }

        // Phase 1 (B-038) additive columns for V1→V2 unification.
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "CompletedScenarios", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CompletedScenarios INTEGER NOT NULL DEFAULT 0";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "InteractionsSinceCommitment", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN InteractionsSinceCommitment INTEGER NOT NULL DEFAULT 0";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "InteractionsInApproaching", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN InteractionsInApproaching INTEGER NOT NULL DEFAULT 0";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "ScenarioCommitmentTimeUtc", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN ScenarioCommitmentTimeUtc TEXT NULL";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "SemanticStepSucceeded", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN SemanticStepSucceeded INTEGER NOT NULL DEFAULT 1";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "SemanticDeltaBreakdownsJson", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN SemanticDeltaBreakdownsJson TEXT NOT NULL DEFAULT '[]'";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "SemanticStatDeltaBreakdownsJson", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN SemanticStatDeltaBreakdownsJson TEXT NOT NULL DEFAULT '[]'";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }

        // B-042: per-character encounter behavioral profile bindings — replaces single HusbandAwarenessProfileId
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "CharacterEncounterProfileIdsJson", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CharacterEncounterProfileIdsJson TEXT NULL";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }

        // Multi-encounter Climax state (theme-scoped via [ClimaxMode:multi-encounter] marker).
        // Dormant (0) for themes/phases that don't use multi-encounter mode.
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "CurrentEncounterNumber", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CurrentEncounterNumber INTEGER NOT NULL DEFAULT 0";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "InteractionsInCurrentEncounter", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN InteractionsInCurrentEncounter INTEGER NOT NULL DEFAULT 0";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "TimeSkipPending", cancellationToken))
        {
            await using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN TimeSkipPending INTEGER NOT NULL DEFAULT 0";
            await add.ExecuteNonQueryAsync(cancellationToken);
        }

        // Phase 1 (B-038) additive child tables for V1→V2 unification.
        await using (var createThemeScores = connection.CreateCommand())
        {
            createThemeScores.CommandText = """
                CREATE TABLE IF NOT EXISTS RolePlayV2ThemeScores (
                    SessionId TEXT NOT NULL,
                    ThemeId TEXT NOT NULL,
                    ThemeName TEXT NOT NULL,
                    Intensity TEXT NOT NULL DEFAULT 'None',
                    Score REAL NOT NULL DEFAULT 0,
                    Blocked INTEGER NOT NULL DEFAULT 0,
                    SuppressedHitCount INTEGER NOT NULL DEFAULT 0,
                    IsScenarioCandidate INTEGER NOT NULL DEFAULT 0,
                    NarrativeFitScore REAL NOT NULL DEFAULT 0,
                    LastCandidateEvaluationTimeUtc TEXT NULL,
                    CompletionCooldownInteractions INTEGER NOT NULL DEFAULT 0,
                    BreakdownJson TEXT NOT NULL DEFAULT '{}',
                    UpdatedUtc TEXT NOT NULL,
                    PRIMARY KEY (SessionId, ThemeId)
                );
                """;
            await createThemeScores.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var createThemeTrackerMeta = connection.CreateCommand())
        {
            createThemeTrackerMeta.CommandText = """
                CREATE TABLE IF NOT EXISTS RolePlayV2ThemeTrackerMeta (
                    SessionId TEXT PRIMARY KEY,
                    PrimaryThemeId TEXT NULL,
                    SecondaryThemeId TEXT NULL,
                    ThemeSelectionRule TEXT NOT NULL DEFAULT 'Top1',
                    ObservedTurnCount INTEGER NOT NULL DEFAULT 0,
                    SelectionMinimumTurns INTEGER NOT NULL DEFAULT 0,
                    RecentEvidenceJson TEXT NOT NULL DEFAULT '[]',
                    UpdatedUtc TEXT NOT NULL
                );
                """;
            await createThemeTrackerMeta.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var createScenarioHistory = connection.CreateCommand())
        {
            createScenarioHistory.CommandText = """
                CREATE TABLE IF NOT EXISTS RolePlayV2ScenarioHistory (
                    Id TEXT PRIMARY KEY,
                    SessionId TEXT NOT NULL,
                    ScenarioId TEXT NOT NULL,
                    CompletedAtUtc TEXT NOT NULL,
                    InteractionCount INTEGER NOT NULL DEFAULT 0,
                    PeakThemeScore INTEGER NOT NULL DEFAULT 0,
                    PeakDesireLevel INTEGER NOT NULL DEFAULT 0,
                    AverageRestraintLevel REAL NOT NULL DEFAULT 0,
                    Notes TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_RolePlayV2ScenarioHistory_Session ON RolePlayV2ScenarioHistory(SessionId);
                """;
            await createScenarioHistory.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var createPairwiseStats = connection.CreateCommand())
        {
            createPairwiseStats.CommandText = """
                CREATE TABLE IF NOT EXISTS RolePlayV2PairwiseStats (
                    SessionId TEXT NOT NULL,
                    SourceCharacterId TEXT NOT NULL,
                    TargetCharacterId TEXT NOT NULL,
                    StatsJson TEXT NOT NULL DEFAULT '{}',
                    UpdatedUtc TEXT NOT NULL,
                    PRIMARY KEY (SessionId, SourceCharacterId, TargetCharacterId)
                );
                """;
            await createPairwiseStats.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var createSemanticEvents = connection.CreateCommand())
        {
            createSemanticEvents.CommandText = """
                CREATE TABLE IF NOT EXISTS RolePlayV2SemanticEvents (
                    SessionId TEXT NOT NULL,
                    InteractionId TEXT NOT NULL,
                    EventId TEXT NOT NULL,
                    Confidence TEXT NOT NULL,
                    MappingId TEXT NOT NULL,
                    Direction TEXT NOT NULL,
                    ThemeTargetsJson TEXT NOT NULL DEFAULT '[]',
                    ProcessedUtc TEXT NOT NULL,
                    PRIMARY KEY (SessionId, InteractionId, EventId)
                );
                CREATE INDEX IF NOT EXISTS IX_RolePlayV2SemanticEvents_Session ON RolePlayV2SemanticEvents(SessionId);
                """;
            await createSemanticEvents.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureTurnSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RolePlayV2Turns (
                TurnId TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                TurnIndex INTEGER NOT NULL,
                TurnKind TEXT NOT NULL,
                TriggerSource TEXT NOT NULL,
                InitiatedByActorName TEXT NULL,
                InputInteractionId TEXT NULL,
                OutputInteractionIdsJson TEXT NOT NULL DEFAULT '[]',
                OutputInteractionCount INTEGER NOT NULL DEFAULT 0,
                StartedUtc TEXT NOT NULL,
                CompletedUtc TEXT NULL,
                Status TEXT NOT NULL,
                FailureReason TEXT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE (SessionId, TurnIndex)
            );

            CREATE INDEX IF NOT EXISTS IX_RolePlayV2Turns_Session_TurnIndex
                ON RolePlayV2Turns (SessionId, TurnIndex DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name=$columnName";
        command.Parameters.AddWithValue("$columnName", columnName);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    public async Task SaveCandidateEvaluationsAsync(IReadOnlyList<ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default)
    {
        if (evaluations.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        foreach (var eval in evaluations)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO RolePlayV2CandidateEvaluations (
                    SessionId, EvaluationId, ScenarioId, StageAWillingnessTier, StageBEligible,
                    CharacterAlignmentScore, NarrativeEvidenceScore, PreferencePriorityScore,
                    FitScore, UnpenalizedFitScore, Confidence, TieBreakKey, Rationale, DetailsJson,
                    SuccessorCausalityBoost, EvaluatedUtc)
                VALUES (
                    $sessionId, $evaluationId, $scenarioId, $tier, $eligible,
                    $characterAlignmentScore, $narrativeEvidenceScore, $preferencePriorityScore,
                    $fitScore, $unpenalizedFitScore, $confidence, $tieBreakKey, $rationale, $detailsJson,
                    $successorCausalityBoost, $evaluatedUtc);
                """;
            command.Parameters.AddWithValue("$sessionId", eval.SessionId);
            command.Parameters.AddWithValue("$evaluationId", eval.EvaluationId);
            command.Parameters.AddWithValue("$scenarioId", eval.ScenarioId);
            command.Parameters.AddWithValue("$tier", eval.StageAWillingnessTier);
            command.Parameters.AddWithValue("$eligible", eval.StageBEligible ? 1 : 0);
            command.Parameters.AddWithValue("$characterAlignmentScore", eval.CharacterAlignmentScore);
            command.Parameters.AddWithValue("$narrativeEvidenceScore", eval.NarrativeEvidenceScore);
            command.Parameters.AddWithValue("$preferencePriorityScore", eval.PreferencePriorityScore);
            command.Parameters.AddWithValue("$fitScore", eval.FitScore);
            command.Parameters.AddWithValue("$unpenalizedFitScore", eval.UnpenalizedFitScore);
            command.Parameters.AddWithValue("$confidence", eval.Confidence);
            command.Parameters.AddWithValue("$tieBreakKey", eval.TieBreakKey);
            command.Parameters.AddWithValue("$rationale", eval.Rationale);
            command.Parameters.AddWithValue("$detailsJson", eval.DetailsJson);
            command.Parameters.AddWithValue("$successorCausalityBoost", eval.SuccessorCausalityBoost);
            command.Parameters.AddWithValue("$evaluatedUtc", eval.EvaluatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
        => LoadCandidateEvaluationsCoreAsync(sessionId, take, cancellationToken);

    public async Task SaveTransitionEventAsync(NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RolePlayV2PhaseTransitions (
                TransitionId, SessionId, FromPhase, ToPhase, TriggerType, EvidencePayload, ReasonCode, OccurredUtc)
            VALUES (
                $transitionId, $sessionId, $fromPhase, $toPhase, $triggerType, $evidencePayload, $reasonCode, $occurredUtc);
            """;
        command.Parameters.AddWithValue("$transitionId", transitionEvent.TransitionId);
        command.Parameters.AddWithValue("$sessionId", transitionEvent.SessionId);
        command.Parameters.AddWithValue("$fromPhase", transitionEvent.FromPhase.ToString());
        command.Parameters.AddWithValue("$toPhase", transitionEvent.ToPhase.ToString());
        command.Parameters.AddWithValue("$triggerType", transitionEvent.TriggerType.ToString());
        command.Parameters.AddWithValue("$evidencePayload", transitionEvent.EvidencePayload);
        command.Parameters.AddWithValue("$reasonCode", transitionEvent.ReasonCode);
        command.Parameters.AddWithValue("$occurredUtc", transitionEvent.OccurredUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
    {
        var events = new List<NarrativePhaseTransitionEvent>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TransitionId, SessionId, FromPhase, ToPhase, TriggerType, EvidencePayload, ReasonCode, OccurredUtc
            FROM RolePlayV2PhaseTransitions
            WHERE SessionId = $sessionId
            ORDER BY OccurredUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 500));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new NarrativePhaseTransitionEvent
            {
                TransitionId = reader.GetString(0),
                SessionId = reader.GetString(1),
                FromPhase = Enum.TryParse<NarrativePhase>(reader.GetString(2), out var fromPhase) ? fromPhase : NarrativePhase.BuildUp,
                ToPhase = Enum.TryParse<NarrativePhase>(reader.GetString(3), out var toPhase) ? toPhase : NarrativePhase.BuildUp,
                TriggerType = Enum.TryParse<TransitionTriggerType>(reader.GetString(4), out var triggerType) ? triggerType : TransitionTriggerType.Threshold,
                EvidencePayload = reader.GetString(5),
                ReasonCode = reader.GetString(6),
                OccurredUtc = DateTime.TryParse(reader.GetString(7), out var occurredUtc) ? occurredUtc : DateTime.UtcNow
            });
        }

        return events;
    }

    public async Task SaveCompletionMetadataAsync(ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RolePlayV2CompletionMetadata (
                SessionId, CycleIndex, ScenarioId, PeakPhase, ResetReason, StartedUtc, CompletedUtc)
            VALUES (
                $sessionId, $cycleIndex, $scenarioId, $peakPhase, $resetReason, $startedUtc, $completedUtc);
            """;
        command.Parameters.AddWithValue("$sessionId", metadata.SessionId);
        command.Parameters.AddWithValue("$cycleIndex", metadata.CycleIndex);
        command.Parameters.AddWithValue("$scenarioId", metadata.ScenarioId);
        command.Parameters.AddWithValue("$peakPhase", metadata.PeakPhase.ToString());
        command.Parameters.AddWithValue("$resetReason", metadata.ResetReason);
        command.Parameters.AddWithValue("$startedUtc", metadata.StartedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", metadata.CompletedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveDecisionPointAsync(DecisionPoint decisionPoint, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var pointCmd = connection.CreateCommand())
        {
            pointCmd.Transaction = tx;
            pointCmd.CommandText = """
                INSERT INTO RolePlayV2DecisionPoints (
                    DecisionPointId, SessionId, ScenarioId, Phase, TriggerSource, ContextSummary, AskingActorName, TargetActorId, TransparencyMode, OptionIdsJson, CreatedUtc)
                VALUES (
                    $decisionPointId, $sessionId, $scenarioId, $phase, $triggerSource, $contextSummary, $askingActorName, $targetActorId, $transparencyMode, $optionIdsJson, $createdUtc);
                """;
            pointCmd.Parameters.AddWithValue("$decisionPointId", decisionPoint.DecisionPointId);
            pointCmd.Parameters.AddWithValue("$sessionId", decisionPoint.SessionId);
            pointCmd.Parameters.AddWithValue("$scenarioId", decisionPoint.ScenarioId);
            pointCmd.Parameters.AddWithValue("$phase", decisionPoint.Phase.ToString());
            pointCmd.Parameters.AddWithValue("$triggerSource", decisionPoint.TriggerSource);
            pointCmd.Parameters.AddWithValue("$contextSummary", decisionPoint.ContextSummary);
            pointCmd.Parameters.AddWithValue("$askingActorName", decisionPoint.AskingActorName);
            pointCmd.Parameters.AddWithValue("$targetActorId", decisionPoint.TargetActorId);
            pointCmd.Parameters.AddWithValue("$transparencyMode", decisionPoint.TransparencyMode.ToString());
            pointCmd.Parameters.AddWithValue("$optionIdsJson", JsonSerializer.Serialize(decisionPoint.OptionIds));
            pointCmd.Parameters.AddWithValue("$createdUtc", decisionPoint.CreatedUtc.ToString("O"));
            await pointCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var option in options)
        {
            await using var optionCmd = connection.CreateCommand();
            optionCmd.Transaction = tx;
            var persistedOptionId = $"{decisionPoint.DecisionPointId}:{option.OptionId}";
            optionCmd.CommandText = """
                INSERT INTO RolePlayV2DecisionOptions (
                    OptionId, DecisionPointId, DisplayText, ResponsePreview, BehaviorStyleHint, CharacterDirectionInstruction, ChatInstruction, VisibilityMode, Prerequisites, StatDeltaMap, IsCustomResponseFallback)
                VALUES (
                    $optionId, $decisionPointId, $displayText, $responsePreview, $behaviorStyleHint, $characterDirectionInstruction, $chatInstruction, $visibilityMode, $prerequisites, $statDeltaMap, $isCustomResponseFallback);
                """;
            optionCmd.Parameters.AddWithValue("$optionId", persistedOptionId);
            optionCmd.Parameters.AddWithValue("$decisionPointId", option.DecisionPointId);
            optionCmd.Parameters.AddWithValue("$displayText", option.DisplayText);
            optionCmd.Parameters.AddWithValue("$responsePreview", option.ResponsePreview);
            optionCmd.Parameters.AddWithValue("$behaviorStyleHint", option.BehaviorStyleHint);
            optionCmd.Parameters.AddWithValue("$characterDirectionInstruction", option.CharacterDirectionInstruction);
            optionCmd.Parameters.AddWithValue("$chatInstruction", option.ChatInstruction);
            optionCmd.Parameters.AddWithValue("$visibilityMode", option.VisibilityMode.ToString());
            optionCmd.Parameters.AddWithValue("$prerequisites", option.Prerequisites);
            optionCmd.Parameters.AddWithValue("$statDeltaMap", option.StatDeltaMap);
            optionCmd.Parameters.AddWithValue("$isCustomResponseFallback", option.IsCustomResponseFallback ? 1 : 0);
            await optionCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
    {
        var points = new List<DecisionPoint>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DecisionPointId, SessionId, ScenarioId, Phase, TriggerSource, ContextSummary, AskingActorName, TargetActorId, TransparencyMode, OptionIdsJson, CreatedUtc
            FROM RolePlayV2DecisionPoints
            WHERE SessionId = $sessionId
            ORDER BY CreatedUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 500));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            points.Add(new DecisionPoint
            {
                DecisionPointId = reader.GetString(0),
                SessionId = reader.GetString(1),
                ScenarioId = reader.GetString(2),
                Phase = Enum.TryParse<NarrativePhase>(reader.GetString(3), out var phase) ? phase : NarrativePhase.BuildUp,
                TriggerSource = reader.GetString(4),
                ContextSummary = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                AskingActorName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                TargetActorId = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                TransparencyMode = Enum.TryParse<TransparencyMode>(reader.GetString(8), out var mode) ? mode : TransparencyMode.Directional,
                OptionIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(9)) ?? [],
                CreatedUtc = DateTime.TryParse(reader.GetString(10), out var createdUtc) ? createdUtc : DateTime.UtcNow
            });
        }

        points.Reverse();
        return points;
    }

    public async Task<IReadOnlyList<DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default)
    {
        var options = new List<DecisionOption>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OptionId, DecisionPointId, DisplayText, ResponsePreview, BehaviorStyleHint, CharacterDirectionInstruction, ChatInstruction, VisibilityMode, Prerequisites, StatDeltaMap, IsCustomResponseFallback
            FROM RolePlayV2DecisionOptions
            WHERE DecisionPointId = $decisionPointId
            ORDER BY rowid ASC;
            """;
        command.Parameters.AddWithValue("$decisionPointId", decisionPointId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var persistedOptionId = reader.GetString(0);
            var logicalOptionId = persistedOptionId;
            var separatorIndex = persistedOptionId.IndexOf(':');
            if (separatorIndex > 0
                && separatorIndex < persistedOptionId.Length - 1
                && persistedOptionId.StartsWith(decisionPointId + ":", StringComparison.OrdinalIgnoreCase))
            {
                logicalOptionId = persistedOptionId[(separatorIndex + 1)..];
            }

            options.Add(new DecisionOption
            {
                OptionId = logicalOptionId,
                DecisionPointId = reader.GetString(1),
                DisplayText = reader.GetString(2),
                ResponsePreview = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                BehaviorStyleHint = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                CharacterDirectionInstruction = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                ChatInstruction = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                VisibilityMode = Enum.TryParse<TransparencyMode>(reader.GetString(7), out var mode) ? mode : TransparencyMode.Directional,
                Prerequisites = reader.GetString(8),
                StatDeltaMap = reader.GetString(9),
                IsCustomResponseFallback = reader.GetInt32(10) == 1
            });
        }

        return options;
    }

    public async Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RolePlayV2ConceptInjections (SessionId, PayloadJson, CreatedUtc)
            VALUES ($sessionId, $payloadJson, $createdUtc);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(result));
        command.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveFormulaVersionReferenceAsync(string sessionId, FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RolePlayV2FormulaVersionRefs (
                SessionId, CycleIndex, FormulaVersionId, Name, ParameterPayload, EffectiveFromUtc, IsDefault, CreatedUtc)
            VALUES (
                $sessionId, $cycleIndex, $formulaVersionId, $name, $parameterPayload, $effectiveFromUtc, $isDefault, $createdUtc);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$cycleIndex", cycleIndex);
        command.Parameters.AddWithValue("$formulaVersionId", version.FormulaVersionId);
        command.Parameters.AddWithValue("$name", version.Name);
        command.Parameters.AddWithValue("$parameterPayload", version.ParameterPayload);
        command.Parameters.AddWithValue("$effectiveFromUtc", version.EffectiveFromUtc.ToString("O"));
        command.Parameters.AddWithValue("$isDefault", version.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveUnsupportedSessionErrorAsync(UnsupportedSessionError error, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RolePlayV2UnsupportedSessionErrors (
                ErrorCode, SessionId, DetectedSchemaVersion, MissingCanonicalStatsJson, RecoveryGuidance, EmittedUtc)
            VALUES (
                $errorCode, $sessionId, $detectedSchemaVersion, $missingCanonicalStatsJson, $recoveryGuidance, $emittedUtc);
            """;
        command.Parameters.AddWithValue("$errorCode", error.ErrorCode);
        command.Parameters.AddWithValue("$sessionId", error.SessionId);
        command.Parameters.AddWithValue("$detectedSchemaVersion", (object?)error.DetectedSchemaVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$missingCanonicalStatsJson", JsonSerializer.Serialize(error.MissingCanonicalStats));
        command.Parameters.AddWithValue("$recoveryGuidance", error.RecoveryGuidance);
        command.Parameters.AddWithValue("$emittedUtc", error.EmittedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default)
    {
        var errors = new List<UnsupportedSessionError>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ErrorCode, SessionId, DetectedSchemaVersion, MissingCanonicalStatsJson, RecoveryGuidance, EmittedUtc
            FROM RolePlayV2UnsupportedSessionErrors
            WHERE SessionId = $sessionId
            ORDER BY EmittedUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 200));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            errors.Add(new UnsupportedSessionError
            {
                ErrorCode = reader.GetString(0),
                SessionId = reader.GetString(1),
                DetectedSchemaVersion = reader.IsDBNull(2) ? null : reader.GetString(2),
                MissingCanonicalStats = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
                RecoveryGuidance = reader.GetString(4),
                EmittedUtc = DateTime.TryParse(reader.GetString(5), out var emittedUtc) ? emittedUtc : DateTime.UtcNow
            });
        }

        errors.Reverse();
        return errors;
    }

    public async Task SaveThemeMachineDiagnosticEventsAsync(IReadOnlyList<ThemeMachineDiagnosticEvent> events, CancellationToken cancellationToken = default)
    {
        if (events is null || events.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineDiagnosticsSchemaAsync(connection, cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var machineEvent in events)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)tx;
            command.CommandText = """
                INSERT INTO RolePlayV2ThemeMachineDiagnostics (
                    EventId, SessionId, ThemeId, MachineKey, DefinitionVersion, EventType,
                    FromStateCode, ToStateCode, TransitionId, ReasonCode, PayloadJson, OccurredUtc)
                VALUES (
                    $eventId, $sessionId, $themeId, $machineKey, $definitionVersion, $eventType,
                    $fromStateCode, $toStateCode, $transitionId, $reasonCode, $payloadJson, $occurredUtc)
                ON CONFLICT(EventId) DO UPDATE SET
                    SessionId = excluded.SessionId,
                    ThemeId = excluded.ThemeId,
                    MachineKey = excluded.MachineKey,
                    DefinitionVersion = excluded.DefinitionVersion,
                    EventType = excluded.EventType,
                    FromStateCode = excluded.FromStateCode,
                    ToStateCode = excluded.ToStateCode,
                    TransitionId = excluded.TransitionId,
                    ReasonCode = excluded.ReasonCode,
                    PayloadJson = excluded.PayloadJson,
                    OccurredUtc = excluded.OccurredUtc;
                """;
            command.Parameters.AddWithValue("$eventId", machineEvent.EventId);
            command.Parameters.AddWithValue("$sessionId", machineEvent.SessionId);
            command.Parameters.AddWithValue("$themeId", machineEvent.ThemeId);
            command.Parameters.AddWithValue("$machineKey", machineEvent.MachineKey);
            command.Parameters.AddWithValue("$definitionVersion", machineEvent.DefinitionVersion);
            command.Parameters.AddWithValue("$eventType", machineEvent.EventType);
            command.Parameters.AddWithValue("$fromStateCode", (object?)machineEvent.FromStateCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$toStateCode", (object?)machineEvent.ToStateCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$transitionId", (object?)machineEvent.TransitionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$reasonCode", machineEvent.ReasonCode);
            command.Parameters.AddWithValue("$payloadJson", machineEvent.PayloadJson);
            command.Parameters.AddWithValue("$occurredUtc", machineEvent.OccurredUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to load theme machine diagnostics.");
        }

        var events = new List<ThemeMachineDiagnosticEvent>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineDiagnosticsSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EventId, SessionId, ThemeId, MachineKey, DefinitionVersion, EventType,
                   FromStateCode, ToStateCode, TransitionId, ReasonCode, PayloadJson, OccurredUtc
            FROM RolePlayV2ThemeMachineDiagnostics
            WHERE SessionId = $sessionId
            ORDER BY OccurredUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 500));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var occurredUtcRaw = reader.GetString(11);
            if (!DateTime.TryParse(occurredUtcRaw, null, DateTimeStyles.RoundtripKind, out var occurredUtc))
            {
                throw new InvalidOperationException($"Invalid machine diagnostics timestamp '{occurredUtcRaw}' for event '{reader.GetString(0)}'.");
            }

            events.Add(new ThemeMachineDiagnosticEvent
            {
                EventId = reader.GetString(0),
                SessionId = reader.GetString(1),
                ThemeId = reader.GetString(2),
                MachineKey = reader.GetString(3),
                DefinitionVersion = reader.GetInt32(4),
                EventType = reader.GetString(5),
                FromStateCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                ToStateCode = reader.IsDBNull(7) ? null : reader.GetString(7),
                TransitionId = reader.IsDBNull(8) ? null : reader.GetString(8),
                ReasonCode = reader.GetString(9),
                PayloadJson = reader.GetString(10),
                OccurredUtc = occurredUtc
            });
        }

        return events;
    }

    private async Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsCoreAsync(string sessionId, int take, CancellationToken cancellationToken)
    {
        var evaluations = new List<ScenarioCandidateEvaluation>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SessionId, EvaluationId, ScenarioId, StageAWillingnessTier, StageBEligible,
                 CharacterAlignmentScore, NarrativeEvidenceScore, PreferencePriorityScore,
                 FitScore, UnpenalizedFitScore, Confidence, TieBreakKey, Rationale, DetailsJson,
                 SuccessorCausalityBoost, EvaluatedUtc
            FROM RolePlayV2CandidateEvaluations
            WHERE SessionId = $sessionId
            ORDER BY EvaluatedUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 500));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            evaluations.Add(new ScenarioCandidateEvaluation
            {
                SessionId = reader.GetString(0),
                EvaluationId = reader.GetString(1),
                ScenarioId = reader.GetString(2),
                StageAWillingnessTier = reader.GetString(3),
                StageBEligible = reader.GetInt32(4) == 1,
                CharacterAlignmentScore = reader.GetDecimal(5),
                NarrativeEvidenceScore = reader.GetDecimal(6),
                PreferencePriorityScore = reader.GetDecimal(7),
                FitScore = reader.GetDecimal(8),
                UnpenalizedFitScore = reader.GetDecimal(9),
                Confidence = reader.GetDecimal(10),
                TieBreakKey = reader.GetString(11),
                Rationale = reader.GetString(12),
                DetailsJson = reader.GetString(13),
                SuccessorCausalityBoost = reader.IsDBNull(14) ? 0m : reader.GetDecimal(14),
                EvaluatedUtc = DateTime.TryParse(reader.GetString(15), out var evaluatedUtc) ? evaluatedUtc : DateTime.UtcNow
            });
        }

        evaluations.Reverse();
        return evaluations;
    }

    public async Task SaveEncounterSummaryAsync(EncounterSummaryRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RolePlayV2EncounterSummaries (
                Id, SessionId, CharacterId, SummaryType, CycleIndex, FromPhase, ToPhase,
                OccurredUtc, InteractionCountInPhase, SceneLocation, ActiveThemeId,
                FinishingMoveId, PositionIdsJson, CharacterStatsSnapshotJson,
                TemplateSummary, LlmSummary, LlmEnhancedUtc)
            VALUES (
                $id, $sessionId, $characterId, $summaryType, $cycleIndex, $fromPhase, $toPhase,
                $occurredUtc, $interactionCountInPhase, $sceneLocation, $activeThemeId,
                $finishingMoveId, $positionIdsJson, $characterStatsSnapshotJson,
                $templateSummary, $llmSummary, $llmEnhancedUtc);
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$sessionId", record.SessionId);
        cmd.Parameters.AddWithValue("$characterId", record.CharacterId);
        cmd.Parameters.AddWithValue("$summaryType", record.SummaryType.ToString());
        cmd.Parameters.AddWithValue("$cycleIndex", record.CycleIndex);
        cmd.Parameters.AddWithValue("$fromPhase", record.FromPhase.ToString());
        cmd.Parameters.AddWithValue("$toPhase", record.ToPhase.ToString());
        cmd.Parameters.AddWithValue("$occurredUtc", record.OccurredUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$interactionCountInPhase", record.InteractionCountInPhase);
        cmd.Parameters.AddWithValue("$sceneLocation", (object?)record.SceneLocation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$activeThemeId", (object?)record.ActiveThemeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finishingMoveId", (object?)record.FinishingMoveId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$positionIdsJson", record.PositionIdsJson ?? "[]");
        cmd.Parameters.AddWithValue("$characterStatsSnapshotJson", record.CharacterStatsSnapshotJson);
        cmd.Parameters.AddWithValue("$templateSummary", string.IsNullOrEmpty(record.TemplateSummary) ? (object)DBNull.Value : record.TemplateSummary);
        cmd.Parameters.AddWithValue("$llmSummary", (object?)record.LlmSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$llmEnhancedUtc", record.LlmEnhancedUtc.HasValue ? (object)record.LlmEnhancedUtc.Value.ToString("O") : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateEncounterSummaryLlmAsync(string summaryId, string llmSummary, DateTime llmEnhancedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE RolePlayV2EncounterSummaries
            SET LlmSummary = $llmSummary, LlmEnhancedUtc = $llmEnhancedUtc
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", summaryId);
        cmd.Parameters.AddWithValue("$llmSummary", llmSummary);
        cmd.Parameters.AddWithValue("$llmEnhancedUtc", llmEnhancedUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EncounterSummaryRecord>> LoadEncounterSummariesForSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var results = new List<EncounterSummaryRecord>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CharacterId, SummaryType, CycleIndex, FromPhase, ToPhase,
                   OccurredUtc, InteractionCountInPhase, SceneLocation, ActiveThemeId,
                   FinishingMoveId, PositionIdsJson, CharacterStatsSnapshotJson,
                   TemplateSummary, LlmSummary, LlmEnhancedUtc
            FROM RolePlayV2EncounterSummaries
            WHERE SessionId = $sessionId
            ORDER BY OccurredUtc ASC;
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
        {
            results.Add(new EncounterSummaryRecord
            {
                Id           = rdr.GetString(0),
                SessionId    = sessionId,
                CharacterId  = rdr.GetString(1),
                SummaryType  = Enum.Parse<EncounterSummaryType>(rdr.GetString(2)),
                CycleIndex   = rdr.GetInt32(3),
                FromPhase    = Enum.Parse<NarrativePhase>(rdr.GetString(4)),
                ToPhase      = Enum.Parse<NarrativePhase>(rdr.GetString(5)),
                OccurredUtc  = ParseUtcTimestamp(rdr.GetString(6), sessionId),
                InteractionCountInPhase = rdr.GetInt32(7),
                SceneLocation           = rdr.IsDBNull(8) ? null : rdr.GetString(8),
                ActiveThemeId           = rdr.IsDBNull(9) ? null : rdr.GetString(9),
                FinishingMoveId         = rdr.IsDBNull(10) ? null : rdr.GetString(10),
                PositionIdsJson         = rdr.IsDBNull(11) ? "[]" : rdr.GetString(11),
                CharacterStatsSnapshotJson = rdr.IsDBNull(12) ? "{}" : rdr.GetString(12),
                TemplateSummary         = rdr.IsDBNull(13) ? string.Empty : rdr.GetString(13),
                LlmSummary              = rdr.IsDBNull(14) ? null : rdr.GetString(14),
                LlmEnhancedUtc          = rdr.IsDBNull(15) ? null : ParseUtcTimestamp(rdr.GetString(15), sessionId)
            });
        }
        return results;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureThemeMachineDiagnosticsSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RolePlayV2ThemeMachineDiagnostics (
                EventId TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                ThemeId TEXT NOT NULL,
                MachineKey TEXT NOT NULL,
                DefinitionVersion INTEGER NOT NULL,
                EventType TEXT NOT NULL,
                FromStateCode TEXT NULL,
                ToStateCode TEXT NULL,
                TransitionId TEXT NULL,
                ReasonCode TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                OccurredUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RolePlayV2ThemeMachineDiagnostics_Session_OccurredUtc
                ON RolePlayV2ThemeMachineDiagnostics (SessionId, OccurredUtc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// T014 backward compat: find the first character with Role="Husband" in the session's
    /// linked scenario so we can synthesize <see cref="AdaptiveScenarioState.CharacterEncounterProfileIds"/>
    /// from the legacy <c>HusbandAwarenessProfileId</c> column on sessions saved before B-042.
    /// </summary>
    private static async Task<string?> TryFindHusbandCharacterIdAsync(
        SqliteConnection connection,
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Step 1: resolve the scenario ID from the session payload
        await using var sessionCmd = connection.CreateCommand();
        sessionCmd.CommandText = "SELECT json_extract(PayloadJson, '$.ScenarioId') FROM Sessions WHERE Id = $sessionId";
        sessionCmd.Parameters.AddWithValue("$sessionId", sessionId);
        var scenarioId = Convert.ToString(await sessionCmd.ExecuteScalarAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            return null;
        }

        // Step 2: get the characters JSON from the scenario payload
        await using var scenarioCmd = connection.CreateCommand();
        scenarioCmd.CommandText = "SELECT json_extract(PayloadJson, '$.Characters') FROM Scenarios WHERE Id = $scenarioId";
        scenarioCmd.Parameters.AddWithValue("$scenarioId", scenarioId);
        var charactersJson = Convert.ToString(await scenarioCmd.ExecuteScalarAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(charactersJson))
        {
            return null;
        }

        // Step 3: deserialize and find the first Husband character
        try
        {
            var characters = JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(charactersJson);
            if (characters is null)
            {
                return null;
            }

            foreach (var c in characters)
            {
                if (c.TryGetProperty("Role", out var roleProp)
                    && string.Equals(roleProp.GetString(), "Husband", StringComparison.OrdinalIgnoreCase)
                    && c.TryGetProperty("Id", out var idProp))
                {
                    return idProp.GetString();
                }
            }
        }
        catch
        {
            // ignore deserialization errors — backward compat is best-effort
        }

        return null;
    }
}
