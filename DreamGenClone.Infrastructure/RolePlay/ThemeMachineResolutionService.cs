using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ThemeMachineResolutionService : IThemeMachineResolutionService
{
    private readonly string _connectionString;

    public ThemeMachineResolutionService(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<RPThemeMachineDefinition?> ResolveAsync(
        string sessionId,
        string activeScenarioId,
        ThemeMachineSessionSnapshot? pinnedSnapshot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required for theme machine resolution.");
        }

        if (string.IsNullOrWhiteSpace(activeScenarioId))
        {
            throw new InvalidOperationException("Active scenario id is required for theme machine resolution.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineSchemaAsync(connection, cancellationToken);

        var themeExists = await ThemeExistsAsync(connection, activeScenarioId, cancellationToken);
        if (!themeExists)
        {
            throw new InvalidOperationException(
                $"Theme machine resolution failed for session '{sessionId}': RP theme '{activeScenarioId}' does not exist.");
        }

        var definitions = await LoadDefinitionsAsync(connection, activeScenarioId, cancellationToken);
        if (definitions.Count == 0)
        {
            if (pinnedSnapshot is not null
                && string.Equals(pinnedSnapshot.ThemeId, activeScenarioId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Theme machine resolution failed for session '{sessionId}': pinned machine snapshot references theme '{activeScenarioId}' but no machine definitions exist.");
            }

            return null;
        }

        RPThemeMachineDefinition selectedDefinition;
        if (pinnedSnapshot is null)
        {
            var activeDefinitions = definitions.Where(x => x.IsActive).ToList();
            if (activeDefinitions.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Theme machine resolution failed for session '{sessionId}' and theme '{activeScenarioId}': no active machine definition found.");
            }

            if (activeDefinitions.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Theme machine resolution failed for session '{sessionId}' and theme '{activeScenarioId}': multiple active machine definitions found.");
            }

            selectedDefinition = activeDefinitions[0];
        }
        else
        {
            if (!string.Equals(pinnedSnapshot.ThemeId, activeScenarioId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Theme machine resolution failed for session '{sessionId}': pinned theme '{pinnedSnapshot.ThemeId}' does not match active scenario theme '{activeScenarioId}'.");
            }

            selectedDefinition = definitions.FirstOrDefault(x =>
                string.Equals(x.DefinitionId, pinnedSnapshot.DefinitionId, StringComparison.OrdinalIgnoreCase)
                && x.Version == pinnedSnapshot.DefinitionVersion)
                ?? throw new InvalidOperationException(
                    $"Theme machine resolution failed for session '{sessionId}': pinned definition '{pinnedSnapshot.DefinitionId}' v{pinnedSnapshot.DefinitionVersion} was not found for theme '{activeScenarioId}'.");

            if (!string.Equals(selectedDefinition.MachineKey, pinnedSnapshot.MachineKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Theme machine resolution failed for session '{sessionId}': pinned machine key '{pinnedSnapshot.MachineKey}' does not match resolved machine key '{selectedDefinition.MachineKey}'.");
            }
        }

        selectedDefinition.States = await LoadStatesAsync(connection, selectedDefinition.DefinitionId, cancellationToken);
        selectedDefinition.Transitions = await LoadTransitionsAsync(connection, selectedDefinition.DefinitionId, cancellationToken);

        ValidateResolvedDefinition(sessionId, activeScenarioId, selectedDefinition, pinnedSnapshot is not null);
        return selectedDefinition;
    }

    private static void ValidateResolvedDefinition(
        string sessionId,
        string activeScenarioId,
        RPThemeMachineDefinition definition,
        bool forPinnedSession)
    {
        if (definition.States.Count == 0)
        {
            throw new InvalidOperationException(
                $"Theme machine resolution failed for session '{sessionId}' and theme '{activeScenarioId}': definition '{definition.DefinitionId}' has no states.");
        }

        var initialStateCount = definition.States.Count(x => x.IsInitial);
        if (initialStateCount != 1)
        {
            throw new InvalidOperationException(
                $"Theme machine resolution failed for session '{sessionId}' and theme '{activeScenarioId}': definition '{definition.DefinitionId}' must have exactly one initial state, found {initialStateCount}.");
        }

        var stateCodes = definition.States
            .Where(x => !string.IsNullOrWhiteSpace(x.StateCode))
            .Select(x => x.StateCode.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (stateCodes.Count != definition.States.Count)
        {
            throw new InvalidOperationException(
                $"Theme machine resolution failed for session '{sessionId}' and theme '{activeScenarioId}': definition '{definition.DefinitionId}' has duplicate or missing state codes.");
        }

        foreach (var transition in definition.Transitions)
        {
            if (!stateCodes.Contains(transition.FromStateCode) || !stateCodes.Contains(transition.ToStateCode))
            {
                throw new InvalidOperationException(
                    $"Theme machine resolution failed for session '{sessionId}' and theme '{activeScenarioId}': transition '{transition.TransitionId}' references missing states.");
            }
        }

        if (!forPinnedSession && !definition.IsActive)
        {
            throw new InvalidOperationException(
                $"Theme machine resolution failed for session '{sessionId}' and theme '{activeScenarioId}': selected definition '{definition.DefinitionId}' is not active.");
        }
    }

    private async Task<bool> ThemeExistsAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM RPThemes WHERE Id = $themeId";
        command.Parameters.AddWithValue("$themeId", themeId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<List<RPThemeMachineDefinition>> LoadDefinitionsAsync(
        SqliteConnection connection,
        string themeId,
        CancellationToken cancellationToken)
    {
        var definitions = new List<RPThemeMachineDefinition>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DefinitionId, ThemeId, MachineKey, Version, Name, IsActive, IsSeeded, CreatedUtc, UpdatedUtc
            FROM RPThemeMachineDefinitions
            WHERE ThemeId = $themeId
            ORDER BY Version DESC, DefinitionId;
            """;
        command.Parameters.AddWithValue("$themeId", themeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            definitions.Add(new RPThemeMachineDefinition
            {
                DefinitionId = reader.GetString(0),
                ThemeId = reader.GetString(1),
                MachineKey = reader.GetString(2),
                Version = reader.GetInt32(3),
                Name = reader.GetString(4),
                IsActive = reader.GetInt32(5) == 1,
                IsSeeded = reader.GetInt32(6) == 1,
                CreatedUtc = ParseUtc(reader.GetString(7), "RPThemeMachineDefinitions.CreatedUtc"),
                UpdatedUtc = ParseUtc(reader.GetString(8), "RPThemeMachineDefinitions.UpdatedUtc")
            });
        }

        return definitions;
    }

    private static async Task<List<RPThemeMachineState>> LoadStatesAsync(
        SqliteConnection connection,
        string definitionId,
        CancellationToken cancellationToken)
    {
        var states = new List<RPThemeMachineState>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT StateId, DefinitionId, StateCode, Label, IsInitial, IsTerminal, SortOrder
            FROM RPThemeMachineStates
            WHERE DefinitionId = $definitionId
            ORDER BY SortOrder, StateId;
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states.Add(new RPThemeMachineState
            {
                StateId = reader.GetString(0),
                DefinitionId = reader.GetString(1),
                StateCode = reader.GetString(2),
                Label = reader.GetString(3),
                IsInitial = reader.GetInt32(4) == 1,
                IsTerminal = reader.GetInt32(5) == 1,
                SortOrder = reader.GetInt32(6)
            });
        }

        return states;
    }

    private static async Task<List<RPThemeMachineTransition>> LoadTransitionsAsync(
        SqliteConnection connection,
        string definitionId,
        CancellationToken cancellationToken)
    {
        var transitions = new List<RPThemeMachineTransition>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TransitionId, DefinitionId, FromStateCode, ToStateCode, Priority, TriggerType,
                   GateConfigJson, BlockReasonCode, IsEnabled, CreatedUtc, UpdatedUtc
            FROM RPThemeMachineTransitions
            WHERE DefinitionId = $definitionId
            ORDER BY Priority, TransitionId;
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            transitions.Add(new RPThemeMachineTransition
            {
                TransitionId = reader.GetString(0),
                DefinitionId = reader.GetString(1),
                FromStateCode = reader.GetString(2),
                ToStateCode = reader.GetString(3),
                Priority = reader.GetInt32(4),
                TriggerType = reader.GetString(5),
                GateConfigJson = reader.GetString(6),
                BlockReasonCode = reader.GetString(7),
                IsEnabled = reader.GetInt32(8) == 1,
                CreatedUtc = ParseUtc(reader.GetString(9), "RPThemeMachineTransitions.CreatedUtc"),
                UpdatedUtc = ParseUtc(reader.GetString(10), "RPThemeMachineTransitions.UpdatedUtc")
            });
        }

        return transitions;
    }

    private static DateTime ParseUtc(string raw, string fieldName)
    {
        if (!DateTime.TryParse(raw, null, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new InvalidOperationException($"Invalid UTC timestamp for {fieldName}: '{raw}'.");
        }

        return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureThemeMachineSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RPThemeMachineDefinitions (
                DefinitionId TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                MachineKey TEXT NOT NULL,
                Version INTEGER NOT NULL,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 0,
                IsSeeded INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE,
                UNIQUE (ThemeId, MachineKey, Version)
            );

            CREATE TABLE IF NOT EXISTS RPThemeMachineStates (
                StateId TEXT PRIMARY KEY,
                DefinitionId TEXT NOT NULL,
                StateCode TEXT NOT NULL,
                Label TEXT NOT NULL,
                IsInitial INTEGER NOT NULL DEFAULT 0,
                IsTerminal INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (DefinitionId) REFERENCES RPThemeMachineDefinitions(DefinitionId) ON DELETE CASCADE,
                UNIQUE (DefinitionId, StateCode)
            );

            CREATE TABLE IF NOT EXISTS RPThemeMachineTransitions (
                TransitionId TEXT PRIMARY KEY,
                DefinitionId TEXT NOT NULL,
                FromStateCode TEXT NOT NULL,
                ToStateCode TEXT NOT NULL,
                Priority INTEGER NOT NULL,
                TriggerType TEXT NOT NULL,
                GateConfigJson TEXT NOT NULL,
                BlockReasonCode TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (DefinitionId) REFERENCES RPThemeMachineDefinitions(DefinitionId) ON DELETE CASCADE,
                UNIQUE (DefinitionId, FromStateCode, Priority)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
