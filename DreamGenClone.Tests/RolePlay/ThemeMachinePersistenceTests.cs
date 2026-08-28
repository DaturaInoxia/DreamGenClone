using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ThemeMachinePersistenceTests
{
    [Fact]
    public async Task LoadAdaptiveStateAsync_ThrowsWhenThemeMachineSnapshotJsonIsMalformed()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-machine-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var connectionString = $"Data Source={dbPath}";
            await EnsureAdaptiveStateBaseSchemaAsync(connectionString);

            var repository = new RolePlayStateRepository(
                Options.Create(new PersistenceOptions { ConnectionString = connectionString }));

            await repository.SaveAdaptiveStateAsync(new AdaptiveScenarioState
            {
                SessionId = "session-1",
                ActiveScenarioId = "theme-1",
                CurrentPhase = NarrativePhase.Committed,
                TurnCountInPhase = 1,
                CharacterSnapshots =
                [
                    new CharacterStatProfileV2 { CharacterId = "char-1", Desire = 60, Restraint = 40, Dominance = 50, Loyalty = 50, SelfRespect = 50, RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 55, ["Connection"] = 50 } }
                ]
            });

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();
            await using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE RolePlayV2AdaptiveStates SET ThemeMachineSnapshotJson = $snapshot WHERE SessionId = $sessionId";
                update.Parameters.AddWithValue("$snapshot", "{\"MachineKey\":");
                update.Parameters.AddWithValue("$sessionId", "session-1");
                await update.ExecuteNonQueryAsync();
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.LoadAdaptiveStateAsync("session-1"));

            Assert.Contains("invalid ThemeMachineSnapshotJson payload", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // SQLite can hold the file handle briefly after disposal on Windows.
                }
            }
        }
    }

    [Fact]
    public async Task LoadAdaptiveStateAsync_ThrowsWhenThemeMachineSnapshotJsonIsMissingRequiredFields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-machine-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var connectionString = $"Data Source={dbPath}";
            await EnsureAdaptiveStateBaseSchemaAsync(connectionString);

            var repository = new RolePlayStateRepository(
                Options.Create(new PersistenceOptions { ConnectionString = connectionString }));

            await repository.SaveAdaptiveStateAsync(new AdaptiveScenarioState
            {
                SessionId = "session-1",
                ActiveScenarioId = "theme-1",
                CurrentPhase = NarrativePhase.Committed,
                TurnCountInPhase = 1,
                CharacterSnapshots =
                [
                    new CharacterStatProfileV2 { CharacterId = "char-1", Desire = 60, Restraint = 40, Dominance = 50, Loyalty = 50, SelfRespect = 50, RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 55, ["Connection"] = 50 } }
                ]
            });

            var invalidSnapshot = "{" +
                                  "\"ThemeId\":\"theme-1\"," +
                                  "\"DefinitionId\":\"definition-1\"," +
                                  "\"DefinitionVersion\":1," +
                                  "\"CurrentStateCode\":\"ReturnBeatRequired\"," +
                                  "\"TurnsInCurrentState\":0," +
                                  "\"ReturnBeatCompleted\":false," +
                                  "\"LastEvaluatedUtc\":\"" + DateTime.UtcNow.ToString("O") + "\"" +
                                  "}";

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();
            await using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE RolePlayV2AdaptiveStates SET ThemeMachineSnapshotJson = $snapshot WHERE SessionId = $sessionId";
                update.Parameters.AddWithValue("$snapshot", invalidSnapshot);
                update.Parameters.AddWithValue("$sessionId", "session-1");
                await update.ExecuteNonQueryAsync();
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.LoadAdaptiveStateAsync("session-1"));

            Assert.Contains("missing MachineKey", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // SQLite can hold the file handle briefly after disposal on Windows.
                }
            }
        }
    }

    private static async Task EnsureAdaptiveStateBaseSchemaAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RolePlayV2AdaptiveStates (
                SessionId TEXT PRIMARY KEY,
                ActiveScenarioId TEXT NULL,
                CurrentPhase TEXT NOT NULL,
                TurnCountInPhase INTEGER NOT NULL,
                ConsecutiveLeadCount INTEGER NOT NULL,
                LastEvaluationUtc TEXT NOT NULL,
                CycleIndex INTEGER NOT NULL,
                ActiveFormulaVersion TEXT NOT NULL,
                ActiveVariantId TEXT NULL,
                SelectedWillingnessProfileId TEXT NULL,
                SelectedNarrativeGateProfileId TEXT NULL,
                HusbandAwarenessProfileId TEXT NULL,
                PhaseOverrideFloor TEXT NULL,
                PhaseOverrideScenarioId TEXT NULL,
                PhaseOverrideCycleIndex INTEGER NULL,
                PhaseOverrideSource TEXT NULL,
                PhaseOverrideAppliedUtc TEXT NULL,
                CurrentSceneLocation TEXT NULL,
                CharacterLocationsJson TEXT NOT NULL DEFAULT '[]',
                CharacterLocationPerceptionsJson TEXT NOT NULL DEFAULT '[]',
                CharacterSnapshotsJson TEXT NOT NULL DEFAULT '[]',
                ThemeMachineSnapshotJson TEXT NULL,
                CurrentBeatCode TEXT NULL,
                TurnsInCurrentBeat INTEGER NOT NULL DEFAULT 0,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }
}
