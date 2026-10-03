using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.ModelManager;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The scene-LoRA catalog is the storage spot for Krea 2's model LoRAs (unlock / act / anatomy / style) and, per
/// family, for every other family's. These tests pin the two things that make it safe: listing is filtered to the
/// render's family, and a row that cannot be interpreted fails fast instead of being offered to every model.
/// </summary>
public sealed class SceneLoraRepositoryTests
{
    [Fact]
    public async Task ListAsync_ReturnsOnlyTheRequestedFamilyAndOnlyEnabledRows()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.InsertAsync("krea2-unlock", "krea2_nsfw_v4_v43exp.safetensors", "Krea2 NSFW V4",
            SceneImageModelFamily.Krea2, SceneLoraCategory.Unlock, 1.0, enabled: true);
        await fixture.InsertAsync("krea2-act", "krea2_act_cowgirl_lokr.safetensors", "Cowgirl act LoKr",
            SceneImageModelFamily.Krea2, SceneLoraCategory.Act, 1.0, enabled: true);
        await fixture.InsertAsync("krea2-off", "krea2_disabled.safetensors", "Disabled",
            SceneImageModelFamily.Krea2, SceneLoraCategory.Style, 0.7, enabled: false);
        await fixture.InsertAsync("sdxl-anatomy", "sdxl_anatomy.safetensors", "SDXL anatomy",
            SceneImageModelFamily.Sdxl, SceneLoraCategory.Anatomy, 0.6, enabled: true);

        var krea2 = await fixture.Repository.ListAsync(SceneImageModelFamily.Krea2);

        Assert.Equal(2, krea2.Count);
        Assert.All(krea2, row => Assert.Equal(SceneImageModelFamily.Krea2, row.SceneImageModelFamily));
        Assert.DoesNotContain(krea2, row => row.FileName == "krea2_disabled.safetensors");
        Assert.DoesNotContain(krea2, row => row.FileName == "sdxl_anatomy.safetensors");

        var sdxl = await fixture.Repository.ListAsync(SceneImageModelFamily.Sdxl);
        Assert.Single(sdxl);
        Assert.Equal(0.6, sdxl[0].DefaultStrength);
    }

    [Fact]
    public async Task ListAsync_FamilyWithNoRows_ReturnsEmpty()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Empty(await fixture.Repository.ListAsync(SceneImageModelFamily.Krea2));
    }

    [Fact]
    public async Task ListAsync_UnknownFamily_IsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Repository.ListAsync(SceneImageModelFamily.Unknown));
    }

    [Fact]
    public async Task GetByFileNameAsync_ReturnsTheRowRegardlessOfFamily()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.InsertAsync("krea2-act", "krea2_act_cowgirl_lokr.safetensors", "Cowgirl act LoKr",
            SceneImageModelFamily.Krea2, SceneLoraCategory.Act, 1.0, enabled: true);

        var row = await fixture.Repository.GetByFileNameAsync("krea2_act_cowgirl_lokr.safetensors");

        Assert.NotNull(row);
        Assert.Equal(SceneLoraCategory.Act, row!.Category);
        Assert.Null(await fixture.Repository.GetByFileNameAsync("does_not_exist.safetensors"));
    }

    /// <summary>
    /// A row whose family cannot be interpreted must not be offered to any model. Listing filters by the requested
    /// family, so such a row is simply never listed; the unfiltered lookup is where it is caught, and it is caught by
    /// name rather than being read as <c>Unknown</c> (which would make it look applicable to every family).
    /// </summary>
    [Fact]
    public async Task GetByFileNameAsync_UnknownFamilyNameInARow_FailsFastRatherThanReadingAsUnknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync("""
            INSERT INTO SceneLoras
                (Id, FileName, DisplayName, SceneImageModelFamily, Category, DefaultStrength, IsEnabled, Notes, CreatedUtc)
            VALUES ('broken', 'broken.safetensors', 'Broken', 'NotAFamily', 'Act', 1.0, 1, NULL, '2026-10-02T00:00:00Z');
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.GetByFileNameAsync("broken.safetensors"));

        Assert.Contains("NotAFamily", exception.Message, StringComparison.Ordinal);

        // ...and it is never listed for any family either.
        Assert.Empty(await fixture.Repository.ListAsync(SceneImageModelFamily.Krea2));
    }

    [Fact]
    public async Task GetByFileNameAsync_UnknownCategoryInARow_FailsFast()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync("""
            INSERT INTO SceneLoras
                (Id, FileName, DisplayName, SceneImageModelFamily, Category, DefaultStrength, IsEnabled, Notes, CreatedUtc)
            VALUES ('broken', 'broken.safetensors', 'Broken', 'Krea2', 'NotACategory', 1.0, 1, NULL, '2026-10-02T00:00:00Z');
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.GetByFileNameAsync("broken.safetensors"));

        Assert.Contains("NotACategory", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A LoRA applied at a strength nobody chose is a different LoRA, so a non-positive strength is refused by the
    /// SCHEMA - it cannot even be written. That is stronger than a read-time check, and this pins it.
    /// </summary>
    [Fact]
    public async Task NonPositiveDefaultStrength_CannotBeWrittenAtAll()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(() => fixture.ExecuteAsync("""
            INSERT INTO SceneLoras
                (Id, FileName, DisplayName, SceneImageModelFamily, Category, DefaultStrength, IsEnabled, Notes, CreatedUtc)
            VALUES ('zero', 'zero.safetensors', 'Zero', 'Krea2', 'Act', 0, 1, NULL, '2026-10-02T00:00:00Z');
            """));

        Assert.Contains("CHECK constraint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _dbPath;

        public SceneLoraRepository Repository { get; }
        public string ConnectionString { get; }

        private Fixture(string dbPath, SceneLoraRepository repository, string connectionString)
        {
            _dbPath = dbPath;
            Repository = repository;
            ConnectionString = connectionString;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"scene-lora-{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={dbPath};Pooling=False";
            var options = Options.Create(new PersistenceOptions { ConnectionString = connectionString });
            var repository = new SceneLoraRepository(options);
            await repository.EnsureSchemaAsync();
            return new Fixture(dbPath, repository, connectionString);
        }

        public async Task InsertAsync(
            string id,
            string fileName,
            string displayName,
            SceneImageModelFamily family,
            SceneLoraCategory category,
            double defaultStrength,
            bool enabled)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO SceneLoras
                    (Id, FileName, DisplayName, SceneImageModelFamily, Category, DefaultStrength, IsEnabled, Notes, CreatedUtc)
                VALUES ($id, $file, $name, $family, $category, $strength, $enabled, NULL, '2026-10-02T00:00:00Z');
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$file", fileName);
            command.Parameters.AddWithValue("$name", displayName);
            command.Parameters.AddWithValue("$family", family.ToString());
            command.Parameters.AddWithValue("$category", category.ToString());
            command.Parameters.AddWithValue("$strength", defaultStrength);
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            await command.ExecuteNonQueryAsync();
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
            }
            catch (IOException)
            {
                // A locked temp file must not fail the test run.
            }

            return ValueTask.CompletedTask;
        }
    }
}
