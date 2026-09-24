using System.Text.Json;
using DreamGenClone.Application.RolePlay;
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
/// The direction gate as the angle service actually runs it: measure a produced angle image, keep the
/// measurement as evidence, mirror a wrong-facing render when that view's remedy is configured, and block
/// it with the measured reason when it is not.
///
/// The mirror is asserted on the BYTES (the derived file must be the horizontal flip of the source), not on
/// a flag, because the whole point of the remedy is that the same render is turned, not re-rolled.
/// </summary>
public sealed class CharacterIdentityAnglesDirectionGateTests
{
    private const int SourceWidth = 40;
    private const int SourceHeight = 24;

    private static readonly Rgba32 Left = new(220, 30, 30);
    private static readonly Rgba32 Right = new(30, 30, 220);
    private static readonly Rgba32 Bottom = new(40, 200, 40);

    [Fact]
    public async Task WrongFacingThreeQuarter_WithTheConfiguredRemedy_IsMirroredIntoANewMirrorDerivedAttempt()
    {
        var world = CreateWorld(settings => settings.DeriveByMirrorThreeQuarterLeft = true);
        try
        {
            var build = await ReachAnglesAsync(world);
            world.Measurements.Measure = _ => Nose(30.5);   // faces image-right; 3/4 left needs image-left
            var source = AsymmetricPng(40, 24);

            var record = await world.Angles.UploadAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft, "shot.png", new MemoryStream(source));

            Assert.Equal(CharacterIdentityAngleStatus.Complete, record.Status);
            Assert.True(record.MirrorDerived);
            Assert.Null(record.FailureReason);

            // The remedy wrote its own image, tagged as the mirror operation, into this view's candidate batch.
            var derived = Assert.Single(world.Assets.Derived);
            Assert.Equal(MediaEditOperationKind.Mirror, derived.Operation);
            Assert.Equal("ThreeQuarterLeft.png", derived.FileName);
            Assert.Equal(
                CharacterIdentityAnglesService.CandidateBatchIdFor(build.Id, CharacterIdentityAngleView.ThreeQuarterLeft),
                derived.CandidateBatchId);
            Assert.Equal(record.OutputArtifactId, derived.Image.Id);
            Assert.Equal(derived.Image.AssetId, world.Assets.Images.Single(i => i.Id == record.InputArtifactId).AssetId);

            // The mirrored bytes really are the source flipped — and flipped the RIGHT way. The source is red on
            // the left, blue on the right and green along the bottom; after the remedy, x=0 must carry the blue
            // half and the green band must still be at the bottom. A vertical flip (or no flip at all) fails this.
            Assert.Equal(Left, Pixel(source, 0, 0));
            Assert.Equal(Right, Pixel(source, SourceWidth - 1, 0));
            Assert.Equal(Bottom, Pixel(source, 0, SourceHeight - 1));
            Assert.Equal(Right, Pixel(derived.Bytes, 0, 0));
            Assert.Equal(Left, Pixel(derived.Bytes, SourceWidth - 1, 0));
            Assert.Equal(Bottom, Pixel(derived.Bytes, 0, SourceHeight - 1));

            // The wrong render stays as evidence, failed with the measured reason; the mirror is its own attempt.
            var attempts = await world.Angles.ListAttemptsAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft);
            Assert.Equal(2, attempts.Count);
            var rejected = attempts.Single(a => !a.MirrorDerived);
            Assert.Equal(CharacterIdentityAngleStatus.Failed, rejected.Status);
            Assert.Contains("image-left", rejected.FailureReason!, StringComparison.Ordinal);
            Assert.Contains("30.50", rejected.FailureReason!, StringComparison.Ordinal);
            Assert.Contains("30.5", rejected.MeasurementJson!, StringComparison.Ordinal);

