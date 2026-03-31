using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Domain.StoryAnalysis;
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
                IsArchived INTEGER NOT NULL DEFAULT 0,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ParsedStories_ParsedUtc ON ParsedStories (ParsedUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_ParsedStories_SourceDomain ON ParsedStories (SourceDomain);
            CREATE INDEX IF NOT EXISTS IX_ParsedStories_Title ON ParsedStories (Title);
            CREATE INDEX IF NOT EXISTS IX_ParsedStories_SourceUrl ON ParsedStories (SourceUrl);

            CREATE TABLE IF NOT EXISTS StorySummaries (
                Id TEXT PRIMARY KEY,
                ParsedStoryId TEXT NOT NULL UNIQUE,
                SummaryText TEXT NOT NULL,
                GeneratedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_StorySummaries_ParsedStoryId ON StorySummaries (ParsedStoryId);

            CREATE TABLE IF NOT EXISTS StoryAnalyses (
                Id TEXT PRIMARY KEY,
                ParsedStoryId TEXT NOT NULL UNIQUE,
                CharactersJson TEXT NULL,
                ThemesJson TEXT NULL,
                PlotStructureJson TEXT NULL,
                WritingStyleJson TEXT NULL,
                GeneratedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_StoryAnalyses_ParsedStoryId ON StoryAnalyses (ParsedStoryId);

            CREATE TABLE IF NOT EXISTS ThemePreferences (
                Id TEXT PRIMARY KEY,
                ProfileId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Tier TEXT NOT NULL DEFAULT 'Neutral',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RankingProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS StoryRankings (
                Id TEXT PRIMARY KEY,
                ParsedStoryId TEXT NOT NULL,
                ProfileId TEXT NOT NULL DEFAULT '',
                ThemeSnapshotJson TEXT NOT NULL,
                ThemeDetectionsJson TEXT NOT NULL,
                Score REAL NOT NULL,
                IsDisqualified INTEGER NOT NULL DEFAULT 0,
                GeneratedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE,
                UNIQUE(ParsedStoryId, ProfileId)
            );

            CREATE TABLE IF NOT EXISTS StoryCollections (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_StoryCollections_Name ON StoryCollections (Name);

            CREATE TABLE IF NOT EXISTS StoryCollectionMembers (
                Id TEXT PRIMARY KEY,
                CollectionId TEXT NOT NULL,
                ParsedStoryId TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                AddedUtc TEXT NOT NULL,
                FOREIGN KEY (CollectionId) REFERENCES StoryCollections(Id) ON DELETE CASCADE,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE,
                UNIQUE(CollectionId, ParsedStoryId)
            );

            CREATE INDEX IF NOT EXISTS IX_StoryCollectionMembers_CollectionId ON StoryCollectionMembers (CollectionId);
            CREATE INDEX IF NOT EXISTS IX_StoryCollectionMembers_ParsedStoryId ON StoryCollectionMembers (ParsedStoryId);

            CREATE TABLE IF NOT EXISTS UserStoryRatings (
                Id TEXT PRIMARY KEY,
                ParsedStoryId TEXT NOT NULL UNIQUE,
                Stars INTEGER NOT NULL CHECK(Stars >= 1 AND Stars <= 5),
                Comment TEXT NOT NULL DEFAULT '',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_UserStoryRatings_ParsedStoryId ON UserStoryRatings (ParsedStoryId);

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

        // Migrate: add IsArchived column to ParsedStories if missing
        var checkIsArchived = connection.CreateCommand();
        checkIsArchived.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ParsedStories') WHERE name='IsArchived'";
        var hasIsArchived = Convert.ToInt64(await checkIsArchived.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasIsArchived)
        {
            var alterArchived = connection.CreateCommand();
            alterArchived.CommandText = "ALTER TABLE ParsedStories ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0";
            await alterArchived.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated ParsedStories table: added IsArchived column");
        }

        // Migrate: add ProfileId column to RankingCriteria if missing, then migrate to ThemePreferences
        var checkOldCriteria = connection.CreateCommand();
        checkOldCriteria.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RankingCriteria'";
        var hasOldCriteria = Convert.ToInt64(await checkOldCriteria.ExecuteScalarAsync(cancellationToken)) > 0;
        if (hasOldCriteria)
        {
            // Drop old table — data is incompatible with new ThemePreferences schema
            var dropOld = connection.CreateCommand();
            dropOld.CommandText = "DROP TABLE IF EXISTS RankingCriteria";
            await dropOld.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated: dropped old RankingCriteria table (replaced by ThemePreferences)");
        }

        // Migrate StoryRankings: if table is missing new 'Score' column, rebuild with new schema
        var checkOldRankings = connection.CreateCommand();
        checkOldRankings.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StoryRankings') WHERE name='Score'";
        var hasNewScoreColumn = Convert.ToInt64(await checkOldRankings.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasNewScoreColumn)
        {
            var rebuildCmd = connection.CreateCommand();
            rebuildCmd.CommandText = """
                DROP TABLE StoryRankings;
                CREATE TABLE StoryRankings (
                    Id TEXT PRIMARY KEY,
                    ParsedStoryId TEXT NOT NULL,
                    ProfileId TEXT NOT NULL DEFAULT '',
                    ThemeSnapshotJson TEXT NOT NULL,
                    ThemeDetectionsJson TEXT NOT NULL,
                    Score REAL NOT NULL,
                    IsDisqualified INTEGER NOT NULL DEFAULT 0,
                    GeneratedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE,
                    UNIQUE(ParsedStoryId, ProfileId)
                );
                CREATE INDEX IF NOT EXISTS IX_StoryRankings_ParsedStoryId ON StoryRankings (ParsedStoryId);
                CREATE INDEX IF NOT EXISTS IX_StoryRankings_Score ON StoryRankings (Score DESC);
                """;
            await rebuildCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated StoryRankings table to theme-based schema (old ranking data cleared)");
        }

        // Ensure StoryRankings indexes exist (after any migration)
        var rankingIndexCmd = connection.CreateCommand();
        rankingIndexCmd.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_StoryRankings_ParsedStoryId ON StoryRankings (ParsedStoryId);
            CREATE INDEX IF NOT EXISTS IX_StoryRankings_Score ON StoryRankings (Score DESC);
            """;
        await rankingIndexCmd.ExecuteNonQueryAsync(cancellationToken);

        // Migrate: add IsDefault column to RankingProfiles if missing
        var migrateIsDefault = connection.CreateCommand();
        migrateIsDefault.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RankingProfiles') WHERE name='IsDefault'";
        var hasIsDefault = Convert.ToInt64(await migrateIsDefault.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasIsDefault)
        {
            var alterIsDefault = connection.CreateCommand();
            alterIsDefault.CommandText = "ALTER TABLE RankingProfiles ADD COLUMN IsDefault INTEGER NOT NULL DEFAULT 0";
            await alterIsDefault.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RankingProfiles table: added IsDefault column");
        }

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
                CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived, UpdatedUtc)
            VALUES (
                $id, $sourceUrl, $sourceDomain, $title, $author, $parsedUtc, $pageCount,
                $combinedText, $structuredPayloadJson, $parseStatus, $diagnosticsSummaryJson, $isArchived, $updatedUtc)
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
                IsArchived = $isArchived,
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
        command.Parameters.AddWithValue("$isArchived", record.IsArchived ? 1 : 0);
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
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived
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
        return await LoadParsedStoriesAsync(sortMode, includeArchived: false, limit, offset, cancellationToken);
    }

    public async Task<List<ParsedStoryRecord>> LoadParsedStoriesAsync(CatalogSortMode sortMode, bool includeArchived, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var orderClause = sortMode == CatalogSortMode.UrlTitleAsc
            ? "ORDER BY COALESCE(Title, SourceUrl) ASC, ParsedUtc DESC"
            : "ORDER BY ParsedUtc DESC";

        var whereClause = includeArchived ? "" : "WHERE IsArchived = 0";

        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived
            FROM ParsedStories
            {whereClause}
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
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived
            FROM ParsedStories
            WHERE IsArchived = 0
              AND (SourceUrl LIKE $query
               OR SourceDomain LIKE $query
               OR COALESCE(Title, '') LIKE $query
               OR COALESCE(Author, '') LIKE $query
               OR ParseStatus LIKE $query)
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

        // Enable foreign keys so ON DELETE CASCADE fires for child tables
        var fkCmd = connection.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_keys = ON";
        await fkCmd.ExecuteNonQueryAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ParsedStories WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story hard-deleted: {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> ArchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE ParsedStories SET IsArchived = 1, UpdatedUtc = $updatedUtc WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story archived: {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> UnarchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE ParsedStories SET IsArchived = 0, UpdatedUtc = $updatedUtc WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story unarchived: {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> PurgeParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable FK cascade to clean up child rows
        var fkCmd = connection.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_keys = ON";
        await fkCmd.ExecuteNonQueryAsync(cancellationToken);

        // Delete child data (summaries, analyses, rankings) via cascade
        // Then clear the story content but keep Id, SourceUrl, Title, Author
        var deleteChildren = connection.CreateCommand();
        deleteChildren.CommandText = """
            DELETE FROM StorySummaries WHERE ParsedStoryId = $id;
            DELETE FROM StoryAnalyses WHERE ParsedStoryId = $id;
            DELETE FROM StoryRankings WHERE ParsedStoryId = $id;
            """;
        deleteChildren.Parameters.AddWithValue("$id", id);
        await deleteChildren.ExecuteNonQueryAsync(cancellationToken);

        var purgeCmd = connection.CreateCommand();
        purgeCmd.CommandText = """
            UPDATE ParsedStories SET
                CombinedText = '',
                StructuredPayloadJson = '[]',
                DiagnosticsSummaryJson = '{}',
                PageCount = 0,
                ParseStatus = 'Purged',
                IsArchived = 1,
                UpdatedUtc = $updatedUtc
            WHERE Id = $id
            """;
        purgeCmd.Parameters.AddWithValue("$id", id);
        purgeCmd.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        var rowsAffected = await purgeCmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story purged (partial delete): {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<List<ParsedStoryRecord>> FindBySourceUrlAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived
            FROM ParsedStories
            WHERE SourceUrl = $sourceUrl
            ORDER BY ParsedUtc DESC;
            """;
        command.Parameters.AddWithValue("$sourceUrl", sourceUrl);

        var records = new List<ParsedStoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadParsedStoryRecord(reader));
        }

        return records;
    }

    // --- Story Summary persistence ---

    public async Task SaveStorySummaryAsync(StorySummary summary, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StorySummaries (Id, ParsedStoryId, SummaryText, GeneratedUtc, UpdatedUtc)
            VALUES ($id, $parsedStoryId, $summaryText, $generatedUtc, $updatedUtc)
            ON CONFLICT(ParsedStoryId) DO UPDATE SET
                SummaryText = $summaryText,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", summary.Id);
        command.Parameters.AddWithValue("$parsedStoryId", summary.ParsedStoryId);
        command.Parameters.AddWithValue("$summaryText", summary.SummaryText);
        command.Parameters.AddWithValue("$generatedUtc", summary.GeneratedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Story summary persisted: {ParsedStoryId}", summary.ParsedStoryId);
    }

    public async Task<StorySummary?> LoadStorySummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, SummaryText, GeneratedUtc, UpdatedUtc FROM StorySummaries WHERE ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new StorySummary
        {
            Id = reader.GetString(0),
            ParsedStoryId = reader.GetString(1),
            SummaryText = reader.GetString(2),
            GeneratedUtc = DateTime.TryParse(reader.GetString(3), out var gen) ? gen : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Story Analysis persistence ---

    public async Task SaveStoryAnalysisAsync(StoryAnalysisResult analysis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StoryAnalyses (Id, ParsedStoryId, CharactersJson, ThemesJson, PlotStructureJson, WritingStyleJson, GeneratedUtc, UpdatedUtc)
            VALUES ($id, $parsedStoryId, $charactersJson, $themesJson, $plotStructureJson, $writingStyleJson, $generatedUtc, $updatedUtc)
            ON CONFLICT(ParsedStoryId) DO UPDATE SET
                CharactersJson = $charactersJson,
                ThemesJson = $themesJson,
                PlotStructureJson = $plotStructureJson,
                WritingStyleJson = $writingStyleJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", analysis.Id);
        command.Parameters.AddWithValue("$parsedStoryId", analysis.ParsedStoryId);
        command.Parameters.AddWithValue("$charactersJson", (object?)analysis.CharactersJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$themesJson", (object?)analysis.ThemesJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$plotStructureJson", (object?)analysis.PlotStructureJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$writingStyleJson", (object?)analysis.WritingStyleJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$generatedUtc", analysis.GeneratedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Story analysis persisted: {ParsedStoryId}", analysis.ParsedStoryId);
    }

    public async Task<StoryAnalysisResult?> LoadStoryAnalysisAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, CharactersJson, ThemesJson, PlotStructureJson, WritingStyleJson, GeneratedUtc, UpdatedUtc FROM StoryAnalyses WHERE ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new StoryAnalysisResult
        {
            Id = reader.GetString(0),
            ParsedStoryId = reader.GetString(1),
            CharactersJson = reader.IsDBNull(2) ? null : reader.GetString(2),
            ThemesJson = reader.IsDBNull(3) ? null : reader.GetString(3),
            PlotStructureJson = reader.IsDBNull(4) ? null : reader.GetString(4),
            WritingStyleJson = reader.IsDBNull(5) ? null : reader.GetString(5),
            GeneratedUtc = DateTime.TryParse(reader.GetString(6), out var gen) ? gen : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(7), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Ranking Criteria persistence ---

    public async Task SaveThemePreferenceAsync(ThemePreference preference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preference);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ThemePreferences (Id, ProfileId, Name, Description, Tier, CreatedUtc, UpdatedUtc)
            VALUES ($id, $profileId, $name, $description, $tier, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                ProfileId = $profileId,
                Name = $name,
                Description = $description,
                Tier = $tier,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", preference.Id);
        command.Parameters.AddWithValue("$profileId", preference.ProfileId);
        command.Parameters.AddWithValue("$name", preference.Name);
        command.Parameters.AddWithValue("$description", preference.Description);
        command.Parameters.AddWithValue("$tier", preference.Tier.ToString());
        command.Parameters.AddWithValue("$createdUtc", preference.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Theme preference persisted: {Id}, Name={Name}", preference.Id, preference.Name);
    }

    public async Task<ThemePreference?> LoadThemePreferenceAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProfileId, Name, Description, Tier, CreatedUtc, UpdatedUtc FROM ThemePreferences WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadThemePreference(reader);
    }

    public async Task<List<ThemePreference>> LoadAllThemePreferencesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProfileId, Name, Description, Tier, CreatedUtc, UpdatedUtc FROM ThemePreferences ORDER BY UpdatedUtc DESC";

        var results = new List<ThemePreference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadThemePreference(reader));
        }

        _logger.LogInformation("Loaded {Count} theme preferences from database", results.Count);
        return results;
    }

    public async Task<List<ThemePreference>> LoadThemePreferencesByProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProfileId, Name, Description, Tier, CreatedUtc, UpdatedUtc FROM ThemePreferences WHERE ProfileId = $profileId ORDER BY UpdatedUtc DESC";
        command.Parameters.AddWithValue("$profileId", profileId);

        var results = new List<ThemePreference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadThemePreference(reader));
        }

        return results;
    }

    public async Task<bool> DeleteThemePreferenceAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ThemePreferences WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Theme preference deletion attempted: {Id}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static ThemePreference ReadThemePreference(SqliteDataReader reader)
    {
        return new ThemePreference
        {
            Id = reader.GetString(0),
            ProfileId = reader.GetString(1),
            Name = reader.GetString(2),
            Description = reader.GetString(3),
            Tier = Enum.TryParse<ThemeTier>(reader.GetString(4), out var tier) ? tier : ThemeTier.Neutral,
            CreatedUtc = DateTime.TryParse(reader.GetString(5), out var cre) ? cre : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(6), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Ranking Profile persistence ---

    public async Task SaveRankingProfileAsync(RankingProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RankingProfiles (Id, Name, IsDefault, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $isDefault, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                IsDefault = $isDefault,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Ranking profile persisted: {ProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<RankingProfile?> LoadRankingProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IsDefault, CreatedUtc, UpdatedUtc FROM RankingProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new RankingProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            IsDefault = reader.GetInt32(2) != 0,
            CreatedUtc = DateTime.TryParse(reader.GetString(3), out var cre) ? cre : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var upd) ? upd : DateTime.UtcNow
        };
    }

    public async Task<List<RankingProfile>> LoadAllRankingProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IsDefault, CreatedUtc, UpdatedUtc FROM RankingProfiles ORDER BY Name";

        var results = new List<RankingProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RankingProfile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                IsDefault = reader.GetInt32(2) != 0,
                CreatedUtc = DateTime.TryParse(reader.GetString(3), out var cre) ? cre : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var upd) ? upd : DateTime.UtcNow
            });
        }

        return results;
    }

    public async Task<bool> DeleteRankingProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Also delete criteria belonging to this profile
        var deleteCriteria = connection.CreateCommand();
        deleteCriteria.CommandText = "DELETE FROM RankingCriteria WHERE ProfileId = $profileId";
        deleteCriteria.Parameters.AddWithValue("$profileId", id);
        await deleteCriteria.ExecuteNonQueryAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RankingProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Ranking profile deletion attempted: {ProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task SetDefaultRankingProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Clear all defaults, then set the requested one
        var clearCmd = connection.CreateCommand();
        clearCmd.CommandText = "UPDATE RankingProfiles SET IsDefault = 0 WHERE IsDefault = 1";
        await clearCmd.ExecuteNonQueryAsync(cancellationToken);

        var setCmd = connection.CreateCommand();
        setCmd.CommandText = "UPDATE RankingProfiles SET IsDefault = 1, UpdatedUtc = $updatedUtc WHERE Id = $id";
        setCmd.Parameters.AddWithValue("$id", id);
        setCmd.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        await setCmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Set default ranking profile: {ProfileId}", id);
    }

    public async Task<RankingProfile?> LoadDefaultRankingProfileAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IsDefault, CreatedUtc, UpdatedUtc FROM RankingProfiles WHERE IsDefault = 1 LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new RankingProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            IsDefault = reader.GetInt32(2) != 0,
            CreatedUtc = DateTime.TryParse(reader.GetString(3), out var cre) ? cre : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Story Ranking persistence ---

    public async Task SaveStoryRankingAsync(StoryRankingResult ranking, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ranking);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StoryRankings (Id, ParsedStoryId, ProfileId, ThemeSnapshotJson, ThemeDetectionsJson, Score, IsDisqualified, GeneratedUtc, UpdatedUtc)
            VALUES ($id, $parsedStoryId, $profileId, $themeSnapshotJson, $themeDetectionsJson, $score, $isDisqualified, $generatedUtc, $updatedUtc)
            ON CONFLICT(ParsedStoryId, ProfileId) DO UPDATE SET
                ThemeSnapshotJson = $themeSnapshotJson,
                ThemeDetectionsJson = $themeDetectionsJson,
                Score = $score,
                IsDisqualified = $isDisqualified,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", ranking.Id);
        command.Parameters.AddWithValue("$parsedStoryId", ranking.ParsedStoryId);
        command.Parameters.AddWithValue("$profileId", ranking.ProfileId);
        command.Parameters.AddWithValue("$themeSnapshotJson", ranking.ThemeSnapshotJson);
        command.Parameters.AddWithValue("$themeDetectionsJson", ranking.ThemeDetectionsJson);
        command.Parameters.AddWithValue("$score", ranking.Score);
        command.Parameters.AddWithValue("$isDisqualified", ranking.IsDisqualified ? 1 : 0);
        command.Parameters.AddWithValue("$generatedUtc", ranking.GeneratedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Story ranking persisted: {ParsedStoryId}, ProfileId={ProfileId}, Score={Score}, IsDisqualified={IsDisqualified}", ranking.ParsedStoryId, ranking.ProfileId, ranking.Score, ranking.IsDisqualified);
    }

    public async Task<StoryRankingResult?> LoadStoryRankingAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, ProfileId, ThemeSnapshotJson, ThemeDetectionsJson, Score, IsDisqualified, GeneratedUtc, UpdatedUtc FROM StoryRankings WHERE ParsedStoryId = $parsedStoryId LIMIT 1";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadStoryRanking(reader);
    }

    public async Task<StoryRankingResult?> LoadStoryRankingByProfileAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, ProfileId, ThemeSnapshotJson, ThemeDetectionsJson, Score, IsDisqualified, GeneratedUtc, UpdatedUtc FROM StoryRankings WHERE ParsedStoryId = $parsedStoryId AND ProfileId = $profileId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);
        command.Parameters.AddWithValue("$profileId", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadStoryRanking(reader);
    }

    public async Task<List<StoryRankingResult>> LoadStoryRankingsAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, ProfileId, ThemeSnapshotJson, ThemeDetectionsJson, Score, IsDisqualified, GeneratedUtc, UpdatedUtc FROM StoryRankings WHERE ParsedStoryId = $parsedStoryId ORDER BY GeneratedUtc DESC";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        var results = new List<StoryRankingResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryRanking(reader));
        }
        return results;
    }

    private static StoryRankingResult ReadStoryRanking(SqliteDataReader reader)
    {
        return new StoryRankingResult
        {
            Id = reader.GetString(0),
            ParsedStoryId = reader.GetString(1),
            ProfileId = reader.GetString(2),
            ThemeSnapshotJson = reader.GetString(3),
            ThemeDetectionsJson = reader.GetString(4),
            Score = reader.GetDouble(5),
            IsDisqualified = reader.GetInt32(6) != 0,
            GeneratedUtc = DateTime.TryParse(reader.GetString(7), out var gen) ? gen : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var upd) ? upd : DateTime.UtcNow
        };
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
            DiagnosticsSummaryJson = reader.GetString(10),
            IsArchived = reader.FieldCount > 11 && !reader.IsDBNull(11) && reader.GetInt32(11) != 0
        };
    }

    // --- Story Collection operations ---

    public async Task SaveStoryCollectionAsync(StoryCollection collection, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StoryCollections (Id, Name, Description, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", collection.Id);
        command.Parameters.AddWithValue("$name", collection.Name);
        command.Parameters.AddWithValue("$description", (object?)collection.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", collection.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoryCollection?> LoadStoryCollectionAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, CreatedUtc, UpdatedUtc FROM StoryCollections WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadStoryCollection(reader);
        }
        return null;
    }

    public async Task<List<StoryCollection>> LoadAllStoryCollectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, CreatedUtc, UpdatedUtc FROM StoryCollections ORDER BY Name ASC";

        var results = new List<StoryCollection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryCollection(reader));
        }
        return results;
    }

    public async Task<bool> DeleteStoryCollectionAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable foreign keys for cascade
        var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON";
        await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StoryCollections WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<List<StoryCollection>> SearchStoryCollectionsAsync(string query, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Description, CreatedUtc, UpdatedUtc FROM StoryCollections
            WHERE Name LIKE $query OR Description LIKE $query
            ORDER BY Name ASC
            """;
        command.Parameters.AddWithValue("$query", $"%{query}%");

        var results = new List<StoryCollection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryCollection(reader));
        }
        return results;
    }

    // --- Story Collection Membership operations ---

    public async Task SaveStoryCollectionMemberAsync(StoryCollectionMembership membership, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StoryCollectionMembers (Id, CollectionId, ParsedStoryId, SortOrder, AddedUtc)
            VALUES ($id, $collectionId, $parsedStoryId, $sortOrder, $addedUtc)
            ON CONFLICT(CollectionId, ParsedStoryId) DO UPDATE SET
                SortOrder = $sortOrder;
            """;

        command.Parameters.AddWithValue("$id", membership.Id);
        command.Parameters.AddWithValue("$collectionId", membership.CollectionId);
        command.Parameters.AddWithValue("$parsedStoryId", membership.ParsedStoryId);
        command.Parameters.AddWithValue("$sortOrder", membership.SortOrder);
        command.Parameters.AddWithValue("$addedUtc", membership.AddedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<StoryCollectionMembership>> LoadCollectionMembersAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CollectionId, ParsedStoryId, SortOrder, AddedUtc
            FROM StoryCollectionMembers
            WHERE CollectionId = $collectionId
            ORDER BY SortOrder ASC
            """;
        command.Parameters.AddWithValue("$collectionId", collectionId);

        var results = new List<StoryCollectionMembership>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryCollectionMembership(reader));
        }
        return results;
    }

    public async Task<List<StoryCollection>> LoadCollectionsForStoryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.Name, c.Description, c.CreatedUtc, c.UpdatedUtc
            FROM StoryCollections c
            INNER JOIN StoryCollectionMembers m ON c.Id = m.CollectionId
            WHERE m.ParsedStoryId = $parsedStoryId
            ORDER BY c.Name ASC
            """;
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        var results = new List<StoryCollection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryCollection(reader));
        }
        return results;
    }

    public async Task<bool> DeleteStoryCollectionMemberAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StoryCollectionMembers WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteStoryCollectionMemberByStoryAsync(string collectionId, string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StoryCollectionMembers WHERE CollectionId = $collectionId AND ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$collectionId", collectionId);
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static StoryCollection ReadStoryCollection(SqliteDataReader reader)
    {
        return new StoryCollection
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedUtc = DateTime.TryParse(reader.GetString(3), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var updated) ? updated : DateTime.UtcNow
        };
    }

    private static StoryCollectionMembership ReadStoryCollectionMembership(SqliteDataReader reader)
    {
        return new StoryCollectionMembership
        {
            Id = reader.GetString(0),
            CollectionId = reader.GetString(1),
            ParsedStoryId = reader.GetString(2),
            SortOrder = reader.GetInt32(3),
            AddedUtc = DateTime.TryParse(reader.GetString(4), out var added) ? added : DateTime.UtcNow
        };
    }

    // ── User Story Rating ──

    public async Task SaveUserStoryRatingAsync(UserStoryRating rating, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rating);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO UserStoryRatings (Id, ParsedStoryId, Stars, Comment, CreatedUtc, UpdatedUtc)
            VALUES ($id, $parsedStoryId, $stars, $comment, $createdUtc, $updatedUtc)
            ON CONFLICT(ParsedStoryId) DO UPDATE SET
                Stars = $stars,
                Comment = $comment,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", rating.Id);
        command.Parameters.AddWithValue("$parsedStoryId", rating.ParsedStoryId);
        command.Parameters.AddWithValue("$stars", rating.Stars);
        command.Parameters.AddWithValue("$comment", rating.Comment);
        command.Parameters.AddWithValue("$createdUtc", rating.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("User story rating persisted: {ParsedStoryId}, Stars={Stars}", rating.ParsedStoryId, rating.Stars);
    }

    public async Task<UserStoryRating?> LoadUserStoryRatingAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, Stars, Comment, CreatedUtc, UpdatedUtc FROM UserStoryRatings WHERE ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new UserStoryRating
        {
            Id = reader.GetString(0),
            ParsedStoryId = reader.GetString(1),
            Stars = reader.GetInt32(2),
            Comment = reader.GetString(3),
            CreatedUtc = DateTime.TryParse(reader.GetString(4), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow
        };
    }

    public async Task<bool> DeleteUserStoryRatingAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM UserStoryRatings WHERE ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}
