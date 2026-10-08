using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pins the composed-clip store (B-152). The lifecycle tests exist because a missing SQL parameter binding in
/// <c>TryCompleteAsync</c> (the WHERE clause guards on the claimed status, but <c>$rendering</c> was never added)
/// shipped: a real render finished, both its files were written to disk, and the completion update threw
/// <c>Must add values for the following parameters: $rendering</c>, failing a clip that had actually succeeded.
/// Every statement in the store is exercised here so that class of defect cannot return unnoticed.
/// </summary>
public sealed class SceneVideoRepositoryTests
{
    [Fact]
    public async Task Lifecycle_ClaimThenComplete_PersistsTheVerificationAndTheClip()
    {
        using var fixture = await Fixture.CreateAsync();
        var record = NewRecord();
        await fixture.Repository.InsertAsync(record);

        Assert.True(await fixture.Repository.TryClaimAsync(record.Id, DateTime.UtcNow));

        var claimed = await fixture.Repository.GetAsync(record.Id);
        Assert.NotNull(claimed);
        Assert.Equal(SceneVideoStatus.Rendering, claimed!.Status);
        Assert.NotNull(claimed.StartedUtc);

        var completed = await fixture.Repository.GetAsync(record.Id);
        completed!.FileRelativePath = "11980a46/clip_00001_.mp4";
        completed.NormalizedFileRelativePath = "11980a46/clip_00001__norm.mp4";
        completed.VideoStreamPresent = true;
        completed.AudioStreamPresent = true;
        completed.MeasuredLoudnessLufs = -15.8;
        completed.LoudnessTargetLufs = -16;
        completed.MeasuredDurationSeconds = 5.167;
        completed.VerificationNotes = "h264 + aac, loudness normalized";
        completed.CompletedUtc = DateTime.UtcNow;

        Assert.True(await fixture.Repository.TryCompleteAsync(completed));

        var stored = await fixture.Repository.GetAsync(record.Id);
        Assert.NotNull(stored);
        Assert.Equal(SceneVideoStatus.Complete, stored!.Status);
        Assert.Equal("11980a46/clip_00001_.mp4", stored.FileRelativePath);
        Assert.Equal("11980a46/clip_00001__norm.mp4", stored.NormalizedFileRelativePath);
        Assert.True(stored.VideoStreamPresent);
        Assert.True(stored.AudioStreamPresent);
        Assert.Equal(-15.8, stored.MeasuredLoudnessLufs);
        Assert.Equal(-16, stored.LoudnessTargetLufs);
        Assert.Equal(5.167, stored.MeasuredDurationSeconds);
        Assert.Equal("h264 + aac, loudness normalized", stored.VerificationNotes);
        Assert.NotNull(stored.CompletedUtc);
    }

    [Fact]
    public async Task Lifecycle_Complete_RefusesARowThatWasNeverClaimed()
    {
        using var fixture = await Fixture.CreateAsync();
        var record = NewRecord();
        await fixture.Repository.InsertAsync(record);

        var updated = await fixture.Repository.TryCompleteAsync(record);

        Assert.False(updated);
        var stored = await fixture.Repository.GetAsync(record.Id);
        Assert.Equal(SceneVideoStatus.Pending, stored!.Status);
    }

    [Fact]
    public async Task Lifecycle_FailRecordsTheErrorFromEitherOpenStatus()
    {
        using var fixture = await Fixture.CreateAsync();
        var pending = NewRecord();
        var rendering = NewRecord();
        await fixture.Repository.InsertAsync(pending);
        await fixture.Repository.InsertAsync(rendering);
        await fixture.Repository.TryClaimAsync(rendering.Id, DateTime.UtcNow);

        var pendingFailure = await fixture.Repository.GetAsync(pending.Id);
        pendingFailure!.ErrorMessage = "the host refused the graph";
        pendingFailure.CompletedUtc = DateTime.UtcNow;
        Assert.True(await fixture.Repository.TryFailAsync(pendingFailure));

        var renderingFailure = await fixture.Repository.GetAsync(rendering.Id);
        renderingFailure!.ErrorMessage = "the render timed out";
        renderingFailure.CompletedUtc = DateTime.UtcNow;
        Assert.True(await fixture.Repository.TryFailAsync(renderingFailure));

        Assert.Equal(SceneVideoStatus.Failed, (await fixture.Repository.GetAsync(pending.Id))!.Status);
        var failed = await fixture.Repository.GetAsync(rendering.Id);
        Assert.Equal(SceneVideoStatus.Failed, failed!.Status);
        Assert.Equal("the render timed out", failed.ErrorMessage);
    }

