using System.Text.Encodings.Web;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayDebugEventService : IRolePlayDebugEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly string _connectionString;
    private readonly ILogger<RolePlayDebugEventService> _logger;

    public RolePlayDebugEventService(IOptions<PersistenceOptions> options, ILogger<RolePlayDebugEventService> logger)
    {
        _connectionString = options.Value.ConnectionString;
        _logger = logger;
    }

    public async Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(record.SessionId))
        {
            return;
        }

        record.EventId = string.IsNullOrWhiteSpace(record.EventId) ? Guid.NewGuid().ToString("N") : record.EventId;
        record.CreatedUtc = record.CreatedUtc == default ? DateTime.UtcNow : record.CreatedUtc;
        record.EventKind = string.IsNullOrWhiteSpace(record.EventKind) ? "General" : record.EventKind.Trim();
        record.Severity = string.IsNullOrWhiteSpace(record.Severity) ? "Info" : record.Severity.Trim();
        record.Summary = record.Summary ?? string.Empty;
        record.MetadataJson = NormalizeJson(record.MetadataJson);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RolePlayDebugEvents (
                Id,
                SessionId,
                CorrelationId,
                InteractionId,
                EventKind,
                Severity,
                ActorName,
                ModelIdentifier,
                ProviderName,
                DurationMs,
                Summary,
                MetadataJson,
                CreatedUtc)
            VALUES (
                $id,
                $sessionId,
                $correlationId,
                $interactionId,
                $eventKind,
                $severity,
                $actorName,
                $modelIdentifier,
                $providerName,
                $durationMs,
                $summary,
                $metadataJson,
                $createdUtc);
            """;

        command.Parameters.AddWithValue("$id", record.EventId);
        command.Parameters.AddWithValue("$sessionId", record.SessionId);
        command.Parameters.AddWithValue("$correlationId", (object?)record.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$interactionId", (object?)record.InteractionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$eventKind", record.EventKind);
        command.Parameters.AddWithValue("$severity", record.Severity);
        command.Parameters.AddWithValue("$actorName", (object?)record.ActorName ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelIdentifier", (object?)record.ModelIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerName", (object?)record.ProviderName ?? DBNull.Value);
        command.Parameters.AddWithValue("$durationMs", (object?)record.DurationMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", record.Summary);
        command.Parameters.AddWithValue("$metadataJson", record.MetadataJson);
        command.Parameters.AddWithValue("$createdUtc", record.CreatedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RolePlayDebugEventRecord>> QuerySessionEventsAsync(
        string sessionId,
        string? eventKind = null,
        string? search = null,
        int take = 300,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return [];
        }

        var limit = Math.Clamp(take, 20, 2000);
        var events = new List<RolePlayDebugEventRecord>(limit);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, CorrelationId, InteractionId, EventKind, Severity,
                   ActorName, ModelIdentifier, ProviderName, DurationMs, Summary,
                   MetadataJson, CreatedUtc
            FROM RolePlayDebugEvents
            WHERE SessionId = $sessionId
              AND ($eventKind IS NULL OR EventKind = $eventKind)
              AND (
                    $search IS NULL
                    OR Summary LIKE '%' || $search || '%'
                    OR MetadataJson LIKE '%' || $search || '%'
                  )
            ORDER BY CreatedUtc DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$eventKind", (object?)eventKind ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());
        command.Parameters.AddWithValue("$take", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new RolePlayDebugEventRecord
            {
                EventId = reader.GetString(0),
                SessionId = reader.GetString(1),
                CorrelationId = reader.IsDBNull(2) ? null : reader.GetString(2),
                InteractionId = reader.IsDBNull(3) ? null : reader.GetString(3),
                EventKind = reader.GetString(4),
                Severity = reader.GetString(5),
                ActorName = reader.IsDBNull(6) ? null : reader.GetString(6),
                ModelIdentifier = reader.IsDBNull(7) ? null : reader.GetString(7),
                ProviderName = reader.IsDBNull(8) ? null : reader.GetString(8),
                DurationMs = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                Summary = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                MetadataJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11),
                CreatedUtc = DateTime.TryParse(reader.GetString(12), out var createdUtc)
                    ? createdUtc
                    : DateTime.UtcNow
            });
        }

        events.Reverse();
        return events;
    }

    public Task<IReadOnlyList<string>> GetRecentLogLinesAsync(
        string? sessionId,
        string? correlationId,
        int take = 250,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        var limit = Math.Clamp(take, 50, 2000);
        var logsDirectory = ResolveLogsDirectory();
        if (string.IsNullOrWhiteSpace(logsDirectory) || !Directory.Exists(logsDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>(lines);
        }

        var candidates = Directory.EnumerateFiles(logsDirectory, "dreamgenclone-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(3)
            .ToList();

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> fileLines;
            try
            {
                fileLines = File.ReadLines(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read log file {LogFile}", file);
                continue;
            }

            foreach (var line in fileLines)
            {
                var hasSession = string.IsNullOrWhiteSpace(sessionId)
                    || line.Contains(sessionId, StringComparison.OrdinalIgnoreCase);
                var hasCorrelation = string.IsNullOrWhiteSpace(correlationId)
                    || line.Contains(correlationId, StringComparison.OrdinalIgnoreCase);

                if (hasSession && hasCorrelation)
                {
                    lines.Add(line);
                }
            }
        }

        if (lines.Count > limit)
        {
            lines = lines[^limit..];
        }

        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    private static string NormalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(parsed, JsonOptions);
        }
        catch
        {
            return JsonSerializer.Serialize(new { value = json }, JsonOptions);
        }
    }

    private static string? ResolveLogsDirectory()
    {
        var current = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        if (Directory.Exists(current))
        {
            return current;
        }

        var baseLogs = Path.Combine(AppContext.BaseDirectory, "logs");
        if (Directory.Exists(baseLogs))
        {
            return baseLogs;
        }

        return null;
    }
}
