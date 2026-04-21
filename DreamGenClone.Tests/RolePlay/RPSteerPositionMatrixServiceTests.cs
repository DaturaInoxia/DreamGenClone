using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPSteerPositionMatrixServiceTests : IDisposable
{
    private readonly string _databasePath;

    public RPSteerPositionMatrixServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-rpsteer-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SaveListDeleteRowAsync_RoundTripsRow()
    {
        var service = await CreateServiceAsync();

        var created = await service.SaveSteerPositionMatrixRowAsync(new RPSteerPositionMatrixRow
        {
            DesireBand = "75-100",
            SelfRespectBand = "0-29",
            WifeDominanceBand = "High",
            OtherManDominanceBand = "High",
            PrimaryPositions = ["missionary", "doggy"],
            SecondaryPositions = ["standing"],
            ExcludedPositions = ["oral"],
            WifeBehaviorModifier = "assertive",
            OtherManBehaviorModifier = "commanding",
            TransitionInstruction = "shift physically with urgency",
            SortOrder = 2,
            IsEnabled = true
        });

        var rows = await service.ListSteerPositionMatrixRowsAsync();
        var row = Assert.Single(rows);
        Assert.Equal(created.Id, row.Id);
        Assert.Equal("75-100", row.DesireBand);
        Assert.Equal("0-29", row.SelfRespectBand);
        Assert.Equal("High", row.WifeDominanceBand);
        Assert.Equal("High", row.OtherManDominanceBand);
        Assert.Contains("missionary", row.PrimaryPositions);
        Assert.Contains("oral", row.ExcludedPositions);

        var deleted = await service.DeleteSteerPositionMatrixRowAsync(row.Id);
        Assert.True(deleted);
        Assert.Empty(await service.ListSteerPositionMatrixRowsAsync());
    }

    [Fact]
    public async Task ImportSteerPositionMatrixRowsFromJsonAsync_ReplaceExisting_ReplacesRows()
    {
        var service = await CreateServiceAsync();

        await service.SaveSteerPositionMatrixRowAsync(new RPSteerPositionMatrixRow
        {
            DesireBand = "50-74",
            SelfRespectBand = "30-59",
            WifeDominanceBand = "Medium",
            OtherManDominanceBand = "Medium",
            PrimaryPositions = ["standing"],
            SortOrder = 0,
            IsEnabled = true
        });

        var json = """
        {
          "rows": [
            {
              "desireBand": "75-100",
              "selfRespectBand": "0-29",
              "wifeDominanceBand": "High",
              "otherManDominanceBand": "High",
              "primaryPositions": ["missionary"],
              "secondaryPositions": ["doggy"],
              "excludedPositions": ["oral"],
              "wifeBehaviorModifier": "assertive",
              "otherManBehaviorModifier": "dominant",
              "transitionInstruction": "hard shift",
              "sortOrder": 1,
              "isEnabled": true
            },
            {
              "desire": "25-49",
              "selfRespect": "60-89",
              "wifeDominance": "Low",
              "otherManDominance": "Low",
              "positionsPrimary": ["spooning"],
              "positionsSecondary": [],
              "positionsExcluded": ["missionary"],
              "wifeBehavior": "hesitant",
              "otherManBehavior": "subtle",
              "transition": "soft shift",
              "sortOrder": 2,
              "isEnabled": false
            }
          ]
        }
        """;

        var imported = await service.ImportSteerPositionMatrixRowsFromJsonAsync(json, replaceExisting: true);
        Assert.Equal(2, imported);

        var rows = await service.ListSteerPositionMatrixRowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.WifeDominanceBand == "Medium" && r.OtherManDominanceBand == "Medium");

        var high = Assert.Single(rows, r => r.WifeDominanceBand == "High");
        Assert.Contains("missionary", high.PrimaryPositions);

        var low = Assert.Single(rows, r => r.WifeDominanceBand == "Low");
        Assert.False(low.IsEnabled);
        Assert.Contains("missionary", low.ExcludedPositions);
    }

    [Fact]
    public async Task InitializeAsync_Twice_KeepsSteerPositionMatrixSchemaIdempotent()
    {
        var persistence = CreatePersistence();
        await persistence.InitializeAsync();
        await persistence.InitializeAsync();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        Assert.True(await SqlObjectExistsAsync(connection, "table", "RPSteerPositionMatrixRows"));
        Assert.True(await SqlObjectExistsAsync(connection, "index", "IX_RPSteerPositionMatrixRows_Sort"));

        var service = CreateService();

        await service.SaveSteerPositionMatrixRowAsync(new RPSteerPositionMatrixRow
        {
            DesireBand = "50-74",
            SelfRespectBand = "30-59",
            WifeDominanceBand = "Medium",
            OtherManDominanceBand = "Medium",
            PrimaryPositions = ["standing"],
            SortOrder = 0,
            IsEnabled = true
        });

        var rows = await service.ListSteerPositionMatrixRowsAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task SaveSteerPositionMatrixRowAsync_BackfillsTable_WhenMissingFromExistingDb()
    {
        var service = await CreateServiceAsync();
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();

            await using var dropIndex = connection.CreateCommand();
            dropIndex.CommandText = "DROP INDEX IF EXISTS IX_RPSteerPositionMatrixRows_Sort;";
            await dropIndex.ExecuteNonQueryAsync();

            await using var dropTable = connection.CreateCommand();
            dropTable.CommandText = "DROP TABLE IF EXISTS RPSteerPositionMatrixRows;";
            await dropTable.ExecuteNonQueryAsync();
        }

        var freshService = CreateService();
        await freshService.SaveSteerPositionMatrixRowAsync(new RPSteerPositionMatrixRow
        {
            DesireBand = "75-100",
            SelfRespectBand = "0-29",
            WifeDominanceBand = "High",
            OtherManDominanceBand = "High",
            PrimaryPositions = ["missionary"],
            SortOrder = 1,
            IsEnabled = true
        });

        var rows = await freshService.ListSteerPositionMatrixRowsAsync();
        var row = Assert.Single(rows);
        Assert.Equal("75-100", row.DesireBand);
    }

    private async Task<RPThemeService> CreateServiceAsync()
    {
        var sqlite = CreatePersistence();
        await sqlite.InitializeAsync();
        return CreateService();
    }

    private SqlitePersistence CreatePersistence()
    {
        var persistenceOptions = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={_databasePath}"
        });

        return new SqlitePersistence(
            persistenceOptions,
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
    }

    private RPThemeService CreateService()
    {
        var persistenceOptions = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={_databasePath}"
        });

        return new RPThemeService(persistenceOptions, NullLogger<RPThemeService>.Instance);
    }

    private static async Task<bool> SqlObjectExistsAsync(SqliteConnection connection, string objectType, string objectName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $type AND name = $name";
        command.Parameters.AddWithValue("$type", objectType);
        command.Parameters.AddWithValue("$name", objectName);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        return count > 0;
    }

    public void Dispose()
    {
        if (!File.Exists(_databasePath))
        {
            return;
        }

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // Provider cleanup can hold a transient handle after test completion.
        }
    }
}
