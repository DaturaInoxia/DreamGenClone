using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.ModelManager;

public sealed class FunctionDefaultRepository : IFunctionDefaultRepository
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<FunctionDefaultRepository> _logger;

    public FunctionDefaultRepository(IOptions<PersistenceOptions> options, ILogger<FunctionDefaultRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FunctionModelDefault> SaveAsync(FunctionModelDefault functionDefault, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        functionDefault.UpdatedUtc = DateTime.UtcNow.ToString("o");

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FunctionModelDefaults (Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, UpdatedUtc)
            VALUES ($id, $funcName, $modelId, $temp, $topP, $maxTokens, $updated)
            ON CONFLICT(Id) DO UPDATE SET
                FunctionName = $funcName,
                ModelId = $modelId,
                Temperature = $temp,
                TopP = $topP,
                MaxTokens = $maxTokens,
                UpdatedUtc = $updated
            """;

        command.Parameters.AddWithValue("$id", functionDefault.Id);
        command.Parameters.AddWithValue("$funcName", functionDefault.FunctionName);
        command.Parameters.AddWithValue("$modelId", functionDefault.ModelId);
        command.Parameters.AddWithValue("$temp", functionDefault.Temperature);
        command.Parameters.AddWithValue("$topP", functionDefault.TopP);
        command.Parameters.AddWithValue("$maxTokens", functionDefault.MaxTokens);
        command.Parameters.AddWithValue("$updated", functionDefault.UpdatedUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Function default saved: {FunctionName} → ModelId={ModelId}", functionDefault.FunctionName, functionDefault.ModelId);
        return functionDefault;
    }

    public async Task<FunctionModelDefault?> GetByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, UpdatedUtc FROM FunctionModelDefaults WHERE FunctionName = $funcName";
        command.Parameters.AddWithValue("$funcName", function.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadFunctionDefault(reader);
        }

        return null;
    }

    public async Task<List<FunctionModelDefault>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, UpdatedUtc FROM FunctionModelDefaults ORDER BY FunctionName";

        var defaults = new List<FunctionModelDefault>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            defaults.Add(ReadFunctionDefault(reader));
        }

        return defaults;
    }

    public async Task<List<FunctionModelDefault>> GetByModelIdAsync(string modelId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, UpdatedUtc FROM FunctionModelDefaults WHERE ModelId = $modelId ORDER BY FunctionName";
        command.Parameters.AddWithValue("$modelId", modelId);

        var defaults = new List<FunctionModelDefault>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            defaults.Add(ReadFunctionDefault(reader));
        }

        return defaults;
    }

    public async Task<bool> DeleteByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FunctionModelDefaults WHERE FunctionName = $funcName";
        command.Parameters.AddWithValue("$funcName", function.ToString());

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Function default deleted: {FunctionName}, RowsAffected={RowsAffected}", function, rowsAffected);
        return rowsAffected > 0;
    }

    private static FunctionModelDefault ReadFunctionDefault(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        FunctionName = reader.GetString(1),
        ModelId = reader.GetString(2),
        Temperature = reader.GetDouble(3),
        TopP = reader.GetDouble(4),
        MaxTokens = reader.GetInt32(5),
        UpdatedUtc = reader.GetString(6)
    };
}
