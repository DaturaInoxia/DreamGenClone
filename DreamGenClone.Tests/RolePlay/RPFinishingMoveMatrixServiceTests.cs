using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPFinishingMoveMatrixServiceTests : IDisposable
{
    private readonly string _databasePath;

    public RPFinishingMoveMatrixServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-rpfinish-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SaveListDeleteRowAsync_RoundTripsRow()
    {
        var service = await CreateServiceAsync();

        var created = await service.SaveFinishingMoveMatrixRowAsync(new RPFinishingMoveMatrixRow
        {
            DesireBand = "60-100",
            SelfRespectBand = "0-29",
            OtherManDominanceBand = "60-100",
            PrimaryLocations = ["bedroom", "living room"],
            SecondaryLocations = ["balcony"],
            ExcludedLocations = ["bathroom"],
            WifeBehaviorModifier = "aggressive",
            OtherManBehaviorModifier = "leading",
            TransitionInstruction = "close scene strongly",
            SortOrder = 2,
            IsEnabled = true
        });

        var rows = await service.ListFinishingMoveMatrixRowsAsync();
        var row = Assert.Single(rows);
        Assert.Equal(created.Id, row.Id);
        Assert.Equal("60-100", row.DesireBand);
        Assert.Equal("0-29", row.SelfRespectBand);
        Assert.Equal("60-100", row.OtherManDominanceBand);
        Assert.Contains("bedroom", row.PrimaryLocations);
        Assert.Contains("bathroom", row.ExcludedLocations);

        var deleted = await service.DeleteFinishingMoveMatrixRowAsync(row.Id);
        Assert.True(deleted);
        Assert.Empty(await service.ListFinishingMoveMatrixRowsAsync());
    }

    [Fact]
    public async Task ImportFinishingMoveMatrixRowsFromJsonAsync_ReplaceExisting_ReplacesRows()
    {
        var service = await CreateServiceAsync();

        await service.SaveFinishingMoveMatrixRowAsync(new RPFinishingMoveMatrixRow
        {
            DesireBand = "30-59",
            SelfRespectBand = "30-59",
            OtherManDominanceBand = "30-59",
            PrimaryLocations = ["kitchen"],
            SortOrder = 0,
            IsEnabled = true
        });

        var json = """
        {
          "rows": [
            {
              "desireBand": "75-100",
              "selfRespectBand": "0-29",
              "otherManDominanceBand": "60-100",
              "primaryLocations": ["bedroom"],
              "secondaryLocations": ["living room"],
              "excludedLocations": ["bathroom"],
              "wifeBehaviorModifier": "assertive",
              "otherManBehaviorModifier": "dominant",
              "transitionInstruction": "hard close",
              "sortOrder": 1,
              "isEnabled": true
            },
            {
              "desire": "25-49",
              "selfRespect": "60-89",
              "dominance": "0-29",
              "locationsPrimary": ["hallway"],
              "locationsSecondary": [],
              "locationsExcluded": ["bedroom"],
              "wifeBehavior": "hesitant",
              "otherManBehavior": "subtle",
              "transition": "soft close",
              "sortOrder": 2,
              "isEnabled": false
            }
          ]
        }
        """;

        var imported = await service.ImportFinishingMoveMatrixRowsFromJsonAsync(json, replaceExisting: true);
        Assert.Equal(2, imported);

        var rows = await service.ListFinishingMoveMatrixRowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.DesireBand == "50-74");

        var high = Assert.Single(rows, r => r.OtherManDominanceBand == "60-100");
        Assert.Contains("bedroom", high.PrimaryLocations);

        var low = Assert.Single(rows, r => r.OtherManDominanceBand == "0-29");
        Assert.False(low.IsEnabled);
        Assert.Contains("bedroom", low.ExcludedLocations);
    }

    [Fact]
    public async Task InitializeAsync_Twice_KeepsFinishingMoveMatrixSchemaIdempotent()
    {
        var persistence = CreatePersistence();
        await persistence.InitializeAsync();
        await persistence.InitializeAsync();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        Assert.True(await SqlObjectExistsAsync(connection, "table", "RPFinishingMoveMatrixRows"));
        Assert.True(await SqlObjectExistsAsync(connection, "index", "IX_RPFinishingMoveMatrixRows_Sort"));

        var service = CreateService();

        await service.SaveFinishingMoveMatrixRowAsync(new RPFinishingMoveMatrixRow
        {
            DesireBand = "30-59",
            SelfRespectBand = "30-59",
            OtherManDominanceBand = "30-59",
            PrimaryLocations = ["living room"],
            SortOrder = 0,
            IsEnabled = true
        });

        var rows = await service.ListFinishingMoveMatrixRowsAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task SaveFinishingMoveMatrixRowAsync_BackfillsTable_WhenMissingFromExistingDb()
    {
        var service = await CreateServiceAsync();
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();

            await using var dropIndex = connection.CreateCommand();
            dropIndex.CommandText = "DROP INDEX IF EXISTS IX_RPFinishingMoveMatrixRows_Sort;";
            await dropIndex.ExecuteNonQueryAsync();

            await using var dropTable = connection.CreateCommand();
            dropTable.CommandText = "DROP TABLE IF EXISTS RPFinishingMoveMatrixRows;";
            await dropTable.ExecuteNonQueryAsync();
        }

        var freshService = CreateService();
        await freshService.SaveFinishingMoveMatrixRowAsync(new RPFinishingMoveMatrixRow
        {
            DesireBand = "60-100",
            SelfRespectBand = "0-29",
            OtherManDominanceBand = "60-100",
            PrimaryLocations = ["bedroom"],
            SortOrder = 1,
            IsEnabled = true
        });

        var rows = await freshService.ListFinishingMoveMatrixRowsAsync();
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