    [Fact]
    public async Task Lifecycle_CancelMovesAPendingOrRenderingRowAndNothingElse()
    {
        using var fixture = await Fixture.CreateAsync();
        var pending = NewRecord();
        var rendering = NewRecord();
        var complete = NewRecord();
        await fixture.Repository.InsertAsync(pending);
        await fixture.Repository.InsertAsync(rendering);
        await fixture.Repository.InsertAsync(complete);
        await fixture.Repository.TryClaimAsync(rendering.Id, DateTime.UtcNow);
        await fixture.Repository.TryClaimAsync(complete.Id, DateTime.UtcNow);
        var completing = await fixture.Repository.GetAsync(complete.Id);
        completing!.CompletedUtc = DateTime.UtcNow;
        await fixture.Repository.TryCompleteAsync(completing);

        Assert.True(await fixture.Repository.TryCancelAsync(pending.Id, DateTime.UtcNow));
        Assert.True(await fixture.Repository.TryCancelAsync(rendering.Id, DateTime.UtcNow));
        Assert.False(await fixture.Repository.TryCancelAsync(complete.Id, DateTime.UtcNow));

        Assert.Equal(SceneVideoStatus.Cancelled, (await fixture.Repository.GetAsync(pending.Id))!.Status);
        Assert.Equal(SceneVideoStatus.Cancelled, (await fixture.Repository.GetAsync(rendering.Id))!.Status);
        Assert.Equal(SceneVideoStatus.Complete, (await fixture.Repository.GetAsync(complete.Id))!.Status);
    }

