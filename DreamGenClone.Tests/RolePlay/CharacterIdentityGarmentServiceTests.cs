using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterIdentityGarmentServiceTests
{
    private const string PronounPlaceholder = "{SubjectPronounPossessive}";

    [Fact]
    public async Task ResolveGarmentPrompt_MaleCharacter_UsesHis()
    {
        using var fixture = await Fixture.CreateAsync();

        var prompt = await fixture.Service.ResolveGarmentPromptAsync("char-1", "Dean", "Male");

        Assert.DoesNotContain(PronounPlaceholder, prompt, StringComparison.Ordinal);
        Assert.Contains("his", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveGarmentPrompt_FemaleCharacter_UsesHer()
    {
        using var fixture = await Fixture.CreateAsync();

        var prompt = await fixture.Service.ResolveGarmentPromptAsync("char-1", "Dana", "Female");

        Assert.DoesNotContain(PronounPlaceholder, prompt, StringComparison.Ordinal);
        Assert.Contains("her", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveGarmentPrompt_UnknownGender_UsesTheCharacterNameNotAGuess()
    {
        using var fixture = await Fixture.CreateAsync();

        var prompt = await fixture.Service.ResolveGarmentPromptAsync("char-1", "Dean", "Unknown");

        Assert.DoesNotContain(PronounPlaceholder, prompt, StringComparison.Ordinal);
        Assert.Contains("Dean's", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveGarmentPrompt_UnknownGenderAndNoName_FailsFast()
    {
        using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ResolveGarmentPromptAsync("char-1", "  ", "Unknown"));

        Assert.Contains("gender", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveGarmentPrompt_RequiresACharacter()
    {
        using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ResolveGarmentPromptAsync("  ", "Dean", "Male"));

        Assert.Contains("character", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveEditorModelId_FailsFastNamingTheMissingSetting()
    {
        using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ResolveEditorModelIdAsync());

        Assert.Contains("EditorModelId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveEditorModelId_IsGlobalAndIsNotShadowedByACharacterRow()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.SetEditorModelAsync("editor-global");

        // A character-scoped row exists but declares no editor; the global editor must still resolve.
        // Crop/enhance values are required persisted configuration, so an explicit row must carry them.
        await fixture.Templates.SaveSettingsAsync(new ReferenceWorkflowSettings
        {
            CharacterProfileId = "char-1",
            EnhanceTargetLongEdge = 1024,
            CropHeadroomPercent = 8,
            CropTargetAspect = 1.0
        });

        var resolved = await fixture.Service.ResolveEditorModelIdAsync();

        Assert.Equal("editor-global", resolved);
    }

    [Fact]
    public async Task ResolveGarmentSource_ReturnsTheCompletedFrontArtifact()
    {
        using var fixture = await Fixture.CreateAsync();
        var build = await fixture.StartBuildAtGarmentRemovalAsync();

        var source = await fixture.Service.ResolveGarmentSourceAsync(build.Id);

        Assert.Equal(Fixture.FrontImageId, source.Id);
    }

    [Fact]
    public async Task ResolveGarmentSource_RequiresACompletedFrontStep()
    {
        using var fixture = await Fixture.CreateAsync();
        var build = await fixture.Builds.CreateBuildAsync("char-1", null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ResolveGarmentSourceAsync(build.Id));

        Assert.Contains("completed Front step", error.Message, StringComparison.Ordinal);
    }

    private sealed class StubSceneAssetService : ISceneAssetService
    {
        private readonly Dictionary<string, SceneAssetImage> _images = new(StringComparer.Ordinal);

        public void Add(string imageId, SceneAssetStatus status = SceneAssetStatus.Complete)
            => _images[imageId] = new SceneAssetImage
            {
                Id = imageId,
                AssetId = "front-container",
                Status = status,
                FileRelativePath = "assets/face.png"
            };

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult(_images.TryGetValue(imageId, out var image) ? image : null);

        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(string identityPackId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Fixture : IDisposable
    {
        public const string FrontImageId = "front-image-1";

        private Fixture(
            string root,
            CharacterIdentityGarmentService service,
            CharacterIdentityBuildService builds,
            ImageWorkflowTemplateService templates,
            StubSceneAssetService assets)
        {
            Root = root;
            Service = service;
            Builds = builds;
            Templates = templates;
            Assets = assets;
        }

        public string Root { get; }

        public CharacterIdentityGarmentService Service { get; }

        public CharacterIdentityBuildService Builds { get; }

        public ImageWorkflowTemplateService Templates { get; }

        public StubSceneAssetService Assets { get; }

        public static Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"idg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var dbPath = Path.Combine(root, "garment.db");
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(root, "data", "scene-images")
            });

            var buildRepo = new CharacterIdentityBuildRepository(options);
            buildRepo.EnsureSchemaAsync().GetAwaiter().GetResult();
            var templateRepo = new ImageWorkflowRepository(options);
            templateRepo.EnsureSchemaAsync().GetAwaiter().GetResult();

            var assets = new StubSceneAssetService();
            assets.Add(FrontImageId);

            var builds = new CharacterIdentityBuildService(
                buildRepo, assets, Microsoft.Extensions.Logging.Abstractions.NullLogger<CharacterIdentityBuildService>.Instance);
            var templates = new ImageWorkflowTemplateService(templateRepo);

            var service = new CharacterIdentityGarmentService(buildRepo, templates, assets);

            return Task.FromResult(new Fixture(root, service, builds, templates, assets));
        }

        public async Task SetEditorModelAsync(string editorModelId)
        {
            var settings = await Templates.ResolveSettingsAsync(null);
            settings.EditorModelId = editorModelId;
            await Templates.SaveSettingsAsync(settings);
        }

        /// <summary>Front completed, Validate skipped → GarmentRemoval is the current step.</summary>
        public async Task<CharacterIdentityBuild> StartBuildAtGarmentRemovalAsync()
        {
            var build = await Builds.CreateBuildAsync("char-1", null);
            await Builds.CompleteStepAsync(
                build.Id,
                CharacterIdentityBuildStep.Front,
                inputArtifactId: null,
                outputArtifactId: FrontImageId);
            return await Builds.SkipStepAsync(build.Id, CharacterIdentityBuildStep.Validate);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the temp fixture.
            }
        }
    }
}
