using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.Sessions;

public sealed class SessionService : ISessionService
{
    public const string StorySessionType = "story";
    public const string RolePlaySessionType = "roleplay";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;
    private readonly ILogger<SessionService> _logger;

    public SessionService(IOptions<PersistenceOptions> options, ILogger<SessionService> logger)
    {
        _connectionString = options.Value.ConnectionString;
        _logger = logger;
    }

    public Task SaveStorySessionAsync(StorySession session, CancellationToken cancellationToken = default)
    {
        return SaveAsync(session.Id, StorySessionType, session.Title, JsonSerializer.Serialize(session, JsonOptions), cancellationToken);
    }

    public Task SaveRolePlaySessionAsync(RolePlaySession session, CancellationToken cancellationToken = default)
    {
        // RP sessions persist the full session blob in PayloadJson. V2 adaptive state is
        // persisted exclusively to dedicated V2 tables via IRolePlayStateRepository.
        // AdaptiveStateJson is a legacy column; pass null to clear any stale data on each save.
        // Sync the runtime CharacterStats dictionary into CharacterSnapshots so the persisted
        // blob always contains up-to-date character data (CharacterStats is [JsonIgnore]).
        session.AdaptiveState.SyncCharacterSnapshots();
        var payloadJson = JsonSerializer.Serialize(session, JsonOptions);
        return SaveRolePlayAsync(
            session.Id,
            session.Title,
            payloadJson,
            null,
            session.MaxMilestonesToInject,
            session.MaxArcCompletionsToInject,
            session.MaxEncounterCompletionsToInject,
            cancellationToken);
    }