    [Fact]
    public async Task Insert_RoundTripsEveryMappedField()
    {
        using var fixture = await Fixture.CreateAsync();
        var record = NewRecord();
        record.Title = "the radiator scene";
        record.SessionId = "session-1";
        record.InteractionId = "interaction-2";
        record.OriginKind = SceneVideoOriginKind.SceneImage;
        record.OriginImageId = "scene-image-3";
        record.OriginCoveragePlanId = null;
        record.PromptSnapshot = "subject_definitions:\n<Picture 1> ...";
        record.PromptManuallyEdited = true;
        record.CompilerKey = "minimax-h3-ref2va-six-section";
        record.CompilerVersion = "1.1.0";
        record.SettingsJson = """{"style":"handheld"}""";
        record.ReferencesJson = """[{"role":"FrameAnchor"}]""";
        record.LoraStackJson = """[{"FileName":"a.safetensors","Strength":0.8}]""";
        record.ModelIdentifier = "minimax_h3_ref2va_pruned_w4a8_mixed.safetensors";
        record.RequestedModelId = "5923da81-4fea-413e-b9b2-8a3af893e9ff";
        record.ProviderName = "Local ComfyUI (WOOD-GAME-MAIN 5080)";
        record.Seed = 42;
        record.Width = 1344;
        record.Height = 768;
        record.Length = 124;
        record.Steps = 40;
        record.Fps = 24;
        record.RefImageSize = "match";
        record.JobId = "job-4";
        await fixture.Repository.InsertAsync(record);

        var stored = await fixture.Repository.GetAsync(record.Id);

        Assert.NotNull(stored);
        Assert.Equal("the radiator scene", stored!.Title);
        Assert.Equal("session-1", stored.SessionId);
        Assert.Equal("interaction-2", stored.InteractionId);
        Assert.Equal(SceneVideoStatus.Pending, stored.Status);
        Assert.Equal(SceneVideoOriginKind.SceneImage, stored.OriginKind);
        Assert.Equal("scene-image-3", stored.OriginImageId);
        Assert.Null(stored.OriginCoveragePlanId);
        Assert.Equal("subject_definitions:\n<Picture 1> ...", stored.PromptSnapshot);
        Assert.True(stored.PromptManuallyEdited);
        Assert.Equal("minimax-h3-ref2va-six-section", stored.CompilerKey);
        Assert.Equal("1.1.0", stored.CompilerVersion);
        Assert.Equal("""{"style":"handheld"}""", stored.SettingsJson);
        Assert.Equal("""[{"role":"FrameAnchor"}]""", stored.ReferencesJson);
        Assert.Equal("""[{"FileName":"a.safetensors","Strength":0.8}]""", stored.LoraStackJson);
        Assert.Equal("minimax_h3_ref2va_pruned_w4a8_mixed.safetensors", stored.ModelIdentifier);
        Assert.Equal("5923da81-4fea-413e-b9b2-8a3af893e9ff", stored.RequestedModelId);
        Assert.Equal("Local ComfyUI (WOOD-GAME-MAIN 5080)", stored.ProviderName);
        Assert.Equal(42, stored.Seed);
        Assert.Equal(1344, stored.Width);
        Assert.Equal(768, stored.Height);
        Assert.Equal(124, stored.Length);
        Assert.Equal(40, stored.Steps);
        Assert.Equal(24, stored.Fps);
        Assert.Equal("match", stored.RefImageSize);
        Assert.Equal("job-4", stored.JobId);
        Assert.Null(stored.ErrorMessage);
        Assert.Null(stored.StartedUtc);
        Assert.Null(stored.CompletedUtc);
        Assert.Null(stored.VideoStreamPresent);
        Assert.Null(stored.AudioStreamPresent);
    }

    [Fact]
    public async Task ListRecent_And_ListBySession_ReturnNewestFirst()
    {
        using var fixture = await Fixture.CreateAsync();
        var older = NewRecord();
        older.SessionId = "session-1";
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-10);
        var newer = NewRecord();
        newer.SessionId = "session-1";
        newer.CreatedUtc = DateTime.UtcNow;
        var other = NewRecord();
        other.SessionId = "session-2";
        other.CreatedUtc = DateTime.UtcNow;
        await fixture.Repository.InsertAsync(older);
        await fixture.Repository.InsertAsync(newer);
        await fixture.Repository.InsertAsync(other);

        var recent = await fixture.Repository.ListRecentAsync(2);
        var bySession = await fixture.Repository.ListBySessionAsync("session-1");

