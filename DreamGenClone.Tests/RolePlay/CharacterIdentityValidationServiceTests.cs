using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterIdentityValidationServiceTests
{
    [Fact]
    public async Task Measure_WithinThreshold_PassesAndPersistsEvidence()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.SetInterpreterAsync();
        var build = await fixture.StartBuildWithFrontAsync();

        fixture.Runner.Result = Json("-0.2", "0.48", "130.8", error: null);
        var result = await fixture.Service.MeasureAsync(build.Id);

        Assert.Equal(CharacterIdentityValidationVerdict.Pass, result.Verdict);
        Assert.True(result.CanAdvance);
        Assert.Null(result.BlockReason);
        Assert.Equal(-0.2, result.Measurement!.IrisDyPercent);
        Assert.Equal(130.8, result.Measurement.InterocularPixels);
        Assert.Equal(1.5, result.ThresholdPercent);

        var validateRow = (await fixture.Builds.ListStepsAsync(build.Id))
            .Single(row => row.Step == CharacterIdentityBuildStep.Validate);
        Assert.Contains("irisDyPercent", validateRow.MeasurementJson, StringComparison.Ordinal);
        Assert.Contains("iris_dy_pct", validateRow.RawToolOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Measure_OutsideThreshold_FailsAndBlocksAdvancement()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.SetInterpreterAsync();
        var build = await fixture.StartBuildWithFrontAsync();

        fixture.Runner.Result = Json("3.4", "3.1", "120", error: null);
        var result = await fixture.Service.MeasureAsync(build.Id);

        Assert.Equal(CharacterIdentityValidationVerdict.Fail, result.Verdict);
        Assert.False(result.CanAdvance);
        Assert.Contains("Eye gate failed", result.BlockReason, StringComparison.Ordinal);
        Assert.Contains("1.5", result.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Measure_NoFaceMesh_BlocksUntilOverrideIsRecorded()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.SetInterpreterAsync();
        var build = await fixture.StartBuildWithFrontAsync();

        fixture.Runner.Result = Json(null, null, null, "no face mesh");
        var blocked = await fixture.Service.MeasureAsync(build.Id);

        Assert.Equal(CharacterIdentityValidationVerdict.NoFaceMesh, blocked.Verdict);
        Assert.False(blocked.CanAdvance);
        Assert.Contains("no face mesh", blocked.BlockReason, StringComparison.OrdinalIgnoreCase);

        var overridden = await fixture.Service.RecordOverrideAsync(build.Id, "Full profile; verified by eye.", "ken");

        Assert.True(overridden.ManualOverrideApplied);
        Assert.True(overridden.CanAdvance);
        Assert.Null(overridden.BlockReason);
        Assert.Equal("ken", overridden.ManualOverrideAuthor);
        Assert.Equal("Full profile; verified by eye.", overridden.ManualOverrideReason);
        Assert.NotNull(overridden.ManualOverrideUtc);

        var persisted = (await fixture.Builds.ListStepsAsync(build.Id))
            .Single(row => row.Step == CharacterIdentityBuildStep.Validate);
        Assert.True(persisted.ManualOverrideApplied);
        Assert.Equal("ken", persisted.ManualOverrideAuthor);
        Assert.NotNull(persisted.ManualOverrideUtc);
    }

    [Fact]
    public async Task RecordOverride_RequiresReasonAndAuthor()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.SetInterpreterAsync();
        var build = await fixture.StartBuildWithFrontAsync();

        var noReason = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.RecordOverrideAsync(build.Id, "   ", "ken"));
        Assert.Contains("reason", noReason.Message, StringComparison.OrdinalIgnoreCase);

        var noAuthor = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.RecordOverrideAsync(build.Id, "looks fine", " "));
        Assert.Contains("author", noAuthor.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Measure_FailsFastNamingTheMissingInterpreterSetting()
    {
        using var fixture = await Fixture.CreateAsync();
        var build = await fixture.StartBuildWithFrontAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.MeasureAsync(build.Id));

        Assert.Contains("EyeToolPythonPath", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Measure_RequiresACompletedFrontStep()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.SetInterpreterAsync();
        var build = await fixture.Builds.CreateBuildAsync("char-1", null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.MeasureAsync(build.Id));

        Assert.Contains("completed Front step", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_CompletesValidateAndMovesToGarmentRemoval()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.SetInterpreterAsync();
        var build = await fixture.StartBuildWithFrontAsync();

        fixture.Runner.Result = Json("-0.1", "0.2", "140", error: null);
        await fixture.Service.MeasureAsync(build.Id);

        var advanced = await fixture.Service.AdvanceAsync(build.Id);

        Assert.True(advanced.CanAdvance);
        var reloaded = await fixture.Builds.GetBuildAsync(build.Id);
        Assert.Equal(CharacterIdentityBuildStep.GarmentRemoval, reloaded!.CurrentStep);

        var validateRow = (await fixture.Builds.ListStepsAsync(build.Id))
            .Single(row => row.Step == CharacterIdentityBuildStep.Validate);
        Assert.Equal(CharacterIdentityBuildStepStatus.Complete, validateRow.Status);
    }

    [Fact]
    public async Task Advance_IsRefusedWhileTheGateFails()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.SetInterpreterAsync();
        var build = await fixture.StartBuildWithFrontAsync();

        fixture.Runner.Result = Json("4.0", "3.9", "120", error: null);
        await fixture.Service.MeasureAsync(build.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.AdvanceAsync(build.Id));

        Assert.Contains("cannot advance past Validate", error.Message, StringComparison.Ordinal);
    }

    private static CharacterIdentityMeasurementRun Json(
        string? irisDy, string? eyeDy, string? interoc, string? error)
    {
        var payload = "{"
            + $"\"image\": \"face.png\", \"stem\": \"face\", "
            + $"\"error\": {(error is null ? "null" : $"\"{error}\"")}, "
            + $"\"iris_dy_pct\": {irisDy ?? "null"}, "
            + $"\"eye_dy_pct\": {eyeDy ?? "null"}, "
            + $"\"interoc\": {interoc ?? "null"}, "
            + "\"annot\": null}";
        return new CharacterIdentityMeasurementRun(0, payload + Environment.NewLine, string.Empty);
    }

    private sealed class FakeRunner : ICharacterIdentityMeasurementRunner
    {
        public CharacterIdentityMeasurementRun Result { get; set; } = Json("-0.2", "0.4", "130", null);

        public string? LastInterpreterPath { get; private set; }

        public string? LastImagePath { get; private set; }

        public Task<CharacterIdentityMeasurementRun> RunAsync(
            string interpreterPath, string scriptPath, string imagePath, CancellationToken cancellationToken = default)
        {
            LastInterpreterPath = interpreterPath;
            LastImagePath = imagePath;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubSceneAssetService : ISceneAssetService
    {
        private readonly string _relativePath;

        public StubSceneAssetService(string relativePath) => _relativePath = relativePath;

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneAssetImage?>(new SceneAssetImage
            {
                Id = imageId,
                AssetId = "front-container",
                Status = SceneAssetStatus.Complete,
                FileRelativePath = _relativePath
            });

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

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "DreamGenClone.Web";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IDictionary<object, object> Properties { get; } = new Dictionary<object, object>();
    }

    private sealed class Fixture : IDisposable
    {
        private const string FrontImageId = "front-image-1";

        private Fixture(
            string root,
            string dbPath,
            CharacterIdentityValidationService service,
            CharacterIdentityBuildService builds,
            ImageWorkflowTemplateService templates,
            FakeRunner runner)
        {
            Root = root;
            DbPath = dbPath;
            Service = service;
            Builds = builds;
            Templates = templates;
            Runner = runner;
        }

        public string Root { get; }

        public string DbPath { get; }

        public CharacterIdentityValidationService Service { get; }

        public CharacterIdentityBuildService Builds { get; }

        public ImageWorkflowTemplateService Templates { get; }

        public FakeRunner Runner { get; }

        public static Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"idv-{Guid.NewGuid():N}");
            var contentRoot = Path.Combine(root, "DreamGenClone.Web");
            var sceneRoot = Path.Combine(root, "data", "scene-images");
            Directory.CreateDirectory(Path.Combine(contentRoot, "..", "tools", "eye-validation"));
            File.WriteAllText(Path.Combine(root, "tools", "eye-validation", "measure_iris.py"), "# test stub");
            Directory.CreateDirectory(Path.Combine(sceneRoot, "assets"));
            File.WriteAllText(Path.Combine(sceneRoot, "assets", "face.png"), "not-a-real-png");

            var dbPath = Path.Combine(root, "validation.db");
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                SceneImageRoot = sceneRoot
            });

            var buildRepo = new CharacterIdentityBuildRepository(options);
            buildRepo.EnsureSchemaAsync().GetAwaiter().GetResult();
            var templateRepo = new ImageWorkflowRepository(options);
            templateRepo.EnsureSchemaAsync().GetAwaiter().GetResult();

            var runner = new FakeRunner();
            var assets = new StubSceneAssetService("assets/face.png");
            var environment = new StubHostEnvironment { ContentRootPath = contentRoot };

            var builds = new CharacterIdentityBuildService(
                buildRepo, assets, NullLogger<CharacterIdentityBuildService>.Instance);
            var templates = new ImageWorkflowTemplateService(templateRepo);

            var measurements = new CharacterIdentityMeasurementService(
                templates,
                runner,
                environment,
                NullLogger<CharacterIdentityMeasurementService>.Instance);

            var service = new CharacterIdentityValidationService(
                builds,
                buildRepo,
                templates,
                assets,
                measurements,
                options,
                NullLogger<CharacterIdentityValidationService>.Instance);

            return Task.FromResult(new Fixture(root, dbPath, service, builds, templates, runner));
        }

        public async Task SetInterpreterAsync()
        {
            var settings = await Templates.ResolveSettingsAsync(null);
            settings.EyeToolPythonPath = "python";
            await Templates.SaveSettingsAsync(settings);
        }

        public async Task<CharacterIdentityBuild> StartBuildWithFrontAsync()
        {
            var build = await Builds.CreateBuildAsync("char-1", null);
            await Builds.CompleteStepAsync(
                build.Id,
                CharacterIdentityBuildStep.Front,
                inputArtifactId: null,
                outputArtifactId: FrontImageId);
            return build;
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
