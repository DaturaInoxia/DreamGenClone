using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioEngineSettingsRepository : IScenarioEngineSettingsRepository
{
    private const string SingletonId = "default";

    private readonly PersistenceOptions _options;
    private readonly ILogger<ScenarioEngineSettingsRepository> _logger;

    public ScenarioEngineSettingsRepository(
        IOptions<PersistenceOptions> options,
        ILogger<ScenarioEngineSettingsRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
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
            return new ScenarioEngineSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<ScenarioEngineSettings>(result.ToString()!)
                ?? new ScenarioEngineSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize ScenarioEngineSettings; returning defaults");
            return new ScenarioEngineSettings();
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
        _logger.LogInformation("Saved ScenarioEngineSettings");
    }
}
