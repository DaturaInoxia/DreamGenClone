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
            INSERT INTO Providers (Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, ImageCapability, ImageGenerationPath, ContentPolicy, ImageProtocol, TimeoutSeconds,
                LifecycleStrategyIdentifier, ReadinessPath, ReadinessSuccessContractJson, TransitionTimeoutSeconds, TransitionMarginSeconds, ShutdownDrainPolicyJson,
                MaximumActiveRequests, QueueCapacity, CredentialReference, ServerIdentityPolicyJson, AllowedNetworkBoundary,
                ApiKeyEncrypted, IsEnabled, CreatedUtc, UpdatedUtc, Notes)
            VALUES ($id, $name, $type, $baseUrl, $path, $imageCapability, $imagePath, $contentPolicy, $imageProtocol, $timeout,
                $lifecycleStrategy, $readinessPath, $readinessContract, $transitionTimeout, $transitionMargin, $shutdownDrainPolicy,
                $maximumActiveRequests, $queueCapacity, $credentialReference, $serverIdentityPolicy, $allowedNetworkBoundary,
                $apiKey, $enabled, $created, $updated, $notes)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                ProviderType = $type,
                BaseUrl = $baseUrl,
                ChatCompletionsPath = $path,
                ImageCapability = $imageCapability,
                ImageGenerationPath = $imagePath,
                ContentPolicy = $contentPolicy,
                ImageProtocol = $imageProtocol,
                TimeoutSeconds = $timeout,
                LifecycleStrategyIdentifier = $lifecycleStrategy,
                ReadinessPath = $readinessPath,
                ReadinessSuccessContractJson = $readinessContract,
                TransitionTimeoutSeconds = $transitionTimeout,
                TransitionMarginSeconds = $transitionMargin,
                ShutdownDrainPolicyJson = $shutdownDrainPolicy,
                MaximumActiveRequests = $maximumActiveRequests,
                QueueCapacity = $queueCapacity,
                CredentialReference = $credentialReference,
                ServerIdentityPolicyJson = $serverIdentityPolicy,
                AllowedNetworkBoundary = $allowedNetworkBoundary,
                ApiKeyEncrypted = $apiKey,
                IsEnabled = $enabled,
                UpdatedUtc = $updated,
                Notes = $notes
            """;

        command.Parameters.AddWithValue("$id", provider.Id);
        command.Parameters.AddWithValue("$name", provider.Name);
        command.Parameters.AddWithValue("$type", (int)provider.ProviderType);
        command.Parameters.AddWithValue("$baseUrl", provider.BaseUrl);
        command.Parameters.AddWithValue("$path", provider.ChatCompletionsPath);
        command.Parameters.AddWithValue("$imageCapability", (int)provider.ImageCapability);
        command.Parameters.AddWithValue("$imagePath", provider.ImageGenerationPath);
        command.Parameters.AddWithValue("$contentPolicy", (int)provider.ContentPolicy);
        command.Parameters.AddWithValue("$imageProtocol", (int)provider.ImageProtocol);
        command.Parameters.AddWithValue("$timeout", provider.TimeoutSeconds);
        command.Parameters.AddWithValue("$lifecycleStrategy", (object?)provider.LifecycleStrategyIdentifier ?? DBNull.Value);
        command.Parameters.AddWithValue("$readinessPath", (object?)provider.ReadinessPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$readinessContract", (object?)provider.ReadinessSuccessContractJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$transitionTimeout", (object?)provider.TransitionTimeoutSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$transitionMargin", (object?)provider.TransitionMarginSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$shutdownDrainPolicy", (object?)provider.ShutdownDrainPolicyJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumActiveRequests", (object?)provider.MaximumActiveRequests ?? DBNull.Value);
        command.Parameters.AddWithValue("$queueCapacity", (object?)provider.QueueCapacity ?? DBNull.Value);
        command.Parameters.AddWithValue("$credentialReference", (object?)provider.CredentialReference ?? DBNull.Value);
        command.Parameters.AddWithValue("$serverIdentityPolicy", (object?)provider.ServerIdentityPolicyJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$allowedNetworkBoundary", (object?)provider.AllowedNetworkBoundary ?? DBNull.Value);
        command.Parameters.AddWithValue("$apiKey", (object?)provider.ApiKeyEncrypted ?? DBNull.Value);
        command.Parameters.AddWithValue("$enabled", provider.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$created", provider.CreatedUtc);
        command.Parameters.AddWithValue("$updated", provider.UpdatedUtc);
        command.Parameters.AddWithValue("$notes", (object?)provider.Notes ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Provider saved: {ProviderId} ({ProviderName})", provider.Id, provider.Name);
        return provider;
    }

    public async Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = $"{ProviderSelectColumns} WHERE Id = $id";
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
        command.CommandText = $"{ProviderSelectColumns} ORDER BY Name";

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

    private const string ProviderSelectColumns = """
        SELECT Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, ImageCapability, ImageGenerationPath, ContentPolicy, ImageProtocol, TimeoutSeconds,
               LifecycleStrategyIdentifier, ReadinessPath, ReadinessSuccessContractJson, TransitionTimeoutSeconds, TransitionMarginSeconds, ShutdownDrainPolicyJson,
               MaximumActiveRequests, QueueCapacity, CredentialReference, ServerIdentityPolicyJson, AllowedNetworkBoundary,
               ApiKeyEncrypted, IsEnabled, CreatedUtc, UpdatedUtc, Notes
        FROM Providers
        """;

    private static Provider ReadProvider(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        ProviderType = (ProviderType)reader.GetInt32(2),
        BaseUrl = reader.GetString(3),
        ChatCompletionsPath = reader.GetString(4),
        ImageCapability = (ImageProviderCapability)reader.GetInt32(5),
        ImageGenerationPath = reader.GetString(6),
        ContentPolicy = (ImageContentPolicy)reader.GetInt32(7),
        ImageProtocol = (ImageProtocol)reader.GetInt32(8),
        TimeoutSeconds = reader.GetInt32(9),
        LifecycleStrategyIdentifier = reader.IsDBNull(10) ? null : reader.GetString(10),
        ReadinessPath = reader.IsDBNull(11) ? null : reader.GetString(11),
        ReadinessSuccessContractJson = reader.IsDBNull(12) ? null : reader.GetString(12),
        TransitionTimeoutSeconds = reader.IsDBNull(13) ? null : reader.GetInt32(13),
        TransitionMarginSeconds = reader.IsDBNull(14) ? null : reader.GetInt32(14),
        ShutdownDrainPolicyJson = reader.IsDBNull(15) ? null : reader.GetString(15),
        MaximumActiveRequests = reader.IsDBNull(16) ? null : reader.GetInt32(16),
        QueueCapacity = reader.IsDBNull(17) ? null : reader.GetInt32(17),
        CredentialReference = reader.IsDBNull(18) ? null : reader.GetString(18),
        ServerIdentityPolicyJson = reader.IsDBNull(19) ? null : reader.GetString(19),
        AllowedNetworkBoundary = reader.IsDBNull(20) ? null : reader.GetString(20),
        ApiKeyEncrypted = reader.IsDBNull(21) ? null : reader.GetString(21),
        IsEnabled = reader.GetInt32(22) == 1,
        CreatedUtc = reader.GetString(23),
        UpdatedUtc = reader.GetString(24),
        Notes = reader.IsDBNull(25) ? null : reader.GetString(25)
    };
}
