using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.ModelManager;

public sealed class RegisteredModelRepository : IRegisteredModelRepository
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<RegisteredModelRepository> _logger;

    public RegisteredModelRepository(IOptions<PersistenceOptions> options, ILogger<RegisteredModelRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RegisteredModels (Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, CreatedUtc)
            VALUES ($id, $providerId, $identifier, $displayName, $enabled, $created)
            ON CONFLICT(Id) DO UPDATE SET
                ProviderId = $providerId,
                ModelIdentifier = $identifier,
                DisplayName = $displayName,
                IsEnabled = $enabled
            """;

        command.Parameters.AddWithValue("$id", model.Id);
        command.Parameters.AddWithValue("$providerId", model.ProviderId);
        command.Parameters.AddWithValue("$identifier", model.ModelIdentifier);
        command.Parameters.AddWithValue("$displayName", model.DisplayName);
        command.Parameters.AddWithValue("$enabled", model.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$created", model.CreatedUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Registered model saved: {ModelId} ({DisplayName})", model.Id, model.DisplayName);
        return model;
    }

    public async Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, CreatedUtc FROM RegisteredModels WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadModel(reader);
        }

        return null;
    }

    public async Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, CreatedUtc FROM RegisteredModels WHERE ProviderId = $providerId ORDER BY DisplayName";
        command.Parameters.AddWithValue("$providerId", providerId);

        var models = new List<RegisteredModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            models.Add(ReadModel(reader));
        }

        return models;
    }

    public async Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rm.Id, rm.ProviderId, rm.ModelIdentifier, rm.DisplayName, rm.IsEnabled, rm.CreatedUtc, p.Name AS ProviderName
            FROM RegisteredModels rm
            INNER JOIN Providers p ON rm.ProviderId = p.Id
            WHERE rm.IsEnabled = 1 AND p.IsEnabled = 1
            ORDER BY p.Name, rm.DisplayName
            """;

        var models = new List<RegisteredModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var model = ReadModel(reader);
            var providerNameOrdinal = reader.GetOrdinal("ProviderName");
            if (!reader.IsDBNull(providerNameOrdinal))
                model.ProviderName = reader.GetString(providerNameOrdinal);
            models.Add(model);
        }

        return models;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RegisteredModels WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Registered model deleted: {ModelId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> ExistsByProviderAndIdentifierAsync(string providerId, string modelIdentifier, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM RegisteredModels WHERE ProviderId = $providerId AND ModelIdentifier = $identifier";
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$identifier", modelIdentifier);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static RegisteredModel ReadModel(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        ProviderId = reader.GetString(1),
        ModelIdentifier = reader.GetString(2),
        DisplayName = reader.GetString(3),
        IsEnabled = reader.GetInt32(4) == 1,
        CreatedUtc = reader.GetString(5)
    };
}
