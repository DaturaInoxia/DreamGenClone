using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterIdentityFrontServiceTests
{
    [Fact]
    public async Task ResolveFrontPrompt_FillsPlaceholders()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var prompt = await service.ResolveFrontPromptAsync("char-1", "Dean", "a rugged man with stubble");
            Assert.Contains("Dean", prompt, StringComparison.Ordinal);
            Assert.Contains("a rugged man with stubble", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("{CharacterName}", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("{Description}", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ResolveFrontPrompt_EmptyDescription_NoDanglingColon()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var prompt = await service.ResolveFrontPromptAsync("char-1", "Sam", "");
            Assert.Contains("Sam", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain(": .", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Generate_ThenSelect_CompletesFrontStep()
    {
        var (service, assets, dbPath) = CreateService();
        try
        {
            var build = await assets.Builds.CreateBuildAsync("char-1", null, CharacterIdentityTargetKind.Face);

            var attempt = await service.GenerateFrontAttemptAsync(build.Id, "Dean front", "a portrait of Dean", "model-1", "1024x1024");
            Assert.Equal("Pending", attempt.Status.ToString());

            // Simulate the generation job completing the image.
            var image = assets.Images.Single(i => i.Id == attempt.Id);
            image.Status = SceneAssetStatus.Complete;
            image.Prompt = "a portrait of Dean";
            image.AssociationMetadataJson = "{\"requestedModelId\":\"model-1\"}";

            var updated = await service.SelectFrontAsync(build.Id, attempt.Id);
            Assert.Equal(CharacterIdentityBuildStep.Validate, updated.CurrentStep);

            var steps = await assets.Builds.ListStepsAsync(build.Id);
            var front = steps.Single(s => s.Step == CharacterIdentityBuildStep.Front);
            Assert.Equal(CharacterIdentityBuildStepStatus.Complete, front.Status);
            Assert.Equal(attempt.Id, front.OutputArtifactId);
            Assert.Equal("model-1", front.ResolvedModelId);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Select_RejectsImageFromAnotherContainer()
    {
        var (service, assets, dbPath) = CreateService();
        try
        {
            var build = await assets.Builds.CreateBuildAsync("char-1", null, CharacterIdentityTargetKind.Face);
            await service.EnsureFrontContainerAsync(build.Id, "Dean front");
            assets.Images.Add(new SceneAssetImage
            {
                Id = "other-image",
                AssetId = "some-other-container",
                Status = SceneAssetStatus.Complete
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SelectFrontAsync(build.Id, "other-image"));
            Assert.Contains("does not belong", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task FrontPrompt_HasNoCharacterOverrideByDefault()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            Assert.False(await service.HasFrontPromptOverrideAsync("char-1"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SaveFrontPrompt_PersistsForTheCharacterAndResolvesOnReopen()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            const string custom = "Custom front portrait of Dean in a rainy alley, head and shoulders.";
            await service.SaveFrontPromptAsync("char-1", custom);

            var reopened = await service.ResolveFrontPromptAsync("char-1", "Dean", "");
            Assert.Equal(custom, reopened);
            Assert.True(await service.HasFrontPromptOverrideAsync("char-1"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SaveFrontPrompt_DoesNotChangeAnotherCharacterOrDefault()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            await service.SaveFrontPromptAsync("char-1", "Dean-only custom prompt.");

            var other = await service.ResolveFrontPromptAsync("char-2", "Sam", "");
            Assert.Contains("Sam", other, StringComparison.Ordinal);
            Assert.Contains("Photorealistic frontal portrait", other, StringComparison.Ordinal);
            Assert.False(await service.HasFrontPromptOverrideAsync("char-2"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ResetFrontPromptToDefault_RestoresDefaultAndClearsOverride()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            await service.SaveFrontPromptAsync("char-1", "Dean-only custom prompt.");
            Assert.True(await service.HasFrontPromptOverrideAsync("char-1"));

            var reset = await service.ResetFrontPromptToDefaultAsync("char-1", "Dean", "");

            Assert.Contains("Photorealistic frontal portrait", reset, StringComparison.Ordinal);
            Assert.Contains("Dean", reset, StringComparison.Ordinal);
            Assert.DoesNotContain("Dean-only custom prompt", reset, StringComparison.Ordinal);
            Assert.False(await service.HasFrontPromptOverrideAsync("char-1"));

            var reopened = await service.ResolveFrontPromptAsync("char-1", "Dean", "");
            Assert.Equal(reset, reopened);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task FrontModel_IsUnsetUntilChosen_ThenPersistsForTheCharacterOnly()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            Assert.Null(await service.ResolveFrontModelAsync("char-1"));

            await service.SaveFrontModelAsync("char-1", "model-abc");

            Assert.Equal("model-abc", await service.ResolveFrontModelAsync("char-1"));
            Assert.Null(await service.ResolveFrontModelAsync("char-2"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task FrontModel_ReSelectionOverwritesAndSurvivesReopen()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            await service.SaveFrontModelAsync("char-1", "model-abc");
            await service.SaveFrontModelAsync("char-1", "model-xyz");

            Assert.Equal("model-xyz", await service.ResolveFrontModelAsync("char-1"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (CharacterIdentityFrontService service, StubSceneAssetService assets, string dbPath) CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"front-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var buildRepo = new CharacterIdentityBuildRepository(options);
        buildRepo.EnsureSchemaAsync().GetAwaiter().GetResult();

        var templateRepo = new ImageWorkflowRepository(options);
        templateRepo.EnsureSchemaAsync().GetAwaiter().GetResult();

        var assets = new StubSceneAssetService();
        var builds = new CharacterIdentityBuildService(
            buildRepo,
            assets,
            new CharacterIdentityStepPlanService(buildRepo),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CharacterIdentityBuildService>.Instance);
        var templates = new ImageWorkflowTemplateService(templateRepo);
        assets.Builds = builds;
        var service = new CharacterIdentityFrontService(builds, assets, templates);
        return (service, assets, dbPath);
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

    private sealed class StubSceneAssetService : ISceneAssetService
    {
        public List<SceneAsset> Assets { get; } = [];

        public List<SceneAssetImage> Images { get; } = [];

        public CharacterIdentityBuildService Builds { get; set; } = default!;

        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAsset>>(Assets);

        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default)
        {
            var asset = new SceneAsset { Id = Guid.NewGuid().ToString("N"), Name = name, Type = type, CharacterProfileId = characterProfileId };
            Assets.Add(asset);
            return Task.FromResult(asset);
        }

        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult(Assets.FirstOrDefault(a => a.Id == assetId));

        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null)
        {
            var image = new SceneAssetImage { Id = Guid.NewGuid().ToString("N"), AssetId = assetId, Prompt = prompt, Status = SceneAssetStatus.Pending };
            Images.Add(image);
            return Task.FromResult(image);
        }

        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null)
        {
            var image = new SceneAssetImage { Id = Guid.NewGuid().ToString("N"), AssetId = assetId, Status = SceneAssetStatus.Complete };
            Images.Add(image);
            return Task.FromResult(image);
        }

        public Task<SceneAssetImage> AddDerivedImageAsync(string assetId, string sourceImageId, MediaEditOperationKind operation, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SceneAssetImage>>(Images.Where(i => i.AssetId == assetId).ToList());
        }

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Images.FirstOrDefault(i => i.Id == imageId));
        }

        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImageValidationResultAsync(string imageId, string? validationResultJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> ApproveImageForProductionAsync(string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> CreateFromPromptAsync(string name, string prompt, SceneAssetType type, string modelId, string imageSize, string? candidateBatchId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> CreateFromUploadAsync(string name, SceneAssetType type, string fileName, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> EnqueueEditAsync(string sourceAssetId, string name, string editPrompt, string modelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnqueueProfilePackAsync(SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(string identityPackId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
