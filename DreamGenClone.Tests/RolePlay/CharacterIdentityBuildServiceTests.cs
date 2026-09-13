using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterIdentityBuildServiceTests
{
    [Fact]
    public async Task CreateBuild_CreatesSevenSteps_AllNotStarted()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);
            var steps = await service.ListStepsAsync(build.Id);

            Assert.Equal(7, steps.Count);
            Assert.All(steps, s => Assert.Equal(CharacterIdentityBuildStepStatus.NotStarted, s.Status));
            Assert.Equal(CharacterIdentityBuildStep.Front, build.CurrentStep);
            Assert.Equal(CharacterIdentityBuildStatus.InProgress, build.Status);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task CompleteStep_AdvancesThroughOrder_AndCompletes()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);

            build = await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Front, null, "front-1");
            Assert.Equal(CharacterIdentityBuildStep.Validate, build.CurrentStep);

            build = await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Validate, "front-1", "validated-1");
            Assert.Equal(CharacterIdentityBuildStep.GarmentRemoval, build.CurrentStep);

            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.GarmentRemoval, "validated-1", "de-clothed-1");
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Crop, "de-clothed-1", "cropped-1");
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Enhance, "cropped-1", "enhanced-1");
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Angles, "enhanced-1", "angles-1");
            build = await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Promote, "angles-1", "pack-1");

            Assert.Equal(CharacterIdentityBuildStatus.Complete, build.Status);

            var steps = await service.ListStepsAsync(build.Id);
            Assert.All(steps, s => Assert.Equal(CharacterIdentityBuildStepStatus.Complete, s.Status));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task OutOfOrder_FailsFast()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Validate, "x", "y"));
            Assert.Contains("out of order", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Front", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Skip_IsRecorded_AndAdvances()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);

            build = await service.SkipStepAsync(build.Id, CharacterIdentityBuildStep.Front);
            Assert.Equal(CharacterIdentityBuildStep.Validate, build.CurrentStep);

            var steps = await service.ListStepsAsync(build.Id);
            var front = steps.Single(s => s.Step == CharacterIdentityBuildStep.Front);
            Assert.Equal(CharacterIdentityBuildStepStatus.Skipped, front.Status);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Resume_DoesNotRepeatCompletedSteps()
    {
        var (service, repo, dbPath) = CreateService();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Front, null, "front-1");
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Validate, "front-1", "validated-1");

            // Simulate a restart: reload from persistence.
            var reloaded = await service.GetBuildAsync(build.Id);
            Assert.NotNull(reloaded);
            Assert.Equal(CharacterIdentityBuildStep.GarmentRemoval, reloaded!.CurrentStep);

            var steps = await service.ListStepsAsync(build.Id);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, steps.Single(s => s.Step == CharacterIdentityBuildStep.Front).Status);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, steps.Single(s => s.Step == CharacterIdentityBuildStep.Validate).Status);
            Assert.Equal(CharacterIdentityBuildStepStatus.NotStarted, steps.Single(s => s.Step == CharacterIdentityBuildStep.GarmentRemoval).Status);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ReRunStep_LeavesEarlierArtifactsUntouched()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Front, null, "front-1");
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Validate, "front-1", "validated-1");

            build = await service.ReRunStepAsync(build.Id, CharacterIdentityBuildStep.Validate);
            Assert.Equal(CharacterIdentityBuildStep.Validate, build.CurrentStep);

            var steps = await service.ListStepsAsync(build.Id);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, steps.Single(s => s.Step == CharacterIdentityBuildStep.Front).Status);
            Assert.Equal("front-1", steps.Single(s => s.Step == CharacterIdentityBuildStep.Front).OutputArtifactId);

            var validate = steps.Single(s => s.Step == CharacterIdentityBuildStep.Validate);
            Assert.Equal(CharacterIdentityBuildStepStatus.NotStarted, validate.Status);
            Assert.Null(validate.OutputArtifactId);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task MissingInputArtifact_FailsFast()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Front, null, "front-1");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Validate, null, "validated-1"));
            Assert.Contains("input artifact", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task FailStep_BlocksAdvance_UntilCompleted()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);
            await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Front, null, "front-1");

            build = await service.FailStepAsync(build.Id, CharacterIdentityBuildStep.Validate, "no face mesh");
            Assert.Equal(CharacterIdentityBuildStep.Validate, build.CurrentStep);

            var steps = await service.ListStepsAsync(build.Id);
            var validate = steps.Single(s => s.Step == CharacterIdentityBuildStep.Validate);
            Assert.Equal(CharacterIdentityBuildStepStatus.Failed, validate.Status);
            Assert.Equal("no face mesh", validate.FailureReason);

            // The failed step can be completed (e.g. after a manual override) and the build advances.
            build = await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Validate, "front-1", "validated-1");
            Assert.Equal(CharacterIdentityBuildStep.GarmentRemoval, build.CurrentStep);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SetCanonicalFront_RecordsTheStepsTheImageWentThrough_AndMovesToAngles()
    {
        var (service, _, assets, dbPath) = CreateServiceWithAssets();
        try
        {
            var build = await StartBuildWithFrontAsync(service, assets);

            // The chain the user actually produced: front candidate → de-clothe → crop → enhance.
            AddImage(assets, "de-clothed", "front-candidate", MediaEditOperationKind.Edit);
            AddImage(assets, "cropped", "de-clothed", MediaEditOperationKind.Crop);
            AddImage(assets, "enhanced", "cropped", MediaEditOperationKind.Enhance);

            var updated = await service.SetCanonicalFrontAsync(build.Id, "enhanced");

            // The choice is recorded on the build...
            Assert.Equal("enhanced", updated.CanonicalFrontAssetId);

            // ...and the Front step is left exactly where the pipeline put it: the user approves a canonical
            // image long after Front completed, so nothing may be un-completed or re-opened.
            var steps = await service.ListStepsAsync(build.Id);
            var front = steps.Single(s => s.Step == CharacterIdentityBuildStep.Front);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, front.Status);
            Assert.Equal("front-candidate", front.OutputArtifactId);

            // The three steps the approved image DID go through are complete, each naming its own artifact.
            var deClothe = steps.Single(s => s.Step == CharacterIdentityBuildStep.GarmentRemoval);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, deClothe.Status);
            Assert.Equal("front-candidate", deClothe.InputArtifactId);
            Assert.Equal("de-clothed", deClothe.OutputArtifactId);

            var crop = steps.Single(s => s.Step == CharacterIdentityBuildStep.Crop);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, crop.Status);
            Assert.Equal("de-clothed", crop.InputArtifactId);
            Assert.Equal("cropped", crop.OutputArtifactId);

            var enhance = steps.Single(s => s.Step == CharacterIdentityBuildStep.Enhance);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, enhance.Status);
            Assert.Equal("cropped", enhance.InputArtifactId);
            Assert.Equal("enhanced", enhance.OutputArtifactId);

            Assert.Equal(CharacterIdentityBuildStep.Angles, updated.CurrentStep);

            // The image itself now says what was done to it (B-121 note 002).
            var pipeline = ParsePipeline(assets.PipelineSteps["enhanced"]);
            Assert.Equal("front-candidate", pipeline.FrontArtifactId);
            Assert.Equal(
                new[]
                {
                    (CharacterIdentityBuildStep.GarmentRemoval, CharacterIdentityBuildStepStatus.Complete, "de-clothed"),
                    (CharacterIdentityBuildStep.Crop, CharacterIdentityBuildStepStatus.Complete, "cropped"),
                    (CharacterIdentityBuildStep.Enhance, CharacterIdentityBuildStepStatus.Complete, "enhanced"),
                },
                pipeline.Steps.Select(s => (s.Step, s.Outcome, s.ArtifactId)).ToArray());
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SetCanonicalFront_RecordsAnOperationTheImageNeverHadAsSkipped()
    {
        var (service, _, assets, dbPath) = CreateServiceWithAssets();
        try
        {
            var build = await StartBuildWithFrontAsync(service, assets);

            // A crop straight off the front candidate: no de-clothe, no enhance.
            AddImage(assets, "cropped", "front-candidate", MediaEditOperationKind.Crop);

            var updated = await service.SetCanonicalFrontAsync(build.Id, "cropped");

            var steps = await service.ListStepsAsync(build.Id);
            var deClothe = steps.Single(s => s.Step == CharacterIdentityBuildStep.GarmentRemoval);
            Assert.Equal(CharacterIdentityBuildStepStatus.Skipped, deClothe.Status);
            Assert.Null(deClothe.OutputArtifactId);

            var crop = steps.Single(s => s.Step == CharacterIdentityBuildStep.Crop);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, crop.Status);
            Assert.Equal("front-candidate", crop.InputArtifactId);
            Assert.Equal("cropped", crop.OutputArtifactId);

            var enhance = steps.Single(s => s.Step == CharacterIdentityBuildStep.Enhance);
            Assert.Equal(CharacterIdentityBuildStepStatus.Skipped, enhance.Status);
            Assert.Null(enhance.OutputArtifactId);
            // A skipped step still names the image it would have transformed.
            Assert.Equal("cropped", enhance.InputArtifactId);

            Assert.Equal(CharacterIdentityBuildStep.Angles, updated.CurrentStep);

            var pipeline = ParsePipeline(assets.PipelineSteps["cropped"]);
            Assert.Equal(
                new[]
                {
                    CharacterIdentityBuildStepStatus.Skipped,
                    CharacterIdentityBuildStepStatus.Complete,
                    CharacterIdentityBuildStepStatus.Skipped,
                },
                pipeline.Steps.Select(s => s.Outcome).ToArray());
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SetCanonicalFront_TakesTheOperationNearestTheApprovedImage()
    {
        var (service, _, assets, dbPath) = CreateServiceWithAssets();
        try
        {
            var build = await StartBuildWithFrontAsync(service, assets);

            // Cropping twice: the second crop is the one in the approved image's chain.
            AddImage(assets, "crop-a", "front-candidate", MediaEditOperationKind.Crop);
            AddImage(assets, "crop-b", "crop-a", MediaEditOperationKind.Crop);

            await service.SetCanonicalFrontAsync(build.Id, "crop-b");

            var crop = (await service.ListStepsAsync(build.Id))
                .Single(s => s.Step == CharacterIdentityBuildStep.Crop);
            Assert.Equal("crop-a", crop.InputArtifactId);
            Assert.Equal("crop-b", crop.OutputArtifactId);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SetCanonicalFront_RefusesAnImageThatDoesNotDeriveFromTheFrontArtifact()
    {
        var (service, _, assets, dbPath) = CreateServiceWithAssets();
        try
        {
            var build = await StartBuildWithFrontAsync(service, assets);

            // A crop of some other candidate: its chain roots somewhere other than the Front step's output.
            assets.Images.Add(new SceneAssetImage
            {
                Id = "other-candidate",
                AssetId = "front-container",
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                Sha256 = ShaOf("other-candidate")
            });
            AddImage(assets, "orphan", "other-candidate", MediaEditOperationKind.Crop);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SetCanonicalFrontAsync(build.Id, "orphan"));

            Assert.Contains("does not derive from the Front step's output", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SetCanonicalFront_RefusesAnImageWhoseProvenanceCannotBeRead()
    {
        var (service, _, assets, dbPath) = CreateServiceWithAssets();
        try
        {
            var build = await StartBuildWithFrontAsync(service, assets);
            assets.Images.Add(new SceneAssetImage
            {
                Id = "unknown-origin",
                AssetId = "front-container",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Complete,
                SourceImageId = "front-candidate",
                Sha256 = "sha-unknown-origin"
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SetCanonicalFrontAsync(build.Id, "unknown-origin"));

            Assert.Contains("no recorded provenance", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SetCanonicalFront_RefusesAnImageWhoseRecordedSourceIsNotInTheFrontContainer()
    {
        var (service, _, assets, dbPath) = CreateServiceWithAssets();
        try
        {
            var build = await StartBuildWithFrontAsync(service, assets);
            AddImage(assets, "from-nowhere", null, MediaEditOperationKind.Crop, sourceSha256: "sha-of-a-file-nobody-has");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SetCanonicalFrontAsync(build.Id, "from-nowhere"));

            Assert.Contains("not an image of front asset", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SetCanonicalFront_RejectsAnImageFromAnotherAsset()
    {
        var (service, _, assets, dbPath) = CreateServiceWithAssets();
        try
        {
            var build = await service.CreateBuildAsync("char-1", null);
            build = await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Front, null, "front-container");
            build = await service.SetFrontContainerAsync(build.Id, "front-container");

            assets.Images.Add(new SceneAssetImage
            {
                Id = "foreign-image",
                AssetId = "some-other-asset",
                Status = SceneAssetStatus.Complete,
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SetCanonicalFrontAsync(build.Id, "foreign-image"));
            Assert.Contains("does not belong", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// A build whose Front step is complete and whose front container is set — the state the user is in
    /// when they approve a canonical front in Panel B. The container holds the candidate the Front step
    /// chose, which is where the derived chain has to end.
    /// </summary>
    private static async Task<CharacterIdentityBuild> StartBuildWithFrontAsync(
        CharacterIdentityBuildService service, StubSceneAssetService assets)
    {
        assets.Images.Add(new SceneAssetImage
        {
            Id = "front-candidate",
            AssetId = "front-container",
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Complete,
            Sha256 = ShaOf("front-candidate")
        });

        var build = await service.CreateBuildAsync("char-1", null);
        build = await service.CompleteStepAsync(build.Id, CharacterIdentityBuildStep.Front, null, "front-candidate");
        build = await service.CompleteStepAsync(
            build.Id, CharacterIdentityBuildStep.Validate, "front-candidate", "front-candidate");
        return await service.SetFrontContainerAsync(build.Id, "front-container");
    }

    private static SceneAssetImage AddImage(
        StubSceneAssetService assets, string id, string? sourceImageId, MediaEditOperationKind kind)
        => AddImage(assets, id, sourceImageId, kind, sourceSha256: ShaOf(sourceImageId));

    private static SceneAssetImage AddImage(
        StubSceneAssetService assets, string id, string? sourceImageId, MediaEditOperationKind kind, string? sourceSha256)
        => AddImage(assets, id, sourceImageId, MediaEditProvenance.OperationValue(kind), sourceSha256);

    /// <summary>
    /// Adds an image the way the pipeline creates one: it carries its own checksum, and its provenance
    /// records the operation and the checksum of the file it was produced from.
    /// </summary>
    private static SceneAssetImage AddImage(
        StubSceneAssetService assets, string id, string? sourceImageId, string? operationValue, string? sourceSha256)
    {
        var image = new SceneAssetImage
        {
            Id = id,
            AssetId = "front-container",
            Kind = SceneAssetKind.Edited,
            Status = SceneAssetStatus.Complete,
            SourceImageId = sourceImageId,
            Sha256 = ShaOf(id),
            SourceProvenanceJson = operationValue is null || sourceSha256 is null
                ? null
                : $"{{\"operation\":\"{operationValue}\",\"{MediaEditProvenance.SourceShaKey}\":\"{sourceSha256}\"}}"
        };
        assets.Images.Add(image);
        return image;
    }

    private static string ShaOf(string? imageId) => $"sha-{imageId}";

    private static SceneAssetImagePipeline ParsePipeline(string? json)
    {
        Assert.False(string.IsNullOrWhiteSpace(json), "the approved image must carry its pipeline record");
        return JsonSerializer.Deserialize<SceneAssetImagePipeline>(
            json!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } })!;
    }

    private static (CharacterIdentityBuildService service, CharacterIdentityBuildRepository repo, string dbPath) CreateService()
    {
        var (service, repo, _, dbPath) = CreateServiceWithAssets();
        return (service, repo, dbPath);
    }

    private static (CharacterIdentityBuildService service, CharacterIdentityBuildRepository repo, StubSceneAssetService assets, string dbPath) CreateServiceWithAssets()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"identity-build-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repo = new CharacterIdentityBuildRepository(options);
        repo.EnsureSchemaAsync().GetAwaiter().GetResult();
        var assets = new StubSceneAssetService();
        var service = new CharacterIdentityBuildService(
            repo, assets, Microsoft.Extensions.Logging.Abstractions.NullLogger<CharacterIdentityBuildService>.Instance);
        return (service, repo, assets, dbPath);
    }

    /// <summary>
    /// Minimal scene-asset surface: only the image lookup is implemented, because the canonical-front
    /// choice is the only build-service path that reads the asset library. Any other member is a bug
    /// in the test, so it throws rather than silently returning empty data.
    /// </summary>
    private sealed class StubSceneAssetService : ISceneAssetService
    {
        public List<SceneAssetImage> Images { get; } = [];

        /// <summary>The pipeline record written per image, so a test can read what the image now says.</summary>
        public Dictionary<string, string?> PipelineSteps { get; } = new(StringComparer.Ordinal);

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult(Images.FirstOrDefault(i => i.Id == imageId));

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                Images.Where(i => string.Equals(i.AssetId, assetId, StringComparison.Ordinal)).ToList());

        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default)
        {
            PipelineSteps[imageId] = pipelineStepsJson;
            return Task.CompletedTask;
        }
        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null)
            => throw new NotSupportedException();

        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null)
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

        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(string imageId, CancellationToken cancellationToken = default)
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

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
