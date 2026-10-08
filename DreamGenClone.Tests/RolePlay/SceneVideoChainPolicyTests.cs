using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pins the B-156 chain policy (C-13): how deep a chain is, and the refusal that stops it growing past the configured
/// budget. Depth is derived from the lineage rows themselves, so these tests build real lineage in a real store
/// rather than asserting against a double that could agree with a wrong walk.
/// </summary>
public sealed class SceneVideoChainPolicyTests
{
    [Fact]
    public async Task Depth_OfAnOriginalRenderIsOne()
    {
        using var store = await Store.CreateAsync();
        var original = await store.InsertAsync(sourceVideoId: null);

        Assert.Equal(1, await store.Policy.ResolveDepthAsync(original));
        Assert.Equal(original.Id, await store.Policy.ResolveRootIdAsync(original));
    }

    [Fact]
    public async Task Depth_CountsEveryLinkBackToTheOriginal()
    {
        using var store = await Store.CreateAsync();
        var original = await store.InsertAsync(sourceVideoId: null);
        var first = await store.InsertAsync(original.Id);
        var second = await store.InsertAsync(first.Id);

        Assert.Equal(2, await store.Policy.ResolveDepthAsync(first));
        Assert.Equal(3, await store.Policy.ResolveDepthAsync(second));
        Assert.Equal(original.Id, await store.Policy.ResolveRootIdAsync(second));
    }

    [Fact]
    public async Task Depth_RefusesACyclicChainByName()
    {
        using var store = await Store.CreateAsync();
        var first = await store.InsertAsync(sourceVideoId: null);
        var second = await store.InsertAsync(first.Id);
        await store.SetSourceAsync(first.Id, second.Id);

        // The walk starts from the record it is handed (the service reads the source from the store first), so the
        // rewritten link has to be read back before the policy sees it.
        var reloaded = await store.Repository.GetAsync(first.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.Policy.ResolveDepthAsync(reloaded!));

        Assert.Contains("cycle", exception.Message);
        Assert.Contains(first.Id, exception.Message);
    }

    [Fact]
    public async Task Root_FollowsEveryLinkToTheOriginal()
    {
        using var store = await Store.CreateAsync();
        var original = await store.InsertAsync(sourceVideoId: null);
        var first = await store.InsertAsync(original.Id);
        var second = await store.InsertAsync(first.Id);

        Assert.Equal(original.Id, await store.Policy.ResolveRootIdAsync(second));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Budget_AllowsAChainThatReachesTheLimit(int depth) =>
        SceneVideoChainPolicy.EnsureWithinBudget(depth, budget: 3, sourceId: "clip");

    [Fact]
    public void Budget_RefusesTheLinkPastTheLimitAndNamesTheConfiguredValue()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => SceneVideoChainPolicy.EnsureWithinBudget(depth: 4, budget: 3, sourceId: "clip"));

        Assert.Contains("MaxContinuationChainLength", exception.Message);
        Assert.Contains("limit is 3", exception.Message);
        Assert.Contains("3 continuation(s)", exception.Message);
    }

    private sealed class Store : IDisposable
    {
        private Store(string root, SceneVideoRepository repository)
        {
            Root = root;
            Repository = repository;
            Policy = new SceneVideoChainPolicy(repository, NullLogger<SceneVideoChainPolicy>.Instance);
        }

        public string Root { get; }

        public SceneVideoRepository Repository { get; }

        public SceneVideoChainPolicy Policy { get; }

        private string DatabasePath => Path.Combine(Root, "chain.db");

        public static async Task<Store> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"scene-video-chain-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={Path.Combine(root, "chain.db")};Pooling=False"
            });
            var repository = new SceneVideoRepository(options);
            await repository.EnsureSchemaAsync();
            return new Store(root, repository);
        }

        public async Task<SceneVideoRecord> InsertAsync(string? sourceVideoId)
        {
            var record = new SceneVideoRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = string.Empty,
                Status = SceneVideoStatus.Complete,
                OriginKind = SceneVideoOriginKind.Standalone,
                PromptSnapshot = "the compiled six-section document",
                SettingsJson = "{}",
                Length = 124,
                Width = 1344,
                Height = 768,
                Steps = 40,
                Fps = 24,
                RefImageSize = "match",
                SourceVideoId = sourceVideoId,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await Repository.InsertAsync(record);
            return record;
        }

        /// <summary>Rewrites one link directly, so a malformed lineage can be built (no supported path creates one).</summary>
        public async Task SetSourceAsync(string id, string sourceVideoId)
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE SceneVideos SET SourceVideoId = $source WHERE Id = $id;";
            command.Parameters.AddWithValue("$source", sourceVideoId);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
                // A locked temp file must not fail the test run.
            }
        }
    }
}
