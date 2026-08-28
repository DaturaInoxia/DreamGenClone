using DreamGenClone.Application.PromptTester;
using DreamGenClone.Domain.PromptTester;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.PromptTester;

public sealed class PromptTestRunRepository : IPromptTestRunRepository
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<PromptTestRunRepository> _logger;

    public PromptTestRunRepository(IOptions<PersistenceOptions> options, ILogger<PromptTestRunRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SaveAsync(PromptTestRun run, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO PromptTestRuns
            (Id, Comment, ModelIdentifier, ModelDisplayName, ProviderName, SystemMessage, UserPrompt,
             Temperature, TopP, MaxTokens, ResultText, ResultError,
             PromptCharCount, ResultWordCount, ResultCharCount, ElapsedSeconds, CreatedUtc)
            VALUES
            ($id, $comment, $modelIdentifier, $modelDisplayName, $providerName, $systemMessage, $userPrompt,
             $temperature, $topP, $maxTokens, $resultText, $resultError,
             $promptCharCount, $resultWordCount, $resultCharCount, $elapsedSeconds, $createdUtc)
            """;

        command.Parameters.AddWithValue("$id", run.Id);
        command.Parameters.AddWithValue("$comment", (object?)run.Comment ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelIdentifier", run.ModelIdentifier);
        command.Parameters.AddWithValue("$modelDisplayName", run.ModelDisplayName);
        command.Parameters.AddWithValue("$providerName", run.ProviderName);
        command.Parameters.AddWithValue("$systemMessage", (object?)run.SystemMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$userPrompt", run.UserPrompt);
        command.Parameters.AddWithValue("$temperature", run.Temperature);
        command.Parameters.AddWithValue("$topP", run.TopP);
        command.Parameters.AddWithValue("$maxTokens", run.MaxTokens);
        command.Parameters.AddWithValue("$resultText", (object?)run.ResultText ?? DBNull.Value);
        command.Parameters.AddWithValue("$resultError", (object?)run.ResultError ?? DBNull.Value);
        command.Parameters.AddWithValue("$promptCharCount", run.PromptCharCount);
        command.Parameters.AddWithValue("$resultWordCount", run.ResultWordCount);
        command.Parameters.AddWithValue("$resultCharCount", run.ResultCharCount);
        command.Parameters.AddWithValue("$elapsedSeconds", run.ElapsedSeconds);
        command.Parameters.AddWithValue("$createdUtc", run.CreatedUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Saved prompt test run {RunId}", run.Id);
    }

    public async Task<List<PromptTestRun>> GetAllAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Comment, ModelIdentifier, ModelDisplayName, ProviderName, SystemMessage, UserPrompt,
                   Temperature, TopP, MaxTokens, ResultText, ResultError,
                   PromptCharCount, ResultWordCount, ResultCharCount, ElapsedSeconds, CreatedUtc
            FROM PromptTestRuns
            ORDER BY CreatedUtc DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<PromptTestRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapRow(reader));
        }

        return results;
    }

    public async Task<PromptTestRun?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Comment, ModelIdentifier, ModelDisplayName, ProviderName, SystemMessage, UserPrompt,
                   Temperature, TopP, MaxTokens, ResultText, ResultError,
                   PromptCharCount, ResultWordCount, ResultCharCount, ElapsedSeconds, CreatedUtc
            FROM PromptTestRuns
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapRow(reader);
        }

        return null;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PromptTestRuns WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Deleted prompt test run {RunId}, rows affected: {Affected}", id, affected);
        return affected > 0;
    }

    private static PromptTestRun MapRow(SqliteDataReader reader)
    {
        return new PromptTestRun
        {
            Id = reader.GetString(0),
            Comment = reader.IsDBNull(1) ? null : reader.GetString(1),
            ModelIdentifier = reader.GetString(2),
            ModelDisplayName = reader.GetString(3),
            ProviderName = reader.GetString(4),
            SystemMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
            UserPrompt = reader.GetString(6),
            Temperature = reader.GetDouble(7),
            TopP = reader.GetDouble(8),
            MaxTokens = reader.GetInt32(9),
            ResultText = reader.IsDBNull(10) ? null : reader.GetString(10),
            ResultError = reader.IsDBNull(11) ? null : reader.GetString(11),
            PromptCharCount = reader.GetInt32(12),
            ResultWordCount = reader.GetInt32(13),
            ResultCharCount = reader.GetInt32(14),
            ElapsedSeconds = reader.GetDouble(15),
            CreatedUtc = reader.GetString(16)
        };
    }
}
