using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Persistence;

public sealed class SqlitePersistence : ISqlitePersistence
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<SqlitePersistence> _logger;

    public SqlitePersistence(IOptions<PersistenceOptions> options, ILogger<SqlitePersistence> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (!string.IsNullOrWhiteSpace(connectionStringBuilder.DataSource))
        {
            var dataSourcePath = Path.GetFullPath(connectionStringBuilder.DataSource);
            var dataDirectory = Path.GetDirectoryName(dataSourcePath);
            if (!string.IsNullOrWhiteSpace(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Sessions (
                Id TEXT PRIMARY KEY,
                SessionType TEXT NOT NULL,
                Name TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Templates (
                Id TEXT PRIMARY KEY,
                TemplateType TEXT NOT NULL,
                Name TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                ImagePath TEXT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Scenarios (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("SQLite persistence initialized using {ConnectionString}", _options.ConnectionString);
    }

    public async Task SaveScenarioAsync(string id, string name, string payloadJson, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Scenarios (Id, Name, PayloadJson, UpdatedUtc)
            VALUES ($id, $name, $payloadJson, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                PayloadJson = $payloadJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Scenario persisted: {ScenarioId}", id);
    }

    public async Task<(string Id, string Name, string PayloadJson, string UpdatedUtc)?> LoadScenarioAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, PayloadJson, UpdatedUtc FROM Scenarios WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            );
        }

        return null;
    }

    public async Task<List<(string Id, string Name, string PayloadJson, string UpdatedUtc)>> LoadAllScenariosAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, PayloadJson, UpdatedUtc FROM Scenarios ORDER BY UpdatedUtc DESC";

        var results = new List<(string, string, string, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            ));
        }

        _logger.LogInformation("Loaded {ScenarioCount} scenarios from database", results.Count);
        return results;
    }

    public async Task<bool> DeleteScenarioAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Scenarios WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Scenario deletion attempted: {ScenarioId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }
}
