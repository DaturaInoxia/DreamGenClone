using System.Text.Json;
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
        return SaveAsync(session.Id, RolePlaySessionType, session.Title, JsonSerializer.Serialize(session, JsonOptions), cancellationToken);
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

    private async Task<SessionRow?> LoadRowAsync(string sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SessionType, Name, PayloadJson, UpdatedUtc FROM Sessions WHERE Id = $id;";
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
            DateTime.Parse(reader.GetString(4)));
    }

    private sealed record SessionRow(string Id, string SessionType, string Name, string PayloadJson, DateTime UpdatedUtc);

    private static void NormalizeRolePlaySession(RolePlaySession? session)
    {
        if (session is null)
        {
            return;
        }

        session.AdaptiveState ??= new RolePlayAdaptiveState();
        session.AdaptiveState.ThemeTracker ??= new ThemeTrackerState();
        session.AdaptiveState.ThemeTracker.Themes ??= new Dictionary<string, ThemeTrackerItem>(StringComparer.OrdinalIgnoreCase);
        session.AdaptiveState.ThemeTracker.RecentEvidence ??= [];
        session.AdaptiveState.CharacterStats ??= new Dictionary<string, CharacterStatBlock>(StringComparer.OrdinalIgnoreCase);
        session.AdaptiveState.PairwiseStats ??= new Dictionary<string, PairwiseStatBlock>(StringComparer.OrdinalIgnoreCase);
        session.AdaptiveState.ScenarioHistory ??= [];

        session.AdaptiveState.CompletedScenarios = Math.Max(
            session.AdaptiveState.CompletedScenarios,
            session.AdaptiveState.ScenarioHistory.Count);
        session.AdaptiveState.InteractionsSinceCommitment = Math.Max(0, session.AdaptiveState.InteractionsSinceCommitment);
        session.AdaptiveState.InteractionsInApproaching = Math.Max(0, session.AdaptiveState.InteractionsInApproaching);

        if (session.AdaptiveState.ActiveScenarioId is null)
        {
            if (session.AdaptiveState.CurrentNarrativePhase is NarrativePhase.Committed
                or NarrativePhase.Approaching
                or NarrativePhase.Climax)
            {
                session.AdaptiveState.CurrentNarrativePhase = NarrativePhase.BuildUp;
            }
        }
        else if (session.AdaptiveState.CurrentNarrativePhase is NarrativePhase.BuildUp or NarrativePhase.Reset)
        {
            session.AdaptiveState.CurrentNarrativePhase = NarrativePhase.Committed;
        }
    }
}