            var mirrored = attempts.Single(a => a.MirrorDerived);
            Assert.Equal(CharacterIdentityAngleStatus.Complete, mirrored.Status);
            Assert.Equal(record.OutputArtifactId, mirrored.OutputArtifactId);
            Assert.Equal(record.InputArtifactId, mirrored.InputArtifactId);
            Assert.Equal(2, mirrored.AttemptNumber);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task WrongFacingThreeQuarter_WithTheRemedyDisabled_IsBlockedWithTheMeasuredReason_AndNoImageIsWritten()
    {
        var world = CreateWorld(settings => settings.DeriveByMirrorThreeQuarterLeft = false);
        try
        {
            var build = await ReachAnglesAsync(world);
            world.Measurements.Measure = _ => Nose(30.5);

            var record = await world.Angles.UploadAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft, "shot.png",
                new MemoryStream(AsymmetricPng(40, 24)));

            Assert.Equal(CharacterIdentityAngleStatus.Failed, record.Status);
            Assert.False(record.MirrorDerived);
            Assert.Contains("faces image-right", record.FailureReason!, StringComparison.Ordinal);
            Assert.Contains("needs the nose toward image-left", record.FailureReason!, StringComparison.Ordinal);
            Assert.Empty(world.Assets.Derived);

            var attempt = Assert.Single(await world.Angles.ListAttemptsAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft));
            Assert.Equal(CharacterIdentityAngleStatus.Failed, attempt.Status);
            Assert.Equal(record.FailureReason, attempt.FailureReason);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task CorrectFacingThreeQuarter_KeepsTheMeasurementAsEvidence_AndWritesNoSecondImage()
    {
        var world = CreateWorld(_ => { });
        try
        {
            var build = await ReachAnglesAsync(world);
            world.Measurements.Measure = _ => Nose(-45.12);

            var record = await world.Angles.UploadAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft, "shot.png",
                new MemoryStream(AsymmetricPng(40, 24)));

            Assert.Equal(CharacterIdentityAngleStatus.Complete, record.Status);
            Assert.False(record.MirrorDerived);
            Assert.Empty(world.Assets.Derived);

            var attempt = Assert.Single(await world.Angles.ListAttemptsAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft));
            Assert.Equal(CharacterIdentityAngleStatus.Complete, attempt.Status);
            Assert.Contains("-45.12", attempt.MeasurementJson!, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task QueuedRender_GoesThroughTheSameGate_WhenItsResultIsRecorded()
    {
        // The render job's completion path must not be a way around the gate.
        var world = CreateWorld(settings => settings.DeriveByMirrorThreeQuarterLeft = false);
        try
        {
            var build = await ReachAnglesAsync(world);
            world.Measurements.Measure = _ => Nose(-40);
            await world.Angles.UploadAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft, "shot.png",
                new MemoryStream(AsymmetricPng(40, 24)));

            var queued = world.Assets.AddCompleteImage("queued-render");
            world.Measurements.Measure = _ => Nose(35);

            var record = await world.Angles.RecordResultAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft, queued.Id);

            Assert.Equal(CharacterIdentityAngleStatus.Failed, record.Status);
            Assert.Contains("image-left", record.FailureReason!, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Profile_IsNotAsserted_SoItsDirectionNeedsTheUsersConfirmationToAccept()
    {
        var world = CreateWorld(_ => { });
        try
        {
            var build = await ReachAnglesAsync(world);

            world.Measurements.Measure = _ => Nose(-42);
            await world.Angles.UploadAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft, "three-quarter.png",
                new MemoryStream(AsymmetricPng(SourceWidth, SourceHeight)));
            var accepted = Assert.Single(await world.Angles.ListAttemptsAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft));
            await world.Angles.AcceptAttemptAsync(
                build.Id, CharacterIdentityAngleView.ThreeQuarterLeft, accepted.Id);
            Assert.Equal(
                CharacterIdentityAngleStatus.Accepted,
                (await world.Angles.ListAsync(build.Id))
                    .Single(a => a.View == CharacterIdentityAngleView.ThreeQuarterLeft).Status);

            // A profile measures as "no face mesh" (the real tool's answer), which is evidence, not a block.
            world.Measurements.Measure = _ => new CharacterIdentityEyeMeasurement { Error = "no face mesh" };
            var profile = await world.Angles.UploadAsync(
                build.Id, CharacterIdentityAngleView.ProfileLeft, "profile.png",
                new MemoryStream(AsymmetricPng(SourceWidth, SourceHeight)));
            Assert.Equal(CharacterIdentityAngleStatus.Complete, profile.Status);
            Assert.True(profile.ManualConfirmationRequired);

            var profileAttempt = Assert.Single(await world.Angles.ListAttemptsAsync(
                build.Id, CharacterIdentityAngleView.ProfileLeft));
            var profileAfter = (await world.Angles.ListAsync(build.Id))
                .Single(a => a.View == CharacterIdentityAngleView.ProfileLeft);
            Assert.Equal(CharacterIdentityAngleStatus.Complete, profileAfter.Status);
            Assert.Contains("no face mesh", profileAttempt.MeasurementJson!, StringComparison.OrdinalIgnoreCase);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Angles.AcceptAttemptAsync(
                build.Id, CharacterIdentityAngleView.ProfileLeft, profileAttempt.Id));
            Assert.Contains("explicit visual confirmation", error.Message, StringComparison.Ordinal);

            var confirmed = await world.Angles.AcceptAttemptAsync(
                build.Id, CharacterIdentityAngleView.ProfileLeft, profileAttempt.Id, manualConfirmed: true);
            Assert.Equal(CharacterIdentityAngleStatus.Accepted, confirmed.Status);
            Assert.True(confirmed.ManualConfirmed);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>A measurement with only the nose offset — the gate's single input.</summary>
    private static CharacterIdentityEyeMeasurement Nose(double offset)
        => new() { NoseOffsetPercent = offset };

    /// <summary>
    /// Red on the left half, blue on the right half, green along the bottom quarter: the remedy's result is
    /// only correct when the red/blue halves swap AND the green band stays at the bottom.
    /// </summary>
    private static byte[] AsymmetricPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = y >= accessor.Height - accessor.Height / 4
                        ? Bottom
                        : x < row.Length / 2 ? Left : Right;
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    private static Rgba32 Pixel(byte[] png, int x, int y)
    {
        using var image = Image.Load<Rgba32>(png);
        return image[x, y];
    }

    /// <summary>Drives the build to the point the angle panel starts from: a build at Angles with a canonical front.</summary>
    private static async Task<CharacterIdentityBuild> ReachAnglesAsync(World world)
    {
        world.Assets.Images.Add(new SceneAssetImage
        {
            Id = "front-candidate",
            AssetId = "front-container",
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Complete,
            Sha256 = "sha-front-candidate"
        });

        var build = await world.Builds.CreateBuildAsync("char-1", null, CharacterIdentityTargetKind.Face);
        build = await world.Builds.CompleteStepAsync(
            build.Id, CharacterIdentityBuildStep.Front, null, "front-candidate");
        build = await world.Builds.CompleteStepAsync(
            build.Id, CharacterIdentityBuildStep.Validate, "front-candidate", "front-candidate");
        build = await world.Builds.SetFrontContainerAsync(build.Id, "front-container");
        build = await world.Builds.SetCanonicalFrontAsync(build.Id, "front-candidate");

        Assert.Equal(CharacterIdentityBuildStep.Angles, build.CurrentStep);
        return build;
    }

    private static World CreateWorld(Action<ReferenceWorkflowSettings> configure)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"identity-angles-{Guid.NewGuid():N}.db");
        var root = Path.Combine(Path.GetTempPath(), $"identity-angles-images-{Guid.NewGuid():N}");
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
        configure(settings);
        var measurements = new StubMeasurements();
        var angles = new CharacterIdentityAnglesService(
            repository,
            builds,
            assets,
            new StubTemplates(settings),
            measurements,
            new ImageMirrorEngine(),
            options);

