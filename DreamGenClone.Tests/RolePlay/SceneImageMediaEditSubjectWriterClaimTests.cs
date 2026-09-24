using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The claim half of the scene-image writer seam (debug record 066).
///
/// A scene-image edit used to run unclaimed through the ONE media-edit job while its completion went
/// through the claim-guarded <c>TryCompleteImageAsync</c> — so the model produced an image, the bytes were
/// written to disk, and the row stayed 'Pending' forever. These tests pin the claim that makes the
/// completion legal.
/// </summary>
public sealed class SceneImageMediaEditSubjectWriterClaimTests
{
    [Fact]
    public async Task EditRun_ClaimsTheQueuedRow_AndItsCompletionIsThenAccepted()
    {
        await using var fixture = await Fixture.CreateAsync();

        var claimed = await fixture.Writer.ClaimAsync(EditContext(fixture.ImageId));

        Assert.True(claimed);
        var row = await fixture.Images.GetImageAsync(fixture.ImageId);
        Assert.Equal(SceneImageStatus.Generating, row!.Status);
        Assert.NotNull(row.StartedUtc);

        // The point of the claim: the guarded completion now matches the row it belongs to.
        row.Status = SceneImageStatus.Complete;
        row.FileRelativePath = $"{row.SessionId}/{row.Id}.png";
        row.CompletedUtc = DateTime.UtcNow;
        row.UpdatedUtc = DateTime.UtcNow;
        Assert.True(await fixture.Images.TryCompleteImageAsync(row));
        Assert.Equal(SceneImageStatus.Complete, (await fixture.Images.GetImageAsync(fixture.ImageId))!.Status);
    }

    [Fact]
    public async Task EditRun_ReportsAnAlreadyClaimedRowAsClaimed()
    {
        await using var fixture = await Fixture.CreateAsync();

        // A previous delivery of the same job claimed the row and died before completing it: this delivery
        // must finish the work rather than abandon the row in 'Generating'.
        Assert.True(await fixture.Writer.ClaimAsync(EditContext(fixture.ImageId)));
        Assert.True(await fixture.Writer.ClaimAsync(EditContext(fixture.ImageId)));
    }

    [Theory]
    [InlineData(SceneImageStatus.Complete)]
    [InlineData(SceneImageStatus.Cancelled)]
    [InlineData(SceneImageStatus.Failed)]
    public async Task EditRun_RefusesARowThatIsTerminal(SceneImageStatus terminal)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetStatusAsync(fixture.ImageId, terminal);

        Assert.False(await fixture.Writer.ClaimAsync(EditContext(fixture.ImageId)));
        Assert.Equal(terminal, (await fixture.Images.GetImageAsync(fixture.ImageId))!.Status);
    }

    [Fact]
    public async Task EditRun_ThrowsForAnImageThatDoesNotExist()
    {
        await using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Writer.ClaimAsync(EditContext("nobody-queued-this")));
        Assert.Contains("nobody-queued-this", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deterministic operation is deliberately never claimed: it is finished by the same job that picked
    /// it up, and its completion accepts a row that is still 'Pending'.
    /// </summary>
    [Fact]
    public async Task OperationRun_IsNeverClaimed_AndStillCompletes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var crop = MediaEditOperation.ForCrop(new MediaEditCropOperation(
            ImageCropMode.Framing,
            new ImageCropSettings(TargetAspect: 1.0, HeadroomPercent: 50, HorizontalOffsetPercent: 50),
            Measurement: null));

        Assert.True(await fixture.Writer.ClaimAsync(new MediaEditRunContext(fixture.ImageId, crop)));

        var row = await fixture.Images.GetImageAsync(fixture.ImageId);
        Assert.Equal(SceneImageStatus.Pending, row!.Status);
        Assert.Null(row.StartedUtc);

        row.Status = SceneImageStatus.Complete;
        row.FileRelativePath = $"{row.SessionId}/{row.Id}.png";
        row.CompletedUtc = DateTime.UtcNow;
        row.UpdatedUtc = DateTime.UtcNow;
        Assert.True(await fixture.Images.TryCompleteOperationImageAsync(row));
    }

    private static MediaEditRunContext EditContext(string imageId)
        => new(imageId, MediaEditOperation.ForEdit);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;

        private Fixture(string root, string dbPath, SceneImageRepository images, SceneImageMediaEditSubjectWriter writer)
        {
            _root = root;
            DbPath = dbPath;
            Images = images;
            Writer = writer;
        }

        public string DbPath { get; }
        public SceneImageRepository Images { get; }
        public SceneImageMediaEditSubjectWriter Writer { get; }
        public string ImageId { get; private set; } = string.Empty;

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"scene-image-claim-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var dbPath = Path.Combine(root, "claim.db");

            // Pooling=False keeps this fixture off the shared pool: ClearAllPools() is process-wide and
            // destabilises tests running in parallel.
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(root, "scene-images")
            });

            var images = new SceneImageRepository(options);
            var writer = new SceneImageMediaEditSubjectWriter(
                images,
                new SceneImageEditRepository(options),
                new SceneImageStorageService(options, NullLogger<SceneImageStorageService>.Instance),
                NullLogger<SceneImageMediaEditSubjectWriter>.Instance);

            var image = new SceneImageRecord
            {
                SessionId = "session-1",
                InteractionId = "interaction-1",
                PromptRecordId = "prompt-1",
                PromptSnapshot = "make the shirt red",
                Status = SceneImageStatus.Pending,
                Operation = SceneImageOperation.Edit,
                SourceImageId = "source-1",
                ProductionStage = SceneImageProductionStage.Identity,
                ContentPolicy = ImageContentPolicy.Unknown,
                SettingsJson = "{}"
            };
            await images.InsertImageAsync(image);

            return new Fixture(root, dbPath, images, writer) { ImageId = image.Id };
        }

        public async Task SetStatusAsync(string imageId, SceneImageStatus status)
        {
            var row = await Images.GetImageAsync(imageId)
                ?? throw new InvalidOperationException($"Image '{imageId}' was not found.");
            row.Status = status;
            row.ErrorMessage = status == SceneImageStatus.Failed ? "failed earlier" : row.ErrorMessage;
            row.CompletedUtc = status is SceneImageStatus.Complete or SceneImageStatus.Cancelled
                ? DateTime.UtcNow
                : row.CompletedUtc;
            row.UpdatedUtc = DateTime.UtcNow;
            await Images.InsertImageAsync(row);
        }

        public ValueTask DisposeAsync()
        {
            // No SqliteConnection.ClearAllPools(): process-wide and destabilises parallel tests.
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(DbPath + suffix)) File.Delete(DbPath + suffix); } catch (IOException) { }
            }
            return ValueTask.CompletedTask;
        }
    }
}
