using System.Text.Json;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Promotion readiness as the three configured gates actually behave. Two of them are re-checks of evidence
/// the build already produced (the Validate gate and the accepted attempts' recorded direction), and one is a
/// real measurement (the sharpness floor). The sharpness numbers here are genuine: the tests use the real
/// <see cref="ReferenceImageQualityAnalyzer"/> over a noisy image (sharp) and a flat one (blurry).
/// </summary>
public sealed class CharacterIdentityPromotionGateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Readiness_IsReady_WhenEveryViewClearsTheDirectionAndSharpnessGates()
    {
        var world = CreateWorld();
        try
        {
            var build = await SeedPromotableBuildAsync(world);

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.True(readiness.Ready);
            Assert.Empty(readiness.BlockingReasons);
            Assert.Equal(5, readiness.Views.Count);
            Assert.All(readiness.Views, view => Assert.True(view.Ready));
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_BlocksABlurryFront_WithTheMeasuredSharpness_AndTheConfiguredFloor()
    {
        var world = CreateWorld();
        try
        {
            var build = await SeedPromotableBuildAsync(world, frontSharpness: Sharpness.Blurry);

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.False(readiness.Ready);
            var reason = Assert.Single(readiness.BlockingReasons);
            Assert.StartsWith("Front:", reason, StringComparison.Ordinal);
            Assert.Contains("sharpness 0", reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("250", reason, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_ReChecksTheAcceptedAttemptsRecordedDirection_AndBlocksAnUnprovenView()
    {
        var world = CreateWorld();
        try
        {
            // The accepted 3/4 right render was measured facing the wrong way, and no override was recorded:
            // promotion must not be a way around the direction gate.
            var build = await SeedPromotableBuildAsync(
                world, wrongDirectionView: CharacterIdentityAngleView.ThreeQuarterRight);

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.False(readiness.Ready);
            var reason = Assert.Single(readiness.BlockingReasons);
            Assert.StartsWith("Three-quarter right:", reason, StringComparison.Ordinal);
            Assert.Contains("faces image-left", reason, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_AcceptsARecordedManualOverride_InsteadOfTheDirectionGate()
    {
        var world = CreateWorld();
        try
        {
            var build = await SeedPromotableBuildAsync(
                world,
                wrongDirectionView: CharacterIdentityAngleView.ThreeQuarterRight,
                overrideRecorded: true);

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.True(readiness.Ready);
            Assert.Empty(readiness.BlockingReasons);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_BlocksWhenTheValidateGateCannotAdvance_AndQuotesItsReason()
    {
        var world = CreateWorld();
        try
        {
            var build = await SeedPromotableBuildAsync(world);
            world.Validation.Result = new CharacterIdentityValidationResult
            {
                BuildId = build.Id,
                Verdict = CharacterIdentityValidationVerdict.Fail,
                ThresholdPercent = 1.5,
                CanAdvance = false,
                BlockReason = "Measured iris offset 4.20% exceeds the configured 1.50% (no override recorded)."
            };

            var readiness = await world.Promotion.GetReadinessAsync(build.Id);

            Assert.False(readiness.Ready);
            var reason = Assert.Single(readiness.BlockingReasons);
            Assert.StartsWith("Validate:", reason, StringComparison.Ordinal);
            Assert.Contains("4.20%", reason, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Readiness_FailsFast_WhenTheConfiguredSharpnessFloorIsNotPositive()
    {
        var world = CreateWorld(settings => settings.QualityGateMinSharpness = 0);
        try
        {
            var build = await SeedPromotableBuildAsync(world);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Promotion.GetReadinessAsync(build.Id));

            Assert.Contains("QualityGateMinSharpness", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Promote_UploadsEveryViewWithItsOwnFaceViewTag_AndRecordsTheProducedPack()
    {
        var world = CreateWorld();
        try
        {
            var build = await SeedPromotableBuildAsync(world);

            var result = await world.Promotion.PromoteAsync(build.Id);

            Assert.Equal("pack-1", result.PackId);
            Assert.Equal(
                new SceneImageReferenceFaceView?[]
                {
                    SceneImageReferenceFaceView.Front,
                    SceneImageReferenceFaceView.ThreeQuarterLeft,
                    SceneImageReferenceFaceView.ThreeQuarterRight,
                    SceneImageReferenceFaceView.ProfileLeft,
                    SceneImageReferenceFaceView.ProfileRight
                },
                world.Identity.Uploads.Select(upload => upload.FaceView).ToArray());
            Assert.All(world.Identity.Uploads, upload => Assert.Equal(SceneImageReferenceAssetKind.Face, upload.Kind));
            Assert.Equal(5, world.Identity.Provenance.Count);
            Assert.Equal("pack-1", (await world.Builds.GetBuildAsync(build.Id))!.ProducedIdentityPackId);
            // A face build promotes into a FaceOnly draft; raising it to body-complete is the body promotion's job.
            Assert.Equal(CharacterImageIdentityPackScope.FaceOnly, world.Identity.CreatedScope);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>Solid colour: variance of the Laplacian is 0, so a real quality gate must reject it.</summary>
    private static byte[] Blurry(int size) => Solid(size, new Rgba32(128, 128, 128));

    /// <summary>Per-pixel noise: high variance of the Laplacian, so the real analyzer calls it sharp.</summary>
    private static byte[] Sharp(int size)
    {
        using var image = new Image<Rgba32>(size, size);
        var random = new Random(20260921);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    private static byte[] Solid(int size, Rgba32 colour)
    {
        using var image = new Image<Rgba32>(size, size, colour);
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    private enum Sharpness
    {
        Sharp,
        Blurry
    }

    /// <summary>
    /// A build that has done everything the user must do: a canonical front, four accepted angles each with a
    /// recorded measurement, the Angles step complete and the Validate gate clear. Every gate then decides.
    /// </summary>
    private static async Task<CharacterIdentityBuild> SeedPromotableBuildAsync(
        World world,
        Sharpness frontSharpness = Sharpness.Sharp,
        Sharpness angleSharpness = Sharpness.Sharp,
        CharacterIdentityAngleView? wrongDirectionView = null,
        bool overrideRecorded = false)
    {
        world.Assets.Images.Add(new SceneAssetImage
        {
            Id = "front-candidate",
            AssetId = "front-container",
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Complete,
            FileRelativePath = world.Assets.Write("front-candidate", frontSharpness),
            Sha256 = "sha-front-candidate"
        });

        var build = await world.Builds.CreateBuildAsync("char-1", null, CharacterIdentityTargetKind.Face);
        build = await world.Builds.CompleteStepAsync(
            build.Id, CharacterIdentityBuildStep.Front, null, "front-candidate");
        build = await world.Builds.CompleteStepAsync(
            build.Id, CharacterIdentityBuildStep.Validate, "front-candidate", "front-candidate");
        build = await world.Builds.SetFrontContainerAsync(build.Id, "front-container");
        build = await world.Builds.SetCanonicalFrontAsync(build.Id, "front-candidate");

        foreach (var view in new[]
                 {
                     CharacterIdentityAngleView.ThreeQuarterLeft,
                     CharacterIdentityAngleView.ThreeQuarterRight,
                     CharacterIdentityAngleView.ProfileLeft,
                     CharacterIdentityAngleView.ProfileRight
                 })
        {
            var artifactId = $"angle-{view}";
            world.Assets.Images.Add(new SceneAssetImage
            {
                Id = artifactId,
                AssetId = "front-container",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = world.Assets.Write(artifactId, angleSharpness),
                Sha256 = $"sha-{artifactId}"
            });

            // The direction convention: left views need a negative nose offset, right views a positive one.
            var offset = view is CharacterIdentityAngleView.ThreeQuarterLeft or CharacterIdentityAngleView.ProfileLeft
                ? -45.0
                : 45.0;
            var wrong = wrongDirectionView == view;
            if (wrong) offset = -offset;

            var record = new CharacterIdentityAngleRecord
            {
                BuildId = build.Id,
                View = view,
                Status = CharacterIdentityAngleStatus.Accepted,
                InputArtifactId = "front-candidate",
                OutputArtifactId = artifactId,
                ManualConfirmationRequired = view is CharacterIdentityAngleView.ProfileLeft or CharacterIdentityAngleView.ProfileRight,
                ManualConfirmed = view is CharacterIdentityAngleView.ProfileLeft or CharacterIdentityAngleView.ProfileRight
            };
            await world.Repository.UpsertAngleAsync(record);

            var attempt = new CharacterIdentityAngleAttempt
            {
                AngleId = record.Id,
                AttemptNumber = 1,
                InputArtifactId = "front-candidate",
                OutputArtifactId = artifactId,
                Status = CharacterIdentityAngleStatus.Complete,
                ManualOverrideApplied = wrong && overrideRecorded,
                MeasurementJson = JsonSerializer.Serialize(
                    new CharacterIdentityEyeMeasurement { NoseOffsetPercent = offset }, JsonOptions)
            };
            await world.Repository.UpsertAngleAttemptAsync(attempt);

            record.AcceptedAttemptId = attempt.Id;
            await world.Repository.UpsertAngleAsync(record);
        }

        await world.Builds.CompleteStepAsync(
            build.Id, CharacterIdentityBuildStep.Angles, "front-candidate", "angle-ProfileRight");
        return await world.Builds.GetBuildAsync(build.Id) ?? build;
    }

    private static World CreateWorld(Action<ReferenceWorkflowSettings>? configure = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"identity-promote-{Guid.NewGuid():N}.db");
        var root = Path.Combine(Path.GetTempPath(), $"identity-promote-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False",
            SceneImageRoot = root
        });
        var repository = new CharacterIdentityBuildRepository(options);
        repository.EnsureSchemaAsync().GetAwaiter().GetResult();
        var assets = new StubSceneAssetService(root);
        var builds = new CharacterIdentityBuildService(
            repository,
            assets,
            new CharacterIdentityStepPlanService(repository),
            NullLogger<CharacterIdentityBuildService>.Instance);

        var settings = new ReferenceWorkflowSettings();
        configure?.Invoke(settings);
        var validation = new StubValidation();
        var identity = new StubIdentity();
        var promotion = new CharacterIdentityPromotionService(
            repository,
            builds,
            identity,
            assets,
            validation,
            new StubTemplates(settings),
            new ReferenceImageQualityAnalyzer(),
            new StubBodies());

        return new World(promotion, builds, repository, assets, validation, identity, dbPath, root);
    }

    /// <summary>
    /// A face build's promotion must never touch the body service — every member throws, so a face path that
    /// reached it would fail loudly instead of quietly inventing a body slot.
    /// </summary>
    private sealed class StubBodies : ICharacterIdentityBodyService
    {
        public Task<SceneAsset> EnsureContainerAsync(string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterBodyViewSettings> ResolveViewSettingsAsync(
            string characterId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveViewSettingsAsync(
            string characterId, string modelId, string imageSize, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CharacterIdentityBodyView>> ListViewsAsync(string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView?> GetViewAsync(string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterBodyCard?> GetBodyCardAsync(string characterId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterBodyCard> SaveBodyCardAsync(CharacterBodyCard card, int expectedVersion, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> ResolvePromptAsync(string buildId, CharacterIdentityBodyViewKey key, string modelId, string characterName, string? promptOverride = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> ResolveEditInstructionAsync(string buildId, CharacterIdentityBodyViewKey key, string characterName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> GenerateAsync(string buildId, CharacterIdentityBodyViewKey key, string modelId, string imageSize, string characterName, string? promptOverride = null, SceneAssetPoseConditioning? pose = null, bool useIdentity = false, string? identityFaceAssetId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BodyIdentityAvailability> ResolveIdentityAvailabilityAsync(string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BodyPoseAvailability> ResolvePoseAvailabilityAsync(string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> EditFromAcceptedSourceAsync(string buildId, CharacterIdentityBodyViewKey key, string modelId, string characterName, string? promptOverride = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> RecordSourceAsResultAsync(string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> UploadAsync(string buildId, CharacterIdentityBodyViewKey key, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> RecordResultAsync(string buildId, CharacterIdentityBodyViewKey key, string outputArtifactId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> AcceptAsync(string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> AcceptCandidateAsync(string buildId, CharacterIdentityBodyViewKey key, string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> RecordOverrideAsync(string buildId, CharacterIdentityBodyViewKey key, string reason, string author, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> RecordFindingsAsync(string buildId, CharacterIdentityBodyViewKey key, IReadOnlyDictionary<CharacterIdentityBodyCheck, CharacterIdentityBodyCheckVerdict> verdicts, string reviewer, string? note = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityBodyView> AnalyzeQualityAsync(string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed record World(
        CharacterIdentityPromotionService Promotion,
        CharacterIdentityBuildService Builds,
        CharacterIdentityBuildRepository Repository,
        StubSceneAssetService Assets,
        StubValidation Validation,
        StubIdentity Identity,
        string DbPath,
        string Root)
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

    /// <summary>The eye gate as the promotion service asks it: one configured verdict per test.</summary>
    private sealed class StubValidation : ICharacterIdentityValidationService
    {
        public CharacterIdentityValidationResult Result { get; set; } = new()
        {
            Verdict = CharacterIdentityValidationVerdict.Pass,
            ThresholdPercent = 1.5,
            CanAdvance = true
        };

        public Task<CharacterIdentityValidationResult> GetGateAsync(
            string buildId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<CharacterIdentityValidationResult> MeasureAsync(
            string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImageValidation> MeasureImageAsync(
            string imageId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityValidationResult> RecordOverrideAsync(
            string buildId, string reason, string author, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterIdentityValidationResult> AdvanceAsync(
            string buildId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Persisted settings, resolved the way the service resolves them (global row).</summary>
    private sealed class StubTemplates : IImageWorkflowTemplateService
    {
        public StubTemplates(ReferenceWorkflowSettings settings) => Settings = settings;

        public ReferenceWorkflowSettings Settings { get; }

        public Task<ReferenceWorkflowSettings> ResolveSettingsAsync(
            string? characterProfileId, CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        public Task<ImageWorkflowPromptTemplate> ResolveAsync(
            string key, string? characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ImageWorkflowPromptTemplate> ResetToSeedAsync(
            string key, ImageWorkflowPromptTemplateScope scope, string? characterProfileId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(
            string? characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveTemplateAsync(
            ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveSettingsAsync(
            ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The pack store as promotion uses it: it records what was uploaded, so the test can prove each view
    /// arrived with its OWN face-view tag (D7 — promotion must never hardcode <c>Front</c>).
    /// </summary>
    private sealed class StubIdentity : ICharacterImageIdentityService
    {
        public CharacterImageIdentityPack Pack { get; } = new() { Id = "pack-1", CharacterTemplateId = "char-1" };

        public List<(string FileName, SceneImageReferenceAssetKind Kind, SceneImageReferenceFaceView? FaceView)> Uploads { get; } = [];

        public List<(string AssetId, string SourceLabel, SceneImageReferenceConsentState ConsentState)> Provenance { get; } = [];

        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(string characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult<CharacterImageIdentityPack?>(null);

        public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> CreateDraftPackAsync(string characterProfileId, CharacterImageIdentityPackScope scope, CancellationToken cancellationToken = default)
        {
            CreatedScope = scope;
            return Task.FromResult(Pack);
        }

        public CharacterImageIdentityPackScope? CreatedScope { get; private set; }

        public Task<CharacterImageIdentityPack> SetDraftPackScopeAsync(string packId, CharacterImageIdentityPackScope scope, string? canonicalFullBodyAssetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> ApprovePackAsync(string packId, string descriptorSnapshotJson, string canonicalFaceAssetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> SupersedePackAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageReferenceAsset> UploadAssetAsync(string packId, SceneImageReferenceAssetKind kind, string fileName, Stream content, SceneImageReferenceFaceView? faceView = null, SceneImageReferenceBodyView? bodyView = null, SceneImageReferenceBodyState? bodyState = null, CancellationToken cancellationToken = default)
        {
            Uploads.Add((fileName, kind, faceView));
            return Task.FromResult(new SceneImageReferenceAsset
            {
                Id = $"asset-{Uploads.Count}",
                IdentityPackId = packId,
                AssetKind = kind,
                FaceView = faceView,
                BodyView = bodyView,
                BodyState = bodyState
            });
        }

        /// <summary>
        /// The slot writer the promotion uses: it records the same uploads, and reports the slot as empty because
        /// this stub holds no assets — a promotion into an empty pack replaces nothing.
        /// </summary>
        public async Task<SceneImageReferenceSlotWrite> ReplaceSlotAssetAsync(string packId, SceneImageReferenceAssetKind kind, string fileName, Stream content, SceneImageReferenceFaceView? faceView = null, SceneImageReferenceBodyView? bodyView = null, SceneImageReferenceBodyState? bodyState = null, CancellationToken cancellationToken = default)
        {
            var asset = await UploadAssetAsync(
                packId, kind, fileName, content, faceView, bodyView, bodyState, cancellationToken);
            return new SceneImageReferenceSlotWrite(asset, 0);
        }

        public Task SetAssetProvenanceAsync(string assetId, string sourceLabel, SceneImageReferenceConsentState consentState, CancellationToken cancellationToken = default)
        {
            Provenance.Add((assetId, sourceLabel, consentState));
            return Task.CompletedTask;
        }

        public Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetAssetQualityAsync(string assetId, SceneImageReferenceQuality quality, string qualityNotes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageReferenceAsset> AnalyzeAssetQualityAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The asset library as promotion reads it. Images are written to the real scene-image root, so the
    /// sharpness gate measures genuine bytes.
    /// </summary>
    private sealed class StubSceneAssetService : ISceneAssetService
    {
        private readonly string _root;

        public StubSceneAssetService(string root) => _root = root;

        public List<SceneAssetImage> Images { get; } = [];

        public string Write(string id, Sharpness sharpness)
        {
            var relativePath = $"{id}.png";
            File.WriteAllBytes(
                Path.Combine(_root, relativePath),
                sharpness == Sharpness.Blurry ? Blurry(256) : Sharp(256));
            return relativePath;
        }

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult(Images.FirstOrDefault(i => i.Id == imageId));

        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(
            string imageId, CancellationToken cancellationToken = default)
        {
            var image = Images.FirstOrDefault(i => i.Id == imageId)
                ?? throw new InvalidOperationException($"Test image '{imageId}' was not seeded.");
            return Task.FromResult<(SceneAsset, SceneAssetImage, Stream)>((
                new SceneAsset { Id = image.AssetId },
                image,
                File.OpenRead(Path.Combine(_root, image.FileRelativePath!))));
        }

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                Images.Where(i => string.Equals(i.AssetId, assetId, StringComparison.Ordinal)).ToList());

        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddDerivedImageAsync(string assetId, string sourceImageId, MediaEditOperationKind operation, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
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

        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
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
