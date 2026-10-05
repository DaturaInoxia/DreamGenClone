using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// An image approved by mistake has to be removable. Approval is what the reference pickers read and it is also what
/// the delete guard acts on, so those two facts are one story: the delete refuses an approved image and tells the
/// operator to stop using it first — which means "stop using it" has to exist.
///
/// <para>
/// Reported live 2026-10-03: "i approved the wrong image and now want to delete it but it wont let me, how do i stop
/// using it as referenced images." The refusal existed; the action it named did not.
/// </para>
/// </summary>
public sealed class SceneAssetImageRevokeTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>
    /// Withdrawing the approval is what stops the pickers offering the image: they read Approved and nothing else, so
    /// leaving Approved behind would keep a withdrawn image selectable.
    /// </summary>
    [Fact]
    public async Task WithdrawingTheApproval_TakesItOutOfProduction_SoNoPickerOffersIt()
    {
        var world = Build();
        try
        {
            await world.SeedApprovedImageAsync("shed", "img-1");

            var withdrawn = await world.Service.RevokeImageApprovalAsync("img-1");

            Assert.Equal(SceneAssetProductionApprovalStatus.Draft, withdrawn.ProductionApprovalStatus);
            Assert.NotEqual(SceneAssetProductionApprovalStatus.Approved, withdrawn.ProductionApprovalStatus);
        }
        finally
        {
            world.Cleanup();
        }
    }

    /// <summary>
    /// The decision that matters: it returns to Draft, NOT to Revoked. Approval refuses any row that is "not null and
    /// not Draft", so an image parked in Revoked could never be approved again — turning a reversible mistake into a
    /// permanent one. This is the guard on that choice.
    /// </summary>
    [Fact]
    public async Task AWithdrawnImage_CanBeApprovedAgain()
    {
        var world = Build();
        try
        {
            await world.SeedApprovedImageAsync("shed", "img-1");
            await world.Service.RevokeImageApprovalAsync("img-1");

            var reapproved = await world.ApproveAsync("img-1");

            Assert.Equal(SceneAssetProductionApprovalStatus.Approved, reapproved.ProductionApprovalStatus);
        }
        finally
        {
            world.Cleanup();
        }
    }

    /// <summary>
    /// Withdrawing something that was never approved is refused rather than accepted as a no-op. An operator who
    /// believed they had taken an image out of production, but had not, would be refused by the delete next — at which
    /// point the reason is no longer obvious.
    /// </summary>
    [Fact]
    public async Task Withdrawing_AnImageThatIsNotApproved_IsRefused()
    {
        var world = Build();
        try
        {
            await world.SeedImageAsync("shed", SceneAssetType.Prop, "img-1");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Service.RevokeImageApprovalAsync("img-1"));

            Assert.Contains("nothing to take out of production", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Cleanup();
        }
    }

    [Fact]
    public async Task Withdrawing_AnImageThatDoesNotExist_IsRefused()
    {
        var world = Build();
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Service.RevokeImageApprovalAsync("no-such-image"));

            Assert.Contains("was not found", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Cleanup();
        }
    }

    /// <summary>
    /// The refusal an operator actually hit. It has to name the action that clears it: a message that only says "not
    /// allowed" is what produced this report, and the image must survive the refusal.
    /// </summary>
    [Fact]
    public async Task DeletingAnApprovedImage_IsRefused_AndNamesTheWayOut()
    {
        var world = Build();
        try
        {
            await world.SeedApprovedImageAsync("shed", "img-1");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Service.DeleteImageAsync("img-1"));

            Assert.Contains("approved for production", error.Message, StringComparison.Ordinal);
            Assert.Contains("Stop using", error.Message, StringComparison.Ordinal);
            Assert.NotNull(await world.Repo.GetImageAsync("img-1"));
        }
        finally
        {
            world.Cleanup();
        }
    }

    /// <summary>
    /// The reported journey end to end: approve by mistake, withdraw the approval, delete. This is the test that would
    /// have caught the missing action.
    /// </summary>
    [Fact]
    public async Task TheReportedJourney_ApproveByMistakeThenWithdrawThenDelete()
    {
        var world = Build();
        try
        {
            await world.SeedImageAsync("shed", SceneAssetType.Location, "img-1");
            await world.Service.SetImageDisplayNameAsync("img-1", "Indoor Front");
            await world.ApproveAsync("img-1");

            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Service.DeleteImageAsync("img-1"));

            await world.Service.RevokeImageApprovalAsync("img-1");
            await world.Service.DeleteImageAsync("img-1");

            Assert.Null(await world.Repo.GetImageAsync("img-1"));
        }
        finally
        {
            world.Cleanup();
        }
    }

    /// <summary>
    /// The withdrawn image can be deleted but is NOT deleted by the withdrawal itself: taking something out of
    /// production and destroying the bytes are two separate decisions, and only the second is irreversible.
    /// </summary>
    [Fact]
    public async Task WithdrawingLeavesTheImageInPlace_ItOnlyLosesItsApproval()
    {
        var world = Build();
        try
        {
            await world.SeedApprovedImageAsync("shed", "img-1");

            await world.Service.RevokeImageApprovalAsync("img-1");

            var stillThere = await world.Repo.GetImageAsync("img-1");
            Assert.NotNull(stillThere);
            Assert.Equal(SceneAssetStatus.Complete, stillThere.Status);
        }
        finally
        {
            world.Cleanup();
        }
    }

    private static World Build() => World.Create();

    private sealed class World
    {
        private World(SceneAssetService service, SceneAssetRepository repo, string dbPath, string root)
        {
            Service = service;
            Repo = repo;
            _dbPath = dbPath;
            _root = root;
        }

        public SceneAssetService Service { get; }
        public SceneAssetRepository Repo { get; }

        private readonly string _dbPath;
        private readonly string _root;

        public static World Create()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"scene-asset-revoke-{Guid.NewGuid():N}.db");
            var root = Path.Combine(Path.GetTempPath(), $"scene-asset-revoke-files-{Guid.NewGuid():N}");
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                SceneImageRoot = root
            });
            var repo = new SceneAssetRepository(options);
            var service = new SceneAssetService(
                repo,
                new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance),
                new UnusedJobQueue(),
                new StubSettingsResolver(),
                TimeProvider.System,
                NullLogger<SceneAssetService>.Instance);
            return new World(service, repo, dbPath, root);
        }

        public async Task<SceneAssetImage> ApproveAsync(string imageId) =>
            await Service.ApproveImageForProductionAsync(
                imageId,
                "{\"source\":\"test\"}",
                SceneAssetConsentState.Confirmed,
                SceneAssetLicenseState.Confirmed,
                "internal",
                SceneAssetApprovedUseScope.Location,
                "general",
                "{}");

        public async Task SeedImageAsync(string assetId, SceneAssetType type, string imageId)
        {
            await Repo.UpsertAsync(new SceneAsset
            {
                Id = assetId,
                Name = assetId,
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete,
                Type = type,
                IsContainerOnly = true
            });
            await Repo.UpsertImageAsync(new SceneAssetImage
            {
                Id = imageId,
                AssetId = assetId,
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = $"assets/{imageId}.png",
                MediaType = "image/png",
                Width = 1024,
                Height = 1024,
                ByteLength = 1024,
                Sha256 = Sha
            });
        }

        /// <summary>A Prop container, so the location naming gate does not stand between the test and its subject.</summary>
        public async Task SeedApprovedImageAsync(string assetId, string imageId)
        {
            await SeedImageAsync(assetId, SceneAssetType.Prop, imageId);
            await ApproveAsync(imageId);
        }

        public void Cleanup()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(_dbPath + suffix); } catch { /* best effort */ }
            }

            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Withdrawal and deletion never enqueue anything, so the queue only satisfies the constructor.</summary>
    private sealed class UnusedJobQueue : IDurableBackgroundJobQueue
    {
        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> TryActivateAsync(string jobId, DateTime activatedUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WaitForWorkAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubSettingsResolver : ISceneBeatAnalyzerResolver
    {
        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var model = new ResolvedModel(
                "https://example.test",
                "/chat",
                30,
                null,
                "model",
                0.2,
                0.8,
                1024,
                "provider",
                false);
            return Task.FromResult(new ResolvedSceneBeatAnalyzer(
                "function",
                "model",
                "provider",
                model,
                StructuredOutputMode.StrictJsonSchema,
                4096,
                1024,
                1,
                120,
                250,
                [5, 30],
                30,
                8));
        }
    }
}
