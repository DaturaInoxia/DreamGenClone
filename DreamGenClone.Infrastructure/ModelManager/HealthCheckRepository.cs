using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.ModelManager;

public sealed class HealthCheckRepository : IHealthCheckRepository
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<HealthCheckRepository> _logger;

    public HealthCheckRepository(IOptions<PersistenceOptions> options, ILogger<HealthCheckRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SaveBatchAsync(List<HealthCheckResult> results, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Clear existing results first
        var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM HealthCheckResults";
        await deleteCmd.ExecuteNonQueryAsync(cancellationToken);

        foreach (var result in results)
        {
            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO HealthCheckResults (Id, EntityType, EntityId, EntityName, ProviderName, IsHealthy, Message, CheckedUtc)
                VALUES ($id, $entityType, $entityId, $entityName, $providerName, $isHealthy, $message, $checkedUtc)
                """;

            command.Parameters.AddWithValue("$id", result.Id);
            command.Parameters.AddWithValue("$entityType", (int)result.EntityType);
            command.Parameters.AddWithValue("$entityId", result.EntityId);
            command.Parameters.AddWithValue("$entityName", result.EntityName);
            command.Parameters.AddWithValue("$providerName", result.ProviderName);
            command.Parameters.AddWithValue("$isHealthy", result.IsHealthy ? 1 : 0);
            command.Parameters.AddWithValue("$message", result.Message);
            command.Parameters.AddWithValue("$checkedUtc", result.CheckedUtc);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Saved {Count} health check results", results.Count);
    }

    public async Task<List<HealthCheckResult>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, EntityType, EntityId, EntityName, ProviderName, IsHealthy, Message, CheckedUtc FROM HealthCheckResults ORDER BY EntityType, ProviderName, EntityName";

        var results = new List<HealthCheckResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new HealthCheckResult
            {
                Id = reader.GetString(0),
                EntityType = (HealthCheckEntityType)reader.GetInt32(1),
                EntityId = reader.GetString(2),
                EntityName = reader.GetString(3),
                ProviderName = reader.GetString(4),
                IsHealthy = reader.GetInt32(5) == 1,
                Message = reader.GetString(6),
                CheckedUtc = reader.GetString(7)
            });
        }

        return results;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM HealthCheckResults";
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Cleared all health check results");
    }
}