        return new World(angles, builds, assets, measurements, dbPath, root);
    }

    /// <summary>Every collaborator a test needs, plus the temp paths it cleans up afterwards.</summary>
    private sealed record World(
        CharacterIdentityAnglesService Angles,
        CharacterIdentityBuildService Builds,
        StubSceneAssetService Assets,
        StubMeasurements Measurements,
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

    /// <summary>Persisted settings, resolved the way the service resolves them (global row).</summary>
    private sealed class StubTemplates : IImageWorkflowTemplateService
    {
        public StubTemplates(ReferenceWorkflowSettings settings) => Settings = settings;

        public ReferenceWorkflowSettings Settings { get; set; }

        public Task<ReferenceWorkflowSettings> ResolveSettingsAsync(
            string? characterProfileId, CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        public Task<ImageWorkflowPromptTemplate> ResolveAsync(
            string key, string? characterProfileId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageWorkflowPromptTemplate { Key = key, Body = $"{key} prompt" });

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

    /// <summary>The measurement tool, stubbed per file: the real tool is exercised by the validation tests.</summary>
    private sealed class StubMeasurements : ICharacterIdentityMeasurementService
    {
        public Func<string, CharacterIdentityEyeMeasurement> Measure { get; set; } = _ => new();

        public Task<CharacterIdentityMeasurementResult> MeasureFileAsync(
            string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new CharacterIdentityMeasurementResult(
                Measure(imagePath), "stub tool output"));
    }

    /// <summary>
    /// The asset library as the angle panel uses it: uploads and mirror-derived images are written to the real
    /// scene-image root (so the mirror engine reads and the tests can inspect real bytes), every other member
    /// throws because reaching it would be a bug in the test.
    /// </summary>
    private sealed class StubSceneAssetService : ISceneAssetService
    {
        private readonly string _root;
        private int _counter;

        public StubSceneAssetService(string root) => _root = root;

        public List<SceneAssetImage> Images { get; } = [];

        public List<DerivedWrite> Derived { get; } = [];

        public sealed record DerivedWrite(
            SceneAssetImage Image,
            string SourceImageId,
            MediaEditOperationKind Operation,
            string FileName,
            string? CandidateBatchId,
            byte[] Bytes);

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult(Images.FirstOrDefault(i => i.Id == imageId));

        public Task<SceneAssetImage> AddUploadedImageAsync(
            string assetId, string fileName, Stream content, CancellationToken cancellationToken = default,
            string? candidateBatchId = null)
            => Task.FromResult(Add(assetId, content, SceneAssetKind.Uploaded, candidateBatchId));

        public Task<SceneAssetImage> AddDerivedImageAsync(
            string assetId, string sourceImageId, MediaEditOperationKind operation, string fileName, Stream content,
            CancellationToken cancellationToken = default, string? candidateBatchId = null)
        {
            var image = Add(assetId, content, SceneAssetKind.Edited, candidateBatchId);
            image.SourceImageId = sourceImageId;
            Derived.Add(new DerivedWrite(
                image, sourceImageId, operation, fileName, candidateBatchId,
                File.ReadAllBytes(Path.Combine(_root, image.FileRelativePath!))));
            return Task.FromResult(image);
        }

        /// <summary>A finished image already on disk, for the queued-render completion path.</summary>
        public SceneAssetImage AddCompleteImage(string id)
        {
            var relativePath = $"{id}.png";
            File.WriteAllBytes(Path.Combine(_root, relativePath), AsymmetricPng(40, 24));
            var image = new SceneAssetImage
            {
                Id = id,
                AssetId = "front-container",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = relativePath,
                Sha256 = $"sha-{id}"
            };
            Images.Add(image);
            return image;
        }

        private SceneAssetImage Add(string assetId, Stream content, SceneAssetKind kind, string? candidateBatchId)
        {
            var id = $"image-{++_counter}";
            var relativePath = $"{id}.png";
            using (var file = File.Create(Path.Combine(_root, relativePath)))
            {
                content.CopyTo(file);
            }

            var image = new SceneAssetImage
            {
                Id = id,
                AssetId = assetId,
                Kind = kind,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = relativePath,
                Sha256 = $"sha-{id}",
                CandidateBatchId = candidateBatchId
            };
            Images.Add(image);
            return image;
        }

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                Images.Where(i => string.Equals(i.AssetId, assetId, StringComparison.Ordinal)).ToList());

        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(
            string imageId, CancellationToken cancellationToken = default)
        {
            var image = Images.First(i => i.Id == imageId);
            return Task.FromResult<(SceneAsset, SceneAssetImage, Stream)>((
                new SceneAsset { Id = image.AssetId }, image, File.OpenRead(Path.Combine(_root, image.FileRelativePath!))));
        }

        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null)
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
