using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.ModelManager;

public sealed class ProviderRepository : IProviderRepository
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<ProviderRepository> _logger;

    public ProviderRepository(IOptions<PersistenceOptions> options, ILogger<ProviderRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        provider.UpdatedUtc = DateTime.UtcNow.ToString("o");

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Providers (Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, TimeoutSeconds, ApiKeyEncrypted, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $type, $baseUrl, $path, $timeout, $apiKey, $enabled, $created, $updated)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                ProviderType = $type,
                BaseUrl = $baseUrl,
                ChatCompletionsPath = $path,
                TimeoutSeconds = $timeout,
                ApiKeyEncrypted = $apiKey,
                IsEnabled = $enabled,
                UpdatedUtc = $updated
            """;

        command.Parameters.AddWithValue("$id", provider.Id);
        command.Parameters.AddWithValue("$name", provider.Name);
        command.Parameters.AddWithValue("$type", (int)provider.ProviderType);
        command.Parameters.AddWithValue("$baseUrl", provider.BaseUrl);
        command.Parameters.AddWithValue("$path", provider.ChatCompletionsPath);
        command.Parameters.AddWithValue("$timeout", provider.TimeoutSeconds);
        command.Parameters.AddWithValue("$apiKey", (object?)provider.ApiKeyEncrypted ?? DBNull.Value);
        command.Parameters.AddWithValue("$enabled", provider.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$created", provider.CreatedUtc);
        command.Parameters.AddWithValue("$updated", provider.UpdatedUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Provider saved: {ProviderId} ({ProviderName})", provider.Id, provider.Name);
        return provider;
    }

    public async Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, TimeoutSeconds, ApiKeyEncrypted, IsEnabled, CreatedUtc, UpdatedUtc FROM Providers WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadProvider(reader);
        }

        return null;
    }

    public async Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, TimeoutSeconds, ApiKeyEncrypted, IsEnabled, CreatedUtc, UpdatedUtc FROM Providers ORDER BY Name";

        var providers = new List<Provider>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            providers.Add(ReadProvider(reader));
        }

        return providers;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var fkCmd = connection.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_keys = ON";
        await fkCmd.ExecuteNonQueryAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Providers WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Provider deleted: {ProviderId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Providers WHERE Name = $name";
        command.Parameters.AddWithValue("$name", name);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static Provider ReadProvider(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        ProviderType = (ProviderType)reader.GetInt32(2),
        BaseUrl = reader.GetString(3),
        ChatCompletionsPath = reader.GetString(4),
        TimeoutSeconds = reader.GetInt32(5),
        ApiKeyEncrypted = reader.IsDBNull(6) ? null : reader.GetString(6),
        IsEnabled = reader.GetInt32(7) == 1,
        CreatedUtc = reader.GetString(8),
        UpdatedUtc = reader.GetString(9)
    };
}
