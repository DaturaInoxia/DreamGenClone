using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SemanticInteractionAnalysisRepository : ISemanticInteractionAnalysisRepository
{
    private readonly string _connectionString;

    public SemanticInteractionAnalysisRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task UpsertAsync(SemanticInteractionAnalysisState state, CancellationToken cancellationToken = default)
    {
        ValidateState(state);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RolePlaySemanticInteractionAnalysisState (
                Id,
                SessionId,
                InteractionId,
                CharacterId,
                Status,
                ErrorMessage,
                ResultJson,
                PromptSystem,
                PromptUser,
                RawModelOutput,
                CreatedUtc,
                UpdatedUtc,
                AnalyzedUtc)
            VALUES (
                $id,
                $sessionId,
                $interactionId,
                $characterId,
                $status,
                $errorMessage,
                $resultJson,
                $promptSystem,
                $promptUser,
                $rawModelOutput,
                $createdUtc,
                $updatedUtc,
                $analyzedUtc)
            ON CONFLICT(SessionId, InteractionId) DO UPDATE SET
                CharacterId = excluded.CharacterId,
                Status = excluded.Status,
                ErrorMessage = excluded.ErrorMessage,
                ResultJson = excluded.ResultJson,
                PromptSystem = excluded.PromptSystem,
                PromptUser = excluded.PromptUser,
                RawModelOutput = excluded.RawModelOutput,
                UpdatedUtc = excluded.UpdatedUtc,
                AnalyzedUtc = excluded.AnalyzedUtc;
            """;
        command.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(state.Id) ? Guid.NewGuid().ToString("N") : state.Id);
        command.Parameters.AddWithValue("$sessionId", state.SessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", state.InteractionId.Trim());
        command.Parameters.AddWithValue("$characterId", state.CharacterId.Trim());
        command.Parameters.AddWithValue("$status", state.Status.ToString());
        command.Parameters.AddWithValue("$errorMessage", (object?)state.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$resultJson", (object?)state.ResultJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptSystem", (object?)state.PromptSystem ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptUser", (object?)state.PromptUser ?? DBNull.Value);
        command.Parameters.AddWithValue("$rawModelOutput", (object?)state.RawModelOutput ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", state.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", state.UpdatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$analyzedUtc", state.AnalyzedUtc?.ToString("O") ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SemanticInteractionAnalysisState>> ListBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to list semantic analysis states.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, CharacterId, Status, ErrorMessage, ResultJson, PromptSystem, PromptUser, RawModelOutput, CreatedUtc, UpdatedUtc, AnalyzedUtc
            FROM RolePlaySemanticInteractionAnalysisState
            WHERE SessionId = $sessionId
            ORDER BY UpdatedUtc DESC;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());

        var results = new List<SemanticInteractionAnalysisState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SemanticInteractionAnalysisState
            {
                Id = reader.GetString(0),
                SessionId = reader.GetString(1),
                InteractionId = reader.GetString(2),
                CharacterId = reader.GetString(3),
                Status = ParseStatus(reader.GetString(4), reader.GetString(1), reader.GetString(2)),
                ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
                ResultJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                PromptSystem = reader.IsDBNull(7) ? null : reader.GetString(7),
                PromptUser = reader.IsDBNull(8) ? null : reader.GetString(8),
                RawModelOutput = reader.IsDBNull(9) ? null : reader.GetString(9),
                CreatedUtc = ParseUtc(reader.GetString(10), reader.GetString(1), reader.GetString(2), "CreatedUtc"),
                UpdatedUtc = ParseUtc(reader.GetString(11), reader.GetString(1), reader.GetString(2), "UpdatedUtc"),
                AnalyzedUtc = reader.IsDBNull(12)
                    ? null
                    : ParseUtc(reader.GetString(12), reader.GetString(1), reader.GetString(2), "AnalyzedUtc")
            });
        }

        return results;
    }

    public async Task<SemanticInteractionAnalysisState?> GetBySessionAndInteractionAsync(
        string sessionId,
        string interactionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to load semantic analysis state.");
        }

        if (string.IsNullOrWhiteSpace(interactionId))
        {
            throw new InvalidOperationException("Interaction id is required to load semantic analysis state.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, CharacterId, Status, ErrorMessage, ResultJson, PromptSystem, PromptUser, RawModelOutput, CreatedUtc, UpdatedUtc, AnalyzedUtc
            FROM RolePlaySemanticInteractionAnalysisState
            WHERE SessionId = $sessionId AND InteractionId = $interactionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", interactionId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SemanticInteractionAnalysisState
        {
            Id = reader.GetString(0),
            SessionId = reader.GetString(1),
            InteractionId = reader.GetString(2),
            CharacterId = reader.GetString(3),
            Status = ParseStatus(reader.GetString(4), reader.GetString(1), reader.GetString(2)),
            ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
            ResultJson = reader.IsDBNull(6) ? null : reader.GetString(6),
            PromptSystem = reader.IsDBNull(7) ? null : reader.GetString(7),
            PromptUser = reader.IsDBNull(8) ? null : reader.GetString(8),
            RawModelOutput = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedUtc = ParseUtc(reader.GetString(10), reader.GetString(1), reader.GetString(2), "CreatedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(11), reader.GetString(1), reader.GetString(2), "UpdatedUtc"),
            AnalyzedUtc = reader.IsDBNull(12)
                ? null
                : ParseUtc(reader.GetString(12), reader.GetString(1), reader.GetString(2), "AnalyzedUtc")
        };
    }

    public async Task<SemanticInteractionAnalysisState?> GetLatestBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var all = await ListBySessionAsync(sessionId, cancellationToken);
        return all.FirstOrDefault();
    }

    public async Task<SemanticInteractionAnalysisState?> GetLatestBySessionAndCharacterAsync(
        string sessionId,
        string characterId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required to load semantic analysis watermark.");
        }

        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new InvalidOperationException("Character id is required to load semantic analysis watermark.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SessionId, InteractionId, CharacterId, Status, ErrorMessage, ResultJson, PromptSystem, PromptUser, RawModelOutput, CreatedUtc, UpdatedUtc, AnalyzedUtc
            FROM RolePlaySemanticInteractionAnalysisState
            WHERE SessionId = $sessionId AND CharacterId = $characterId
            ORDER BY UpdatedUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.Trim());
        command.Parameters.AddWithValue("$characterId", characterId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SemanticInteractionAnalysisState
        {
            Id = reader.GetString(0),
            SessionId = reader.GetString(1),
            InteractionId = reader.GetString(2),
            CharacterId = reader.GetString(3),
            Status = ParseStatus(reader.GetString(4), reader.GetString(1), reader.GetString(2)),
            ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
            ResultJson = reader.IsDBNull(6) ? null : reader.GetString(6),
            PromptSystem = reader.IsDBNull(7) ? null : reader.GetString(7),
            PromptUser = reader.IsDBNull(8) ? null : reader.GetString(8),
            RawModelOutput = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedUtc = ParseUtc(reader.GetString(10), reader.GetString(1), reader.GetString(2), "CreatedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(11), reader.GetString(1), reader.GetString(2), "UpdatedUtc"),
            AnalyzedUtc = reader.IsDBNull(12)
                ? null
                : ParseUtc(reader.GetString(12), reader.GetString(1), reader.GetString(2), "AnalyzedUtc")
        };
    }

    private static void ValidateState(SemanticInteractionAnalysisState state)
    {
        if (string.IsNullOrWhiteSpace(state.SessionId))
        {
            throw new InvalidOperationException("Semantic analysis state requires SessionId.");
        }

        if (string.IsNullOrWhiteSpace(state.InteractionId))
        {
            throw new InvalidOperationException("Semantic analysis state requires InteractionId.");
        }

        if (string.IsNullOrWhiteSpace(state.CharacterId))
        {
            throw new InvalidOperationException("Semantic analysis state requires CharacterId.");
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RolePlaySemanticInteractionAnalysisState (
                Id TEXT NOT NULL PRIMARY KEY,
                SessionId TEXT NOT NULL,
                InteractionId TEXT NOT NULL,
                CharacterId TEXT NOT NULL,
                Status TEXT NOT NULL,
                ErrorMessage TEXT NULL,
                ResultJson TEXT NULL,
                PromptSystem TEXT NULL,
                PromptUser TEXT NULL,
                RawModelOutput TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                AnalyzedUtc TEXT NULL,
                UNIQUE (SessionId, InteractionId)
            );

            CREATE INDEX IF NOT EXISTS IX_RolePlaySemanticAnalysis_Session_UpdatedUtc
                ON RolePlaySemanticInteractionAnalysisState (SessionId, UpdatedUtc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Migration: add PromptSystem, PromptUser, RawModelOutput columns to pre-existing tables.
        await MigrateAddColumnAsync(connection, "PromptSystem", "TEXT NULL", cancellationToken);
        await MigrateAddColumnAsync(connection, "PromptUser", "TEXT NULL", cancellationToken);
        await MigrateAddColumnAsync(connection, "RawModelOutput", "TEXT NULL", cancellationToken);
    }

    private static async Task MigrateAddColumnAsync(SqliteConnection connection, string column, string columnDef, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE RolePlaySemanticInteractionAnalysisState ADD COLUMN {column} {columnDef}";
        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Column already exists — migration already applied.
        }
    }

    private static SemanticAnalysisStatus ParseStatus(string value, string sessionId, string interactionId)
    {
        if (Enum.TryParse<SemanticAnalysisStatus>(value, out var status))
        {
            return status;
        }

        throw new InvalidOperationException(
            $"RolePlaySemanticInteractionAnalysisState row for session '{sessionId}' interaction '{interactionId}' has invalid Status value '{value}'.");
    }

    private static DateTime ParseUtc(string value, string sessionId, string interactionId, string columnName)
    {
        if (DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        throw new InvalidOperationException(
            $"RolePlaySemanticInteractionAnalysisState row for session '{sessionId}' interaction '{interactionId}' has invalid {columnName} value '{value}'.");
    }
}
