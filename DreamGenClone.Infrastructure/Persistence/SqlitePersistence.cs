using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Domain.StoryParser;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

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

            CREATE TABLE IF NOT EXISTS ParsedStories (
                Id TEXT PRIMARY KEY,
                SourceUrl TEXT NOT NULL,
                SourceDomain TEXT NOT NULL,
                Title TEXT NULL,
                Author TEXT NULL,
                ParsedUtc TEXT NOT NULL,
                PageCount INTEGER NOT NULL,
                CombinedText TEXT NOT NULL,
                StructuredPayloadJson TEXT NOT NULL,
                ParseStatus TEXT NOT NULL,
                DiagnosticsSummaryJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ParsedStories_ParsedUtc ON ParsedStories (ParsedUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_ParsedStories_SourceDomain ON ParsedStories (SourceDomain);
            CREATE INDEX IF NOT EXISTS IX_ParsedStories_Title ON ParsedStories (Title);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        // Migrate: add Author column if missing (for databases created before this column existed)
        var migrateCmd = connection.CreateCommand();
        migrateCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ParsedStories') WHERE name='Author'";
        var hasAuthor = Convert.ToInt64(await migrateCmd.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasAuthor)
        {
            var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE ParsedStories ADD COLUMN Author TEXT NULL";
            await alterCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated ParsedStories table: added Author column");
        }

        var indexCmd = connection.CreateCommand();
        indexCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_ParsedStories_Author ON ParsedStories (Author)";
        await indexCmd.ExecuteNonQueryAsync(cancellationToken);

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

    public async Task SaveParsedStoryAsync(ParsedStoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ParsedStories (
                Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, UpdatedUtc)
            VALUES (
                $id, $sourceUrl, $sourceDomain, $title, $author, $parsedUtc, $pageCount,
                $combinedText, $structuredPayloadJson, $parseStatus, $diagnosticsSummaryJson, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                SourceUrl = $sourceUrl,
                SourceDomain = $sourceDomain,
                Title = $title,
                Author = $author,
                ParsedUtc = $parsedUtc,
                PageCount = $pageCount,
                CombinedText = $combinedText,
                StructuredPayloadJson = $structuredPayloadJson,
                ParseStatus = $parseStatus,
                DiagnosticsSummaryJson = $diagnosticsSummaryJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$sourceUrl", record.SourceUrl);
        command.Parameters.AddWithValue("$sourceDomain", record.SourceDomain);
        command.Parameters.AddWithValue("$title", (object?)record.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", (object?)record.Author ?? DBNull.Value);
        command.Parameters.AddWithValue("$parsedUtc", record.ParsedUtc.ToString("O"));
        command.Parameters.AddWithValue("$pageCount", record.PageCount);
        command.Parameters.AddWithValue("$combinedText", record.CombinedText);
        command.Parameters.AddWithValue("$structuredPayloadJson", record.StructuredPayloadJson);
        command.Parameters.AddWithValue("$parseStatus", record.ParseStatus.ToString());
        command.Parameters.AddWithValue("$diagnosticsSummaryJson", record.DiagnosticsSummaryJson);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story persisted: {ParsedStoryId}, Status={ParseStatus}, Pages={PageCount}", record.Id, record.ParseStatus, record.PageCount);
    }

    public async Task<ParsedStoryRecord?> LoadParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson
            FROM ParsedStories
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadParsedStoryRecord(reader);
    }

    public async Task<List<ParsedStoryRecord>> LoadParsedStoriesAsync(CatalogSortMode sortMode, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var orderClause = sortMode == CatalogSortMode.UrlTitleAsc
            ? "ORDER BY COALESCE(Title, SourceUrl) ASC, ParsedUtc DESC"
            : "ORDER BY ParsedUtc DESC";

        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson
            FROM ParsedStories
            {orderClause}
            LIMIT $limit OFFSET $offset;
            """;

        command.Parameters.AddWithValue("$limit", limit ?? 200);
        command.Parameters.AddWithValue("$offset", offset ?? 0);

        var records = new List<ParsedStoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadParsedStoryRecord(reader));
        }

        _logger.LogInformation("Loaded parsed stories: {Count}, SortMode={SortMode}", records.Count, sortMode);
        return records;
    }

    public async Task<List<ParsedStoryRecord>> SearchParsedStoriesAsync(string query, CatalogSortMode sortMode, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var orderClause = sortMode == CatalogSortMode.UrlTitleAsc
            ? "ORDER BY COALESCE(Title, SourceUrl) ASC, ParsedUtc DESC"
            : "ORDER BY ParsedUtc DESC";

        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson
            FROM ParsedStories
            WHERE SourceUrl LIKE $query
               OR SourceDomain LIKE $query
               OR COALESCE(Title, '') LIKE $query
               OR COALESCE(Author, '') LIKE $query
               OR ParseStatus LIKE $query
            {orderClause};
            """;

        var like = $"%{query ?? string.Empty}%";
        command.Parameters.AddWithValue("$query", like);

        var records = new List<ParsedStoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadParsedStoryRecord(reader));
        }

        _logger.LogInformation("Searched parsed stories: Query='{Query}', Count={Count}, SortMode={SortMode}", query, records.Count, sortMode);
        return records;
    }

    public async Task<bool> DeleteParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ParsedStories WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story deletion attempted: {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static ParsedStoryRecord ReadParsedStoryRecord(SqliteDataReader reader)
    {
        var statusString = reader.GetString(9);
        _ = Enum.TryParse<ParseStatus>(statusString, ignoreCase: true, out var status);

        return new ParsedStoryRecord
        {
            Id = reader.GetString(0),
            SourceUrl = reader.GetString(1),
            SourceDomain = reader.GetString(2),
            Title = reader.IsDBNull(3) ? null : reader.GetString(3),
            Author = reader.IsDBNull(4) ? null : reader.GetString(4),
            ParsedUtc = DateTime.TryParse(reader.GetString(5), out var parsedUtc) ? parsedUtc : DateTime.UtcNow,
            PageCount = reader.GetInt32(6),
            CombinedText = reader.GetString(7),
            StructuredPayloadJson = reader.GetString(8),
            ParseStatus = status,
            DiagnosticsSummaryJson = reader.GetString(10)
        };
    }
}
