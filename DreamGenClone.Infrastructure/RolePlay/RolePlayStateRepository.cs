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
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureAdaptiveStateSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RolePlayV2AdaptiveStates (
                SessionId, ActiveScenarioId, CurrentPhase, InteractionCountInPhase, ConsecutiveLeadCount,
                LastEvaluationUtc, CycleIndex, ActiveFormulaVersion, ActiveVariantId,
                SelectedWillingnessProfileId, SelectedNarrativeGateProfileId, HusbandAwarenessProfileId,
                PhaseOverrideFloor, PhaseOverrideScenarioId, PhaseOverrideCycleIndex, PhaseOverrideSource, PhaseOverrideAppliedUtc,
                CurrentSceneLocation,
                CharacterLocationsJson, CharacterLocationPerceptionsJson, CharacterSnapshotsJson, UpdatedUtc)
            VALUES (
                $sessionId, $activeScenarioId, $currentPhase, $interactionCountInPhase, $consecutiveLeadCount,
                $lastEvaluationUtc, $cycleIndex, $activeFormulaVersion, $activeVariantId,
                $selectedWillingnessProfileId, $selectedNarrativeGateProfileId, $husbandAwarenessProfileId,
                $phaseOverrideFloor, $phaseOverrideScenarioId, $phaseOverrideCycleIndex, $phaseOverrideSource, $phaseOverrideAppliedUtc,
                $currentSceneLocation,
                $characterLocationsJson, $characterLocationPerceptionsJson, $characterSnapshotsJson, $updatedUtc)
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
                HusbandAwarenessProfileId = excluded.HusbandAwarenessProfileId,
                PhaseOverrideFloor = excluded.PhaseOverrideFloor,
                PhaseOverrideScenarioId = excluded.PhaseOverrideScenarioId,
                PhaseOverrideCycleIndex = excluded.PhaseOverrideCycleIndex,
                PhaseOverrideSource = excluded.PhaseOverrideSource,
                PhaseOverrideAppliedUtc = excluded.PhaseOverrideAppliedUtc,
                CurrentSceneLocation = excluded.CurrentSceneLocation,
                CharacterLocationsJson = excluded.CharacterLocationsJson,
                CharacterLocationPerceptionsJson = excluded.CharacterLocationPerceptionsJson,
                CharacterSnapshotsJson = excluded.CharacterSnapshotsJson,
                UpdatedUtc = excluded.UpdatedUtc;
            """;

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
        command.Parameters.AddWithValue("$husbandAwarenessProfileId", (object?)state.HusbandAwarenessProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideFloor", state.PhaseOverrideFloor?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideScenarioId", (object?)state.PhaseOverrideScenarioId ?? DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideCycleIndex", state.PhaseOverrideCycleIndex ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideSource", (object?)state.PhaseOverrideSource ?? DBNull.Value);
        command.Parameters.AddWithValue("$phaseOverrideAppliedUtc", state.PhaseOverrideAppliedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$currentSceneLocation", (object?)state.CurrentSceneLocation ?? DBNull.Value);
        command.Parameters.AddWithValue("$characterLocationsJson", JsonSerializer.Serialize(state.CharacterLocations));
        command.Parameters.AddWithValue("$characterLocationPerceptionsJson", JsonSerializer.Serialize(state.CharacterLocationPerceptions));
        command.Parameters.AddWithValue("$characterSnapshotsJson", JsonSerializer.Serialize(state.CharacterSnapshots));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                                PhaseOverrideFloor, PhaseOverrideScenarioId, PhaseOverrideCycleIndex, PhaseOverrideSource, PhaseOverrideAppliedUtc,
                                CurrentSceneLocation, CharacterLocationsJson, CharacterLocationPerceptionsJson, CharacterSnapshotsJson
            FROM RolePlayV2AdaptiveStates
            WHERE SessionId = $sessionId;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdaptiveScenarioState
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
            HusbandAwarenessProfileId = reader.IsDBNull(11) ? null : reader.GetString(11),
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
            CharacterSnapshots = JsonSerializer.Deserialize<List<CharacterStatProfileV2>>(reader.GetString(20)) ?? []
        };
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
                    FitScore, UnpenalizedFitScore, Confidence, TieBreakKey, Rationale, DetailsJson, EvaluatedUtc)
                VALUES (
                    $sessionId, $evaluationId, $scenarioId, $tier, $eligible,
                    $characterAlignmentScore, $narrativeEvidenceScore, $preferencePriorityScore,
                    $fitScore, $unpenalizedFitScore, $confidence, $tieBreakKey, $rationale, $detailsJson, $evaluatedUtc);
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

        events.Reverse();
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

    private async Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsCoreAsync(string sessionId, int take, CancellationToken cancellationToken)
    {
        var evaluations = new List<ScenarioCandidateEvaluation>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SessionId, EvaluationId, ScenarioId, StageAWillingnessTier, StageBEligible,
                 CharacterAlignmentScore, NarrativeEvidenceScore, PreferencePriorityScore,
                 FitScore, UnpenalizedFitScore, Confidence, TieBreakKey, Rationale, DetailsJson, EvaluatedUtc
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
                EvaluatedUtc = DateTime.TryParse(reader.GetString(14), out var evaluatedUtc) ? evaluatedUtc : DateTime.UtcNow
            });
        }

        evaluations.Reverse();
        return evaluations;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