        Assert.Equal(2, recent.Count);
        Assert.Equal(2, bySession.Count);
        Assert.All(bySession, record => Assert.Equal("session-1", record.SessionId));
        Assert.Contains(bySession, record => record.Id == newer.Id);
        Assert.Contains(bySession, record => record.Id == older.Id);
    }

    [Fact]
    public async Task Delete_RemovesTheRowAndIsIdempotent()
    {
        using var fixture = await Fixture.CreateAsync();
        var record = NewRecord();
        await fixture.Repository.InsertAsync(record);

        await fixture.Repository.DeleteAsync(record.Id);
        await fixture.Repository.DeleteAsync(record.Id);

        Assert.Null(await fixture.Repository.GetAsync(record.Id));
    }

    /// <summary>
    /// A database created before B-156 has no continuation columns, and opening one used to fail outright: the schema
    /// batch ended with <c>CREATE INDEX ... ON SceneVideos (SourceVideoId)</c>, which throws
    /// <c>no such column: SourceVideoId</c> on such a database <em>before</em> the additive column migration could run.
    /// Every repository call then threw and the composer showed the raw SQLite error - the whole feature was dead on
    /// any pre-existing install. This rebuilds that exact state (the current schema with the continuation columns
    /// dropped) and proves opening the store upgrades it in place instead of refusing.
    /// </summary>
    [Fact]
    public async Task Open_UpgradesADatabaseCreatedBeforeTheContinuationColumnsExisted()
    {
        using var fixture = await Fixture.CreateAsync();
        var record = NewRecord();
        record.Title = "the radiator scene";
        await fixture.Repository.InsertAsync(record);

        await fixture.DowngradeToPreContinuationSchemaAsync();
        Assert.Equal(0, await fixture.CountColumnAsync("SourceVideoId"));
        Assert.False(await fixture.IndexExistsAsync("IX_SceneVideos_SourceVideo"));

        // The act under test. Before the fix this threw 'no such column: SourceVideoId'.
        await fixture.Repository.EnsureSchemaAsync();

        foreach (var column in Fixture.ContinuationColumnsForTest)
        {
            Assert.Equal(1, await fixture.CountColumnAsync(column));
        }

        Assert.True(await fixture.IndexExistsAsync("IX_SceneVideos_SourceVideo"));

        var stored = await fixture.Repository.GetAsync(record.Id);
        Assert.NotNull(stored);
        Assert.Equal("the radiator scene", stored!.Title);
        Assert.Equal(SceneVideoStatus.Pending, stored.Status);
        Assert.Equal(124, stored.Length);
        Assert.Null(stored.SourceVideoId);
        Assert.Null(stored.ContinuationKind);
        Assert.Null(stored.GuideFrameIndex);
        Assert.Null(stored.SourceFrameRelativePath);
        Assert.Null(stored.SourceFrameSha256);
        Assert.Null(stored.DriftMetricsJson);
    }

    /// <summary>A schema-complete pending composition: the store requires a prompt and its settings blob.</summary>
    private static SceneVideoRecord NewRecord() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Title = string.Empty,
        Status = SceneVideoStatus.Pending,
        OriginKind = SceneVideoOriginKind.AssetImage,
        OriginImageId = "asset-image-1",
        PromptSnapshot = "the compiled six-section document",
        SettingsJson = "{}",
        Length = 124,
        Width = 1344,
        Height = 768,
        Steps = 40,
        Fps = 24,
        RefImageSize = "match",
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow
    };

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, SceneVideoRepository repository)
        {
            Root = root;
            Repository = repository;
        }

        public string Root { get; }

        public SceneVideoRepository Repository { get; }

        public string DatabasePath => Path.Combine(Root, "scene-video.db");

        /// <summary>The six columns B-156 adds, in the order the additive migration adds them.</summary>
        public static readonly string[] ContinuationColumnsForTest =
        [
            "SourceVideoId", "ContinuationKind", "GuideFrameIndex",
            "SourceFrameRelativePath", "SourceFrameSha256", "DriftMetricsJson"
        ];

        /// <summary>
        /// Rewinds the store to its pre-B-156 physical shape by dropping the continuation columns. The index goes
        /// first: SQLite refuses to drop a column an index depends on.
        /// </summary>
        public async Task DowngradeToPreContinuationSchemaAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            await connection.OpenAsync();

            await using (var dropIndex = connection.CreateCommand())
            {
                dropIndex.CommandText = "DROP INDEX IF EXISTS IX_SceneVideos_SourceVideo;";
                await dropIndex.ExecuteNonQueryAsync();
            }

            foreach (var column in ContinuationColumnsForTest)
            {
                await using var drop = connection.CreateCommand();
                drop.CommandText = $"ALTER TABLE SceneVideos DROP COLUMN {column};";
                await drop.ExecuteNonQueryAsync();
            }
        }

        public async Task<long> CountColumnAsync(string column)
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM pragma_table_info('SceneVideos') WHERE name = '{column}'";
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async Task<bool> IndexExistsAsync(string name)
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $name";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"scene-video-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={Path.Combine(root, "scene-video.db")};Pooling=False"
            });
            var repository = new SceneVideoRepository(options);
            await repository.EnsureSchemaAsync();
            return new Fixture(root, repository);
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
