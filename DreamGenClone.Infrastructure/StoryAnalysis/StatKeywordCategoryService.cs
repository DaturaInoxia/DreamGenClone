using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class StatKeywordCategoryService : IStatKeywordCategoryService
{
    private readonly string _connectionString;
    private readonly ILogger<StatKeywordCategoryService> _logger;

    public StatKeywordCategoryService(IOptions<PersistenceOptions> options, ILogger<StatKeywordCategoryService> logger)
    {
        _connectionString = options.Value.ConnectionString;
        _logger = logger;
    }

    public Task<List<StatKeywordCategory>> ListEnabledAsync(CancellationToken cancellationToken = default)
        => ListAsync(includeDisabled: false, cancellationToken);

    public async Task<List<StatKeywordCategory>> ListAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        var categories = new List<StatKeywordCategory>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = includeDisabled
                ? "SELECT Id, Name, StatName, PerKeywordDelta, MaxAbsDelta, IsEnabled, SortOrder, CreatedUtc, UpdatedUtc FROM RolePlayStatKeywordCategories ORDER BY SortOrder, Name"
                : "SELECT Id, Name, StatName, PerKeywordDelta, MaxAbsDelta, IsEnabled, SortOrder, CreatedUtc, UpdatedUtc FROM RolePlayStatKeywordCategories WHERE IsEnabled = 1 ORDER BY SortOrder, Name";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                categories.Add(new StatKeywordCategory
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    StatName = reader.GetString(2),
                    PerKeywordDelta = reader.GetInt32(3),
                    MaxAbsDelta = reader.GetInt32(4),
                    IsEnabled = reader.GetInt32(5) == 1,
                    SortOrder = reader.GetInt32(6),
                    CreatedUtc = DateTime.TryParse(reader.GetString(7), out var created) ? created : DateTime.UtcNow,
                    UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var updated) ? updated : DateTime.UtcNow
                });
            }
        }

        if (categories.Count == 0)
        {
            return categories;
        }

        var byCategoryId = categories.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        await using var keywordCmd = connection.CreateCommand();
        keywordCmd.CommandText = "SELECT Id, CategoryId, Keyword, SortOrder FROM RolePlayStatKeywordRules ORDER BY CategoryId, SortOrder, Keyword";
        await using var keywordReader = await keywordCmd.ExecuteReaderAsync(cancellationToken);
        while (await keywordReader.ReadAsync(cancellationToken))
        {
            var categoryId = keywordReader.GetString(1);
            if (!byCategoryId.TryGetValue(categoryId, out var category))
            {
                continue;
            }

            category.Keywords.Add(new StatKeywordRule
            {
                Id = keywordReader.GetString(0),
                CategoryId = categoryId,
                Keyword = keywordReader.GetString(2),
                SortOrder = keywordReader.GetInt32(3)
            });
        }

        return categories;
    }

    public async Task<StatKeywordCategory?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(includeDisabled: true, cancellationToken);
        return all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<StatKeywordCategory> SaveAsync(StatKeywordCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        category.Id = string.IsNullOrWhiteSpace(category.Id) ? Guid.NewGuid().ToString("N") : category.Id.Trim();
        category.Name = (category.Name ?? string.Empty).Trim();
        category.StatName = (category.StatName ?? string.Empty).Trim();
        category.PerKeywordDelta = Math.Clamp(category.PerKeywordDelta, -5, 5);
        category.MaxAbsDelta = Math.Clamp(category.MaxAbsDelta, 1, 10);
        category.UpdatedUtc = DateTime.UtcNow;
        if (category.CreatedUtc == default)
        {
            category.CreatedUtc = category.UpdatedUtc;
        }

        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name is required.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(category.StatName))
        {
            throw new ArgumentException("StatName is required.", nameof(category));
        }

        category.Keywords = category.Keywords
            .Where(x => !string.IsNullOrWhiteSpace(x.Keyword))
            .Select(x => new StatKeywordRule
            {
                Id = string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("N") : x.Id.Trim(),
                CategoryId = category.Id,
                Keyword = x.Keyword.Trim(),
                SortOrder = x.SortOrder
            })
            .GroupBy(x => x.Keyword, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = @"
INSERT INTO RolePlayStatKeywordCategories (Id, Name, StatName, PerKeywordDelta, MaxAbsDelta, IsEnabled, SortOrder, CreatedUtc, UpdatedUtc)
VALUES ($id, $name, $statName, $perKeywordDelta, $maxAbsDelta, $isEnabled, $sortOrder, $createdUtc, $updatedUtc)
ON CONFLICT(Id) DO UPDATE SET
Name = excluded.Name,
StatName = excluded.StatName,
PerKeywordDelta = excluded.PerKeywordDelta,
MaxAbsDelta = excluded.MaxAbsDelta,
IsEnabled = excluded.IsEnabled,
SortOrder = excluded.SortOrder,
UpdatedUtc = excluded.UpdatedUtc;";
            command.Parameters.AddWithValue("$id", category.Id);
            command.Parameters.AddWithValue("$name", category.Name);
            command.Parameters.AddWithValue("$statName", category.StatName);
            command.Parameters.AddWithValue("$perKeywordDelta", category.PerKeywordDelta);
            command.Parameters.AddWithValue("$maxAbsDelta", category.MaxAbsDelta);
            command.Parameters.AddWithValue("$isEnabled", category.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$sortOrder", category.SortOrder);
            command.Parameters.AddWithValue("$createdUtc", category.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", category.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = tx;
            deleteCommand.CommandText = "DELETE FROM RolePlayStatKeywordRules WHERE CategoryId = $categoryId";
            deleteCommand.Parameters.AddWithValue("$categoryId", category.Id);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var keyword in category.Keywords)
        {
            await using var insertKeyword = connection.CreateCommand();
            insertKeyword.Transaction = tx;
            insertKeyword.CommandText = @"
INSERT INTO RolePlayStatKeywordRules (Id, CategoryId, Keyword, SortOrder)
VALUES ($id, $categoryId, $keyword, $sortOrder);";
            insertKeyword.Parameters.AddWithValue("$id", keyword.Id);
            insertKeyword.Parameters.AddWithValue("$categoryId", category.Id);
            insertKeyword.Parameters.AddWithValue("$keyword", keyword.Keyword);
            insertKeyword.Parameters.AddWithValue("$sortOrder", keyword.SortOrder);
            await insertKeyword.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return category;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RolePlayStatKeywordCategories WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await ListAsync(includeDisabled: true, cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var defaults = BuildDefaultCategories();
        foreach (var category in defaults)
        {
            await SaveAsync(category, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} role-play stat keyword categories.", defaults.Count);
    }

    private static List<StatKeywordCategory> BuildDefaultCategories()
    {
        return
        [
            BuildCategory("desire", "Desire", "Desire", 1, 4, ["kiss", "touch", "desire", "want", "close", "heat"], 10),
            BuildCategory("restraint", "Restraint", "Restraint", 1, 3, ["can't", "wrong", "shouldn't", "hesitate", "guilt"], 20),
            BuildCategory("tension", "Tension", "Tension", 1, 4, ["fear", "caught", "risk", "panic", "nervous"], 30),
            BuildCategory("connection", "Connection", "Connection", 1, 3, ["safe", "comfort", "trust", "reassure"], 40),
            BuildCategory("dominance", "Dominance", "Dominance", 1, 3, ["control", "command", "obey", "claim", "choose", "decide", "insist"], 50),
            BuildCategory("loyalty-positive", "Loyalty Positive", "Loyalty", 1, 5, ["husband", "wife", "promise", "vow", "faithful", "devoted", "commitment"], 60),
            BuildCategory("loyalty-negative", "Loyalty Negative", "Loyalty", -1, 5, ["affair", "betray", "cheat", "secret", "sneak", "stranger"], 70),
            BuildCategory("selfrespect-positive", "SelfRespect Positive", "SelfRespect", 1, 5, ["boundary", "boundaries", "respect", "dignity", "self-worth", "walk away", "no"], 80),
            BuildCategory("selfrespect-negative", "SelfRespect Negative", "SelfRespect", -1, 5, ["humiliate", "ashamed", "used", "degraded", "demean"], 90)
        ];
    }

    private static StatKeywordCategory BuildCategory(
        string id,
        string name,
        string statName,
        int perKeywordDelta,
        int maxAbsDelta,
        IReadOnlyList<string> keywords,
        int sortOrder)
    {
        return new StatKeywordCategory
        {
            Id = id,
            Name = name,
            StatName = statName,
            PerKeywordDelta = perKeywordDelta,
            MaxAbsDelta = maxAbsDelta,
            SortOrder = sortOrder,
            Keywords = keywords.Select((keyword, index) => new StatKeywordRule
            {
                Id = Guid.NewGuid().ToString("N"),
                CategoryId = id,
                Keyword = keyword,
                SortOrder = index + 1
            }).ToList()
        };
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS RolePlayStatKeywordCategories (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    StatName TEXT NOT NULL,
    PerKeywordDelta INTEGER NOT NULL,
    MaxAbsDelta INTEGER NOT NULL,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS RolePlayStatKeywordRules (
    Id TEXT PRIMARY KEY,
    CategoryId TEXT NOT NULL,
    Keyword TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (CategoryId) REFERENCES RolePlayStatKeywordCategories(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_RolePlayStatKeywordRules_Category_Sort
    ON RolePlayStatKeywordRules (CategoryId, SortOrder, Keyword);
";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