    public async Task<StorySession?> LoadStorySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var row = await LoadRowAsync(sessionId, cancellationToken);
        if (row is null || !string.Equals(row.SessionType, StorySessionType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return JsonSerializer.Deserialize<StorySession>(row.PayloadJson, JsonOptions);
    }

    public async Task<RolePlaySession?> LoadRolePlaySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var row = await LoadRowAsync(sessionId, cancellationToken);
        if (row is null || !string.Equals(row.SessionType, RolePlaySessionType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var session = JsonSerializer.Deserialize<RolePlaySession>(row.PayloadJson, JsonOptions);
        // AdaptiveStateJson is a legacy V1 column. V2 adaptive state is loaded exclusively from
        // dedicated V2 tables via IRolePlayStateRepository. Do not overlay from this column.
        //
        // PromptText invariant: Old interactions (created before B-053) will have null PromptText.
        // We do NOT retroactively populate PromptText for these interactions. Only new interactions
        // created after the feature implementation will have PromptText populated.
        NormalizeRolePlaySession(session);
        return session;
    }

    public async Task<IReadOnlyList<SessionListItem>> GetSessionsByTypeAsync(string sessionType, CancellationToken cancellationToken = default)
    {
        var results = new List<SessionListItem>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionType, Name, UpdatedUtc, PayloadJson
            FROM Sessions
            WHERE SessionType = $sessionType
            ORDER BY UpdatedUtc DESC;
            """;
        command.Parameters.AddWithValue("$sessionType", sessionType);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var name = reader.GetString(2);
            var updatedUtc = DateTime.Parse(reader.GetString(3));
            var payloadJson = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

            var listItem = new SessionListItem
            {
                Id = id,
                SessionType = reader.GetString(1),
                Title = name,
                LastUpdatedUtc = updatedUtc,
                Status = string.Empty,
                InteractionCount = 0
            };

            if (string.Equals(sessionType, RolePlaySessionType, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(payloadJson))
            {
                var rolePlay = JsonSerializer.Deserialize<RolePlaySession>(payloadJson, JsonOptions);
                if (rolePlay is not null)
                {
                    NormalizeRolePlaySession(rolePlay);

                    var status = rolePlay.Status;
                    if (status == RolePlaySessionStatus.NotStarted && rolePlay.Interactions.Count > 0)
                    {
                        status = RolePlaySessionStatus.InProgress;
                    }

                    listItem.Title = string.IsNullOrWhiteSpace(rolePlay.Title) ? name : rolePlay.Title;
                    listItem.Status = status.ToString();
                    listItem.InteractionCount = rolePlay.Interactions.Count;
                    listItem.LastUpdatedUtc = rolePlay.ModifiedAt == default ? updatedUtc : rolePlay.ModifiedAt;
                }
            }

            results.Add(listItem);
        }

        _logger.LogInformation(
            SessionLogEvents.RetrievedSessions,
            "Retrieved {Count} persisted sessions for type {SessionType}",
            results.Count,
            sessionType);
        return results;
    }

    public async Task<SessionExportEnvelope?> GetExportEnvelopeAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var row = await LoadRowAsync(sessionId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(row.PayloadJson);
        return new SessionExportEnvelope
        {
            SchemaVersion = 1,
            SessionType = row.SessionType,
            Payload = document.RootElement.Clone()
        };
    }

    public async Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected > 0)
        {
            _logger.LogInformation(SessionLogEvents.DeletedSession, "Deleted persisted session {SessionId}", sessionId);
        }

        return affected > 0;
    }

    internal Task SaveImportedPayloadAsync(string sessionType, string name, string payloadJson, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString();
        return SaveAsync(id, sessionType, name, payloadJson, cancellationToken);
    }

    private async Task SaveAsync(string id, string sessionType, string name, string payloadJson, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Sessions (Id, SessionType, Name, PayloadJson, UpdatedUtc)
            VALUES ($id, $sessionType, $name, $payloadJson, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                SessionType = excluded.SessionType,
                Name = excluded.Name,
                PayloadJson = excluded.PayloadJson,
                UpdatedUtc = excluded.UpdatedUtc;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$sessionType", sessionType);
        command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? "Untitled Session" : name.Trim());
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation(SessionLogEvents.PersistedSession, "Persisted session {SessionId} as {SessionType}", id, sessionType);
    }

    private async Task SaveRolePlayAsync(string id, string name, string payloadJson, string? adaptiveStateJson, int? maxMilestonesToInject, int? maxArcCompletionsToInject, int? maxEncounterCompletionsToInject, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO Sessions (Id, SessionType, Name, PayloadJson, AdaptiveStateJson, MaxMilestonesToInject, MaxArcCompletionsToInject, MaxEncounterCompletionsToInject, UpdatedUtc)
                VALUES ($id, $sessionType, $name, $payloadJson, $adaptiveStateJson, $maxMilestonesToInject, $maxArcCompletionsToInject, $maxEncounterCompletionsToInject, $updatedUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    SessionType = excluded.SessionType,
                    Name = excluded.Name,
                    PayloadJson = excluded.PayloadJson,
                    AdaptiveStateJson = excluded.AdaptiveStateJson,
                    MaxMilestonesToInject = excluded.MaxMilestonesToInject,
                    MaxArcCompletionsToInject = excluded.MaxArcCompletionsToInject,
                    MaxEncounterCompletionsToInject = excluded.MaxEncounterCompletionsToInject,
                    UpdatedUtc = excluded.UpdatedUtc;
                """;

            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$sessionType", RolePlaySessionType);
            command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? "Untitled Session" : name.Trim());
            command.Parameters.AddWithValue("$payloadJson", payloadJson);
            command.Parameters.AddWithValue("$adaptiveStateJson", (object?)adaptiveStateJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$maxMilestonesToInject", (object?)maxMilestonesToInject ?? DBNull.Value);
            command.Parameters.AddWithValue("$maxArcCompletionsToInject", (object?)maxArcCompletionsToInject ?? DBNull.Value);
            command.Parameters.AddWithValue("$maxEncounterCompletionsToInject", (object?)maxEncounterCompletionsToInject ?? DBNull.Value);
            command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        _logger.LogInformation(SessionLogEvents.PersistedSession, "Persisted session {SessionId} as {SessionType}", id, RolePlaySessionType);
    }

    private async Task<SessionRow?> LoadRowAsync(string sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SessionType, Name, PayloadJson, AdaptiveStateJson, UpdatedUtc FROM Sessions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SessionRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            DateTime.Parse(reader.GetString(5)));
    }

    private sealed record SessionRow(string Id, string SessionType, string Name, string PayloadJson, string? AdaptiveStateJson, DateTime UpdatedUtc);

    private static void NormalizeRolePlaySession(RolePlaySession? session)
    {
        if (session is null)
        {
            return;
        }

        session.AdaptiveState ??= new AdaptiveScenarioState();
        // V2 AdaptiveScenarioState initializes all collections in its constructor; no ??= guards needed.

        // SessionId in AdaptiveScenarioState is the foreign-key used by SaveAdaptiveStateAsync.
        // It is not set in old PayloadJson blobs; always stamp it from the authoritative session Id.
        if (string.IsNullOrWhiteSpace(session.AdaptiveState.SessionId))
        {
            session.AdaptiveState.SessionId = session.Id;
        }

        session.AdaptiveState.CompletedScenarios = Math.Max(
            session.AdaptiveState.CompletedScenarios,
            session.AdaptiveState.ScenarioHistory.Count);
        session.AdaptiveState.InteractionsSinceCommitment = Math.Max(0, session.AdaptiveState.InteractionsSinceCommitment);
        session.AdaptiveState.InteractionsInApproaching = Math.Max(0, session.AdaptiveState.InteractionsInApproaching);

        if (string.IsNullOrWhiteSpace(session.AdaptiveIntensityProfileId)
            && !string.IsNullOrWhiteSpace(session.SelectedIntensityProfileId))
        {
            session.AdaptiveIntensityProfileId = session.SelectedIntensityProfileId;
        }

        {
            var state = session.AdaptiveState;
            var isObserving = string.Equals(state.ThemeSelectionRule, "Observing", StringComparison.OrdinalIgnoreCase);

            if (isObserving && !string.IsNullOrWhiteSpace(state.ActiveScenarioId))
            {
                // Clear any stale ActiveScenarioId that leaked into PayloadJson while the tracker
                // is in its observation window. A non-null ActiveScenarioId in Observing state is a
                // race-condition artefact: background jobs loading this payload would see a live
                // scenario, call RecalculateSelectedThemes, and immediately re-lock onto the just-
                // completed scenario — defeating the observation period entirely.
                session.AdaptiveState.ActiveScenarioId = null;
            }
            else if (!isObserving && string.IsNullOrWhiteSpace(state.ActiveScenarioId))
            {
                // Only attempt to repair ActiveScenarioId when the theme tracker has already
                // completed its observation window and promoted a primary theme. While the rule
                // is "Observing" the scenario race is still open; injecting an ActiveScenarioId
                // here would bypass the observation gate and cause the tracker to jump straight
                // to ActiveScenarioLock on the next RecalculateSelectedThemes call.
                var repairedScenarioId = ResolveScenarioIdFromState(state);
                if (!string.IsNullOrWhiteSpace(repairedScenarioId))
                {
                    session.AdaptiveState.ActiveScenarioId = repairedScenarioId;
                }
            }
        }
    }


    private static string? ResolveScenarioIdFromState(AdaptiveScenarioState state)
    {
        if (!string.IsNullOrWhiteSpace(state.PrimaryThemeId)
            && state.ThemeScores.TryGetValue(state.PrimaryThemeId, out var primaryTheme)
            && !primaryTheme.Blocked)
        {
            return primaryTheme.ThemeId;
        }

        return state.ThemeScores.Values
            .Where(x => !x.Blocked)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ThemeId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ThemeId)
            .FirstOrDefault();
    }
}
