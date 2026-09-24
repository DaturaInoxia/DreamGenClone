using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Body promotion end to end, against the REAL pack store and the REAL approval contract: the point of these
/// tests is not that the promotion writes ten rows, but that the pack it produces is the one
/// <c>ValidateApprovalSet</c> demands — 5×2 body slots plus an approved unclothed Front as the canonical
/// full-body pointer. A stub pack store could not show that, so the promotion's readiness and the pack's
/// approval gate are exercised through the same database.
///
/// The face half is produced the way production produces it (a face draft with five face references), because a
/// body build holds no face artifacts and a BodyComplete pack is a strict superset of the five face slots.
/// </summary>
public sealed class CharacterIdentityBodyPromotionTests
{
    [Fact]
    public async Task Promote_WritesEveryBodySlot_RaisesTheDraft_AndThePackThenApproves()
    {
        var world = CreateWorld();
        try
        {
            var (draftId, faceFrontAssetId) = await SeedFaceDraftAsync(world);
            var build = await SeedAcceptedBodyBuildAsync(world);

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.True(readiness.Ready);
            Assert.Empty(readiness.BlockingReasons);
            Assert.Equal(CharacterImageIdentityPackScope.BodyComplete, readiness.Scope);
            Assert.Equal(12, readiness.BodyViews.Count);
            Assert.Equal(5, readiness.Views.Count);
            Assert.All(readiness.Views, view => Assert.True(view.Ready));
            Assert.All(readiness.BodyViews, view => Assert.True(view.Ready));

            var promoted = await world.Promotion.PromoteAsync(build.Id);

            Assert.Equal(draftId, promoted.PackId);
            var pack = await world.IdentityRepo.GetPackAsync(draftId);
            Assert.Equal(CharacterImageIdentityPackScope.BodyComplete, pack!.PackScope);

            var bodyAssets = (await world.Identity.ListAssetsAsync(draftId))
                .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.FullBody)
                .ToList();
            Assert.Equal(12, bodyAssets.Count);
            foreach (var state in Enum.GetValues<SceneImageReferenceBodyState>())
            {
                foreach (var view in Enum.GetValues<SceneImageReferenceBodyView>())
                {
                    Assert.Contains(bodyAssets, asset => asset.BodyState == state && asset.BodyView == view);
                }
            }

            // The canonical full-body pointer is the unclothed Front the promotion uploaded, not a guess.
            var canonical = Assert.Single(
                bodyAssets, asset => asset.Id == pack.CanonicalFullBodyAssetId);
            Assert.Equal(SceneImageReferenceBodyState.Unclothed, canonical.BodyState);
            Assert.Equal(SceneImageReferenceBodyView.Front, canonical.BodyView);

            // The pack the promotion produced passes the approval contract the repository owns, once the user has
            // approved the promoted body references (approval is explicit per asset, exactly as for faces).
            await ApproveBodySlotsAsync(world, draftId);
            var approved = await world.Identity.ApprovePackAsync(
                draftId, "{\"descriptor\":\"curvy, full bust\"}", faceFrontAssetId);
            Assert.Equal(CharacterImageIdentityPackStatus.Approved, approved.Status);
            Assert.Equal(CharacterImageIdentityPackScope.BodyComplete, approved.PackScope);

            // The body build's own plan is finished by the promotion — it is the terminal action.
            var finished = await world.Builds.GetBuildAsync(build.Id);
            Assert.Equal(CharacterIdentityBuildStatus.Complete, finished!.Status);
            Assert.Equal(CharacterIdentityBuildStep.Promote, finished.CurrentStep);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_ListsEveryBodySlotThatWasNeverStarted()
    {
        var world = CreateWorld();
        try
        {
            await SeedFaceDraftAsync(world);
            var build = await world.Builds.CreateBuildAsync("becky", null, CharacterIdentityTargetKind.Body);

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.False(readiness.Ready);
            Assert.Equal(12, readiness.BodyViews.Count);
            Assert.All(readiness.BodyViews, view => Assert.False(view.Ready));
            Assert.Equal(12, readiness.BlockingReasons.Count);
            Assert.Contains(readiness.BlockingReasons, reason => reason.StartsWith("Clothed Front:", StringComparison.Ordinal));
            Assert.Contains(readiness.BlockingReasons, reason => reason.StartsWith("Unclothed profile right:", StringComparison.Ordinal));
            Assert.All(readiness.BlockingReasons, reason => Assert.Contains("has not been started", reason, StringComparison.Ordinal));
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_NamesAFaceSlotThatIsMissing_AndOneThatIsNotApproved()
    {
        var world = CreateWorld();
        try
        {
            // Five face references, but the profile-right one is not approved and the profile-left one is absent.
            var (draftId, _) = await SeedFaceDraftAsync(
                world,
                omit: SceneImageReferenceFaceView.ProfileLeft,
                unapproved: [SceneImageReferenceFaceView.ProfileRight]);
            var build = await SeedAcceptedBodyBuildAsync(world);

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.Equal(draftId, (await world.IdentityRepo.GetPackAsync(draftId))!.Id);
            Assert.False(readiness.Ready);
            Assert.Contains(
                "Face slot Profile left: the draft pack has no face reference for this slot.",
                readiness.BlockingReasons);
            Assert.Contains(
                "Face slot Profile right: the face reference is present but not approved.",
                readiness.BlockingReasons);
            Assert.Equal(2, readiness.Views.Count(view => !view.Ready));
            // Every body slot is ready, so the face half is the only thing standing in the way.
            Assert.All(readiness.BodyViews, view => Assert.True(view.Ready));
            Assert.Equal(2, readiness.BlockingReasons.Count);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_WithoutADraftPack_SaysToPromoteTheFaceBuildOrSupersede()
    {
        var world = CreateWorld();
        try
        {
            var build = await SeedAcceptedBodyBuildAsync(world);

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.False(readiness.Ready);
            Assert.Contains(readiness.BlockingReasons, reason =>
                reason.Contains("no identity pack draft", StringComparison.Ordinal)
                && reason.Contains("supersede", StringComparison.OrdinalIgnoreCase));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Promotion.PromoteAsync(build.Id));
            Assert.Contains("no identity pack draft", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_QuotesTheFindingsOfAViewThatIsNotAccepted()
    {
        var world = CreateWorld();
        try
        {
            await SeedFaceDraftAsync(world);
            var build = await world.Builds.CreateBuildAsync("becky", null, CharacterIdentityTargetKind.Body);
            var key = CharacterIdentityBodyViewKey.Canonical(
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);
            await world.Body.UploadAsync(build.Id, key, "clothed-front.png", new MemoryStream(world.Assets.ImageBytes));

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.False(readiness.Ready);
            var front = Assert.Single(readiness.BodyViews, view => view.Label == "Clothed Front");
            Assert.False(front.Ready);
            var reason = Assert.Single(readiness.BlockingReasons, item => item.StartsWith("Clothed Front:", StringComparison.Ordinal));
            Assert.Contains("Complete, not Accepted", reason, StringComparison.Ordinal);
            Assert.Contains("body shape and proportions (not reviewed)", reason, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Promote_IsRefused_WhenASlotIsNotReady_AndWritesNothing()
    {
        var world = CreateWorld();
        try
        {
            var (draftId, _) = await SeedFaceDraftAsync(world);
            var build = await world.Builds.CreateBuildAsync("becky", null, CharacterIdentityTargetKind.Body);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Promotion.PromoteAsync(build.Id));

            Assert.Contains("Unclothed profile right: this request has not been started.", error.Message, StringComparison.Ordinal);
            var draft = await world.IdentityRepo.GetPackAsync(draftId);
            Assert.Equal(CharacterImageIdentityPackScope.FaceOnly, draft!.PackScope);
            Assert.Null(draft.CanonicalFullBodyAssetId);
            Assert.Equal(5, (await world.Identity.ListAssetsAsync(draftId)).Count);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task ACanonicalFullBodyPointerThatIsNotTheUnclothedFront_IsRefusedAtApproval()
    {
        var world = CreateWorld();
        try
        {
            var (draftId, faceFrontAssetId) = await SeedFaceDraftAsync(world);
            var build = await SeedAcceptedBodyBuildAsync(world);
            await world.Promotion.PromoteAsync(build.Id);
            await ApproveBodySlotsAsync(world, draftId);

            // Repoint the draft at a clothed reference, the way a bad pointer would arrive from outside the
            // promotion: the pack store's approval is the authority that must refuse it.
            var assets = await world.Identity.ListAssetsAsync(draftId);
            var clothedFront = Assert.Single(assets, asset =>
                asset.AssetKind == SceneImageReferenceAssetKind.FullBody
                && asset.BodyState == SceneImageReferenceBodyState.Clothed
                && asset.BodyView == SceneImageReferenceBodyView.Front);
            var pack = await world.IdentityRepo.GetPackAsync(draftId);
            pack!.CanonicalFullBodyAssetId = clothedFront.Id;
            await world.IdentityRepo.UpsertDraftAsync(pack);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Identity.ApprovePackAsync(draftId, "{\"descriptor\":\"x\"}", faceFrontAssetId));

            Assert.Contains("unclothed Front", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Supersede_KeepsThePriorApprovedPack_AndCarriesTheBodyAssetsIntoTheNewDraft()
    {
        var world = CreateWorld();
        try
        {
            var (draftId, faceFrontAssetId) = await SeedFaceDraftAsync(world);
            var build = await SeedAcceptedBodyBuildAsync(world);
            await world.Promotion.PromoteAsync(build.Id);
            await ApproveBodySlotsAsync(world, draftId);
            await world.Identity.ApprovePackAsync(draftId, "{\"descriptor\":\"curvy, full bust\"}", faceFrontAssetId);

            var next = await world.Identity.SupersedePackAsync(draftId);

            Assert.Equal(draftId, next.SupersedesId);
            Assert.Equal(CharacterImageIdentityPackStatus.Draft, next.Status);
            Assert.Equal(CharacterImageIdentityPackScope.BodyComplete, next.PackScope);

            // The prior approved pack is preserved: its row survives, superseded, with its assets untouched.
            var prior = await world.IdentityRepo.GetPackAsync(draftId);
            Assert.Equal(CharacterImageIdentityPackStatus.Superseded, prior!.Status);
            // The new draft carries the whole body-complete set forward: 12 body slots (six per state since the back
            // view was added, 2026-09-24) plus the pack's 5 face views.
            Assert.Equal(17, (await world.Identity.ListAssetsAsync(draftId)).Count);
            Assert.Equal(17, (await world.Identity.ListAssetsAsync(next.Id)).Count);

            // The new draft carries the whole body-complete set forward, so it approves without promoting again:
            // B-123 then resolves exactly one approved BodyComplete pack for this character.
            var nextFront = Assert.Single(
                await world.Identity.ListAssetsAsync(next.Id),
                asset => asset.AssetKind == SceneImageReferenceAssetKind.Face
                    && asset.FaceView == SceneImageReferenceFaceView.Front);
            var approvedNext = await world.Identity.ApprovePackAsync(
                next.Id, "{\"descriptor\":\"curvy, full bust\"}", nextFront.Id);
            Assert.Equal(CharacterImageIdentityPackStatus.Approved, approvedNext.Status);
            Assert.Equal(
                CharacterImageIdentityPackScope.BodyComplete,
                (await world.IdentityRepo.GetLatestApprovedPackAsync("becky"))!.PackScope);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// The face half as production builds it: a FaceOnly draft with the five canonical face references, each one
    /// uploaded through the identity service (which is the only writer of pack assets). A healthy face half is
    /// the default — the parameters exist to withhold one slot or one approval for the refusal cases.
    /// </summary>
    private static async Task<(string DraftId, string FaceFrontAssetId)> SeedFaceDraftAsync(
        World world,
        SceneImageReferenceFaceView? omit = null,
        IReadOnlyList<SceneImageReferenceFaceView>? unapproved = null)
    {
        var draft = await world.Identity.CreateDraftPackAsync("becky", CharacterImageIdentityPackScope.FaceOnly);
        string? frontAssetId = null;
        foreach (var view in Enum.GetValues<SceneImageReferenceFaceView>())
        {
            if (view == omit)
                continue;

            await using var content = new MemoryStream(world.Assets.ImageBytes);
            var asset = await world.Identity.UploadAssetAsync(
                draft.Id, SceneImageReferenceAssetKind.Face, $"{view}.png", content, view);
            if (view == SceneImageReferenceFaceView.Front)
                frontAssetId = asset.Id;

            if (unapproved?.Contains(view) != true)
                await world.Identity.SetAssetApprovalAsync(asset.Id, true);
        }

        Assert.NotNull(frontAssetId);
        return (draft.Id, frontAssetId!);
    }

    /// <summary>The user's explicit approval of each promoted body reference, before the pack can be approved.</summary>
    private static async Task ApproveBodySlotsAsync(World world, string packId)
    {
        var bodyAssets = (await world.Identity.ListAssetsAsync(packId))
            .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.FullBody)
            .ToList();
        Assert.Equal(12, bodyAssets.Count);
        foreach (var asset in bodyAssets)
            await world.Identity.SetAssetApprovalAsync(asset.Id, true);
    }

    /// <summary>Every canonical body slot produced, reviewed and accepted, one request at a time.</summary>
    private static async Task<CharacterIdentityBuild> SeedAcceptedBodyBuildAsync(World world)
    {
        var build = await world.Builds.CreateBuildAsync("becky", null, CharacterIdentityTargetKind.Body);
        foreach (var state in Enum.GetValues<SceneImageReferenceBodyState>())
        {
            foreach (var view in Enum.GetValues<SceneImageReferenceBodyView>())
            {
                var key = CharacterIdentityBodyViewKey.Canonical(state, view);
                await world.Body.UploadAsync(
                    build.Id, key, $"{state}-{view}.png", new MemoryStream(world.Assets.ImageBytes));
                await world.Body.RecordFindingsAsync(
                    build.Id,
                    key,
                    CharacterIdentityBodyChecks.All.ToDictionary(
                        check => check, _ => CharacterIdentityBodyCheckVerdict.Pass),
                    reviewer: "reviewer-1");
                await world.Body.AcceptAsync(build.Id, key);
            }
        }

        return (await world.Builds.GetBuildAsync(build.Id))!;
    }

    private static World CreateWorld()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"body-promotion-{Guid.NewGuid():N}.db");
        var root = Path.Combine(Path.GetTempPath(), $"body-promotion-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False",
            SceneImageRoot = root
        });

        var buildRepository = new CharacterIdentityBuildRepository(options);
        buildRepository.EnsureSchemaAsync().GetAwaiter().GetResult();
        var assets = new StubSceneAssets();
        var builds = new CharacterIdentityBuildService(
            buildRepository,
            assets,
            new CharacterIdentityStepPlanService(buildRepository),
            NullLogger<CharacterIdentityBuildService>.Instance);

        // Created BEFORE the body service: the brief factory resolves the character's approved identity pack, so it
        // depends on the same repository the promotion flow writes to.
        var identityRepository = new CharacterImageIdentityRepository(options);
        var storage = new CharacterImageAssetStorageService(options, NullLogger<CharacterImageAssetStorageService>.Instance);
        var identity = new CharacterImageIdentityService(
            identityRepository, storage, new ReferenceImageQualityAnalyzer(), NullLogger<CharacterImageIdentityService>.Instance);

        var bodies = new CharacterIdentityBodyService(
            buildRepository,
            builds,
            new CharacterBodyCardRepository(options),
            new StubTemplates(),
            assets,
            new ReferenceImageQualityAnalyzer(),
            new BodyReferenceBriefFactory(
                new StubCharacterTemplates(), identityRepository, NullLogger<BodyReferenceBriefFactory>.Instance),
            new StubModelResolution(),
            new StubPoseResolver(),
            new StubReferenceStrategies(),
            NullLogger<CharacterIdentityBodyService>.Instance);

        var promotion = new CharacterIdentityPromotionService(
            buildRepository,
            builds,
            identity,
            assets,
            new StubValidation(),
            new StubTemplates(),
            new ReferenceImageQualityAnalyzer(),
            bodies);

        return new World(promotion, bodies, builds, identity, identityRepository, assets, dbPath, root);
    }

    private sealed record World(
        CharacterIdentityPromotionService Promotion,
        CharacterIdentityBodyService Body,
        CharacterIdentityBuildService Builds,
        CharacterImageIdentityService Identity,
        CharacterImageIdentityRepository IdentityRepo,
        StubSceneAssets Assets,
        string DbPath,
        string Root) : IDisposable
    {
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = DbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }

            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>These tests drive the body path; a face-only gate asked here would be a bug, so it throws.</summary>
    private sealed class StubValidation : ICharacterIdentityValidationService
    {
        public Task<CharacterIdentityValidationResult> GetGateAsync(string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityValidationResult> MeasureAsync(string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImageValidation> MeasureImageAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityValidationResult> RecordOverrideAsync(string buildId, string reason, string author, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityValidationResult> AdvanceAsync(string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Nothing in this file resolves a prompt: uploading a view is the acquisition path under test.</summary>
    private sealed class StubTemplates : IImageWorkflowTemplateService
    {
        public Task<ReferenceWorkflowSettings> ResolveSettingsAsync(string? characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ImageWorkflowPromptTemplate> ResolveAsync(string key, string? characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ImageWorkflowPromptTemplate> ResetToSeedAsync(string key, ImageWorkflowPromptTemplateScope scope, string? characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(string? characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveTemplateAsync(ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveSettingsAsync(ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Uploading a view never compiles a prompt, so the character template store is never asked.</summary>
    private sealed class StubCharacterTemplates : DreamGenClone.Application.Templates.ITemplateService
    {
        public Task<TemplateDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TemplateDefinition>> GetAllAsync(
            TemplateType? templateType = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TemplateDefinition> SaveAsync(
            TemplateDefinition template, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateImagePathAsync(Guid id, string imagePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Uploading a view never resolves a model, so the model registry is never asked.</summary>
    private sealed class StubModelResolution : DreamGenClone.Application.ModelManager.IModelResolutionService
    {
        public Task<ResolvedModel> ResolveAsync(
            AppFunction function, string? sessionModelId = null, double? sessionTemperature = null,
            double? sessionTopP = null, int? sessionMaxTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedModel> ResolveImagePromptModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedImageModel> ResolveImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(
            string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(
            string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(
            bool identityCapableOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The promotion and upload flows never pose-condition anything, so the pose resolver is never asked — it throws
    /// rather than answering, so an accidental request from these paths shows up instead of passing silently.
    /// </summary>
    /// <summary>
    /// The identity-STRATEGY declarations this world resolves against: the capability check asks the same
    /// declaration + qualification contract the render path asks.
    /// </summary>
    private sealed class StubReferenceStrategies : IReferenceStrategyResolver
    {
        public Task<ReferenceStrategyResolution> ResolveAsync(
            string modelId, string strategy, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReferenceStrategyResolution(
                strategy == ReferenceStrategyResolver.IdentityReferenceConditioning
                    ? ReferenceStrategyResolutionStatus.Possible
                    : ReferenceStrategyResolutionStatus.Unqualified,
                strategy,
                $"'{strategy}' resolution for model '{modelId}'."));
    }

    private sealed class StubPoseResolver : IPoseImageModelResolver
    {
        public Task<ResolvedPoseImageModel> ResolveAsync(
            string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The asset library with real PNG bytes per image and a stored file path, so the promotion's download step
    /// and the identity service's quality analyzer see genuine content.
    /// </summary>
    private sealed class StubSceneAssets : ISceneAssetService
    {
        private int _counter;

        public List<SceneAssetImage> All { get; } = [];

        public byte[] ImageBytes { get; } = CreatePng();

        public Task<SceneAsset> CreateAssetAsync(
            string name, SceneAssetType type, string? characterProfileId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SceneAsset { Id = $"container-{++_counter}", Name = name, Type = type });

        public Task<SceneAssetImage> AddUploadedImageAsync(
            string assetId, string fileName, Stream content, CancellationToken cancellationToken = default,
            string? candidateBatchId = null)
            => Task.FromResult(Add(assetId, candidateBatchId));

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult(All.FirstOrDefault(image => image.Id == imageId));

        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneAsset?>(new SceneAsset { Id = assetId });

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                All.Where(image => string.Equals(image.AssetId, assetId, StringComparison.Ordinal)).ToList());

        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(
            string imageId, CancellationToken cancellationToken = default)
        {
            var image = All.FirstOrDefault(candidate => candidate.Id == imageId)
                ?? throw new InvalidOperationException($"Test image '{imageId}' was not seeded.");
            image.Width = 1024;
            image.Height = 1536;
            image.ByteLength = ImageBytes.Length;
            return Task.FromResult<(SceneAsset, SceneAssetImage, Stream)>(
                (new SceneAsset { Id = image.AssetId }, image, new MemoryStream(ImageBytes)));
        }

        private SceneAssetImage Add(string assetId, string? candidateBatchId)
        {
            var id = $"image-{++_counter}";
            var image = new SceneAssetImage
            {
                Id = id,
                AssetId = assetId,
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = $"{id}.png",
                CandidateBatchId = candidateBatchId,
                Sha256 = $"sha-{_counter}"
            };
            All.Add(image);
            return image;
        }

        private static byte[] CreatePng()
        {
            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                64, 96, new SixLabors.ImageSharp.PixelFormats.Rgba32(120, 90, 80));
            using var buffer = new MemoryStream();
            image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            return buffer.ToArray();
        }

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            // The acceptance path reads this view's deck to take the image the operator APPROVED. This double holds
            // every image it created, so the lookup is a filter rather than a refusal.
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                All.Where(image => string.Equals(image.CandidateBatchId, candidateBatchId, StringComparison.Ordinal)).ToList());

        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddDerivedImageAsync(string assetId, string sourceImageId, MediaEditOperationKind operation, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null)
            => throw new NotSupportedException();

        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImageValidationResultAsync(string imageId, string? validationResultJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> ApproveImageForProductionAsync(string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> CreateFromPromptAsync(string name, string prompt, SceneAssetType type, string modelId, string imageSize, string? candidateBatchId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> CreateFromUploadAsync(string name, SceneAssetType type, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> EnqueueEditAsync(string sourceAssetId, string name, string editPrompt, string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnqueueProfilePackAsync(SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(string identityPackId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
