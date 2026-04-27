using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioEngineSettingsRepository : IScenarioEngineSettingsRepository
{
    private const string SingletonId = "default";

    private readonly PersistenceOptions _options;

    public ScenarioEngineSettingsRepository(
        IOptions<PersistenceOptions> options)
    {
        _options = options.Value;
    }

    public async Task<ScenarioEngineSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT SettingsJson FROM ScenarioEngineSettings WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SingletonId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            throw new InvalidOperationException(
                "ScenarioEngineSettings row not found in database. " +
                "Required engine configuration is missing; configure engine settings in the UI before starting a session.");
        }

        try
        {
            return JsonSerializer.Deserialize<ScenarioEngineSettings>(result.ToString()!)
                ?? throw new InvalidOperationException(
                    "ScenarioEngineSettings deserialized to null; the stored JSON is empty or invalid.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to deserialize ScenarioEngineSettings from database. The stored JSON is corrupt.", ex);
        }
    }

    public async Task SaveAsync(ScenarioEngineSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(settings);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ScenarioEngineSettings (Id, SettingsJson, UpdatedUtc)
            VALUES ($id, $json, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                SettingsJson = $json,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", SingletonId);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
