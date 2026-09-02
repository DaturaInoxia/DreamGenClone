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
        var validationError = functionDefault.ValidateSceneBeatAnalyzerConfiguration();
        if (validationError is not null)
            throw new InvalidOperationException($"RP Scene Beat Analyzer configuration is invalid: {validationError}");

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        functionDefault.UpdatedUtc = DateTime.UtcNow.ToString("o");

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FunctionModelDefaults (
                Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, ThinkingMode, MaxConcurrentJobs,
                DurableJobLeaseSeconds, DurableJobPollIntervalMilliseconds, TransientRetryCount,
                TransientRetryDelaysSecondsJson, DiagnosticsRetentionDays, MaximumCatalogueEntries, UpdatedUtc)
            VALUES (
                $id, $funcName, $modelId, $temp, $topP, $maxTokens, $thinkingMode, $maxConcurrentJobs,
                $durableJobLeaseSeconds, $durableJobPollIntervalMilliseconds, $transientRetryCount,
                $transientRetryDelaysSecondsJson, $diagnosticsRetentionDays, $maximumCatalogueEntries, $updated)
            ON CONFLICT(Id) DO UPDATE SET
                FunctionName = $funcName,
                ModelId = $modelId,
                Temperature = $temp,
                TopP = $topP,
                MaxTokens = $maxTokens,
                ThinkingMode = $thinkingMode,
                MaxConcurrentJobs = $maxConcurrentJobs,
                DurableJobLeaseSeconds = $durableJobLeaseSeconds,
                DurableJobPollIntervalMilliseconds = $durableJobPollIntervalMilliseconds,
                TransientRetryCount = $transientRetryCount,
                TransientRetryDelaysSecondsJson = $transientRetryDelaysSecondsJson,
                DiagnosticsRetentionDays = $diagnosticsRetentionDays,
                MaximumCatalogueEntries = $maximumCatalogueEntries,
                UpdatedUtc = $updated
            """;

        command.Parameters.AddWithValue("$id", functionDefault.Id);
        command.Parameters.AddWithValue("$funcName", functionDefault.FunctionName);
        command.Parameters.AddWithValue("$modelId", functionDefault.ModelId);
        command.Parameters.AddWithValue("$temp", functionDefault.Temperature);
        command.Parameters.AddWithValue("$topP", functionDefault.TopP);
        command.Parameters.AddWithValue("$maxTokens", functionDefault.MaxTokens);
        command.Parameters.AddWithValue("$thinkingMode", (int)functionDefault.ThinkingMode);
        command.Parameters.AddWithValue("$maxConcurrentJobs", (object?)functionDefault.MaxConcurrentJobs ?? DBNull.Value);
        command.Parameters.AddWithValue("$durableJobLeaseSeconds", (object?)functionDefault.DurableJobLeaseSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$durableJobPollIntervalMilliseconds", (object?)functionDefault.DurableJobPollIntervalMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$transientRetryCount", (object?)functionDefault.TransientRetryCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$transientRetryDelaysSecondsJson", (object?)functionDefault.TransientRetryDelaysSecondsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$diagnosticsRetentionDays", (object?)functionDefault.DiagnosticsRetentionDays ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumCatalogueEntries", (object?)functionDefault.MaximumCatalogueEntries ?? DBNull.Value);
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
        command.CommandText = SelectColumns + " WHERE FunctionName = $funcName";
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
        command.CommandText = SelectColumns + " ORDER BY FunctionName";

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
        command.CommandText = SelectColumns + " WHERE ModelId = $modelId ORDER BY FunctionName";
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

    private static FunctionModelDefault ReadFunctionDefault(SqliteDataReader reader)
    {
        var thinkingModeValue = reader.GetInt32(6);
        if (!Enum.IsDefined(typeof(ThinkingMode), thinkingModeValue))
        {
            throw new InvalidDataException($"Function model default contains invalid ThinkingMode value '{thinkingModeValue}'.");
        }

        return new FunctionModelDefault
        {
            Id = reader.GetString(0),
            FunctionName = reader.GetString(1),
            ModelId = reader.GetString(2),
            Temperature = reader.GetDouble(3),
            TopP = reader.GetDouble(4),
            MaxTokens = reader.GetInt32(5),
            ThinkingMode = (ThinkingMode)thinkingModeValue,
            UpdatedUtc = reader.GetString(7),
            MaxConcurrentJobs = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            DurableJobLeaseSeconds = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            DurableJobPollIntervalMilliseconds = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            TransientRetryCount = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            TransientRetryDelaysSecondsJson = reader.IsDBNull(12) ? null : reader.GetString(12),
            DiagnosticsRetentionDays = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            MaximumCatalogueEntries = reader.IsDBNull(14) ? null : reader.GetInt32(14)
        };
    }

    private const string SelectColumns = """
        SELECT Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, ThinkingMode, UpdatedUtc,
               MaxConcurrentJobs, DurableJobLeaseSeconds, DurableJobPollIntervalMilliseconds,
               TransientRetryCount, TransientRetryDelaysSecondsJson, DiagnosticsRetentionDays,
               MaximumCatalogueEntries
        FROM FunctionModelDefaults
        """;
}
