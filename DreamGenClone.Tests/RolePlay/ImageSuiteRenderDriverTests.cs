using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The path that makes "run the whole baseline catalog on BigLust" an operator action (B-135).
///
/// <para>
/// Three things are pinned here because each one has a way of failing that looks like success. A cell must render from
/// ITS OWN prompt for the chosen model — rendering the neutral scene for every model would produce a full container of
/// images that answered a different question. The prompt must be sent AS AUTHORED, not compiled a second time, or
/// Pony's variant gets a second quality string prepended and quietly exceeds its qualified length. And a cell with no
/// prompt for the chosen model must be REPORTED, never silently dropped, because a skipped cell and a cell that was
/// never in the run look identical in the finished images.
/// </para>
/// </summary>
public sealed class ImageSuiteRenderDriverTests
{
    private const string SuiteId = "suite-1";

    [Fact]
    public async Task RenderAsync_SendsEachCellsOwnPromptForTheChosenVariant()
    {
        var cells = new[]
        {
            Cell(0, "missionary", ("biglust", "BIGLUST-MISSIONARY"), ("pony", "PONY-MISSIONARY")),
            Cell(1, "doggy", ("biglust", "BIGLUST-DOGGY"))
        };
        var (driver, assets) = Build(cells);

        var report = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId,
            [cells[0].Id, cells[1].Id],
            "biglust",
            "model-1",
            "1024x1024",
            ImageSeedSource.Random,
            "baseline · biglust"));

        Assert.Equal(2, report.EnqueuedCount);
        Assert.Equal(0, report.SkippedCount);
        Assert.Equal(["BIGLUST-MISSIONARY", "BIGLUST-DOGGY"], assets.Prompts);

        // The catalog authored these for this model's dialect, so the render states that rather than letting the
        // runtime compiler reshape text that is already model-ready.
        Assert.All(assets.Options, options =>
            Assert.Equal(ImageSuiteRenderDriver.CatalogVariantCompilerId, options?.PromptCompilerId));

        // One run, one container, one batch id: the set comes back together in the Asset Manager.
        Assert.All(assets.BatchIds, batchId => Assert.Equal(report.ContainerAssetId, batchId));
        Assert.All(assets.AssetIds, assetId => Assert.Equal(report.ContainerAssetId, assetId));
        Assert.Equal("baseline · biglust", assets.ContainerName);
        Assert.Equal(report.ContainerAssetId, assets.ContainerId);
    }

    [Fact]
    public async Task RenderAsync_RendersOnlyTheChosenCells()
    {
        var cells = new[]
        {
            Cell(0, "missionary", ("biglust", "A")),
            Cell(1, "doggy", ("biglust", "B")),
            Cell(2, "cowgirl", ("biglust", "C"))
        };
        var (driver, assets) = Build(cells);

        var one = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId, [cells[1].Id], "biglust", "model-1", "1024x1024", ImageSeedSource.Random, "just one"));

        Assert.Equal(1, one.EnqueuedCount);
        Assert.Equal(["B"], assets.Prompts);

        var all = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId, [.. cells.Select(cell => cell.Id)], "biglust", "model-1", "1024x1024", ImageSeedSource.Random, "all of them"));

        Assert.Equal(3, all.EnqueuedCount);
        Assert.Equal(["B", "A", "B", "C"], assets.Prompts);
    }

    [Fact]
    public async Task RenderAsync_SkipsACellWithNoPromptForTheChosenModelAndReportsItByKey()
    {
        var cells = new[]
        {
            Cell(0, "missionary", ("biglust", "A")),
            Cell(1, "doggy", ("pony", "PONY-ONLY"))
        };
        var (driver, assets) = Build(cells);

        var report = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId, [cells[0].Id, cells[1].Id], "biglust", "model-1", "1024x1024", ImageSeedSource.Random, "partial"));

        Assert.Equal(1, report.EnqueuedCount);
        Assert.Equal(1, report.SkippedCount);
        Assert.Equal(["A"], assets.Prompts);

        var skip = Assert.Single(report.Skipped);
        Assert.Equal("doggy", skip.CellName);
        Assert.Contains("has no 'biglust' prompt", skip.Reason);
    }

    [Fact]
    public async Task RenderAsync_SkipsACellWhoseVariantsAreUnreadableRatherThanCancellingTheRun()
    {
        var broken = new ImageSuiteCell
        {
            Id = "cell-broken",
            SuiteId = SuiteId,
            Ordinal = 1,
            Name = "broken",
            VariantsJson = "{ this is not json"
        };
        var cells = new[] { Cell(0, "missionary", ("biglust", "A")), broken };
        var (driver, assets) = Build(cells);

        var report = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId, [cells[0].Id, broken.Id], "biglust", "model-1", "1024x1024", ImageSeedSource.Random, "one bad cell"));

        Assert.Equal(1, report.EnqueuedCount);
        Assert.Equal(["A"], assets.Prompts);
        Assert.Contains("unreadable", Assert.Single(report.Skipped).Reason);
    }

    [Fact]
    public async Task RenderAsync_CarriesTheIdentityReferenceWhenOneIsGiven()
    {
        var cells = new[] { Cell(0, "missionary", ("biglust", "A")) };
        var (driver, assets) = Build(cells);

        await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId,
            [cells[0].Id],
            "biglust",
            "model-1",
            "1024x1024",
            ImageSeedSource.Random,
            "with the character",
            IdentityPackId: "pack-1",
            IdentityFaceAssetId: "face-1"));

        var conditioning = Assert.Single(assets.Options).Identity;
        Assert.NotNull(conditioning);
        Assert.Equal("pack-1", conditioning!.PackId);
        Assert.Equal("face-1", conditioning.FaceAssetId);
    }

    [Fact]
    public async Task RenderAsync_CarriesTheCharacterLorasTheRunWasGiven()
    {
        var cells = new[] { Cell(0, "missionary", ("biglust", "A")), Cell(1, "doggy", ("biglust", "B")) };
        var (driver, assets) = Build(cells);
        var loras = new List<SceneImageCharacterLoraSelection>
        {
            new() { ArtifactId = "becky-artifact", Strength = 0.8 }
        };

        var report = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId,
            [cells[0].Id, cells[1].Id],
            "biglust",
            "model-1",
            "1024x1024",
            ImageSeedSource.Random,
            "with the character applied",
            CharacterLoras: loras));

        // EVERY image in the run, not just the first: a set is one experiment, and a LoRA applied to half of it would
        // make the run unreadable as a comparison.
        Assert.Equal(2, assets.Options.Count);
        Assert.All(assets.Options, options =>
        {
            var applied = Assert.Single(options!.CharacterLoras!);
            Assert.Equal("becky-artifact", applied.ArtifactId);
            Assert.Equal(0.8, applied.Strength);
        });

        // Recorded on the report too, so the run can be read without opening the database.
        Assert.Equal(1, report.LoraCount);
    }

    [Fact]
    public async Task RenderAsync_WithNoLoras_AppliesNoneRatherThanAnEmptyChain()
    {
        var cells = new[] { Cell(0, "missionary", ("biglust", "A")) };
        var (driver, assets) = Build(cells);

        var report = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId, [cells[0].Id], "biglust", "model-1", "1024x1024", ImageSeedSource.Random, "no character"));

        // Null, not an empty list: "no LoRA" is a configured state in which the render emits no LoRA node at all.
        Assert.All(assets.Options, options => Assert.Null(options!.CharacterLoras));
        Assert.Equal(0, report.LoraCount);
    }

    [Fact]
    public async Task RenderAsync_RefusesEveryDeclarationItCannotInvent()
    {
        var cells = new[] { Cell(0, "missionary", ("biglust", "A")) };
        var (driver, _) = Build(cells);
        var complete = new ImageSuiteRenderRequest(
            SuiteId, [cells[0].Id], "biglust", "model-1", "1024x1024", ImageSeedSource.Random, "named run");

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.RenderAsync(complete with { CellIds = [] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.RenderAsync(complete with { VariantKey = " " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.RenderAsync(complete with { ModelId = string.Empty }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.RenderAsync(complete with { ImageSize = string.Empty }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.RenderAsync(complete with { RunName = string.Empty }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.RenderAsync(complete with { SuiteId = "no-such-suite" }));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.RenderAsync(complete with { CellIds = ["a-cell-from-another-suite"] }));

        // Neither seed mode is assumed: a run that did not say whether it reproduces or explores produced images
        // nobody could interpret.
        var unstatedSeed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.RenderAsync(complete with { SeedSource = ImageSeedSource.Unknown }));
        Assert.Contains("reproduces", unstatedSeed.Message);

        // A run of nothing must not be reported as a finished run, so the empty selection says which declaration is
        // missing rather than quietly rendering zero images.
        var emptySelection = await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.RenderAsync(complete with { CellIds = [] }));
        Assert.Contains("at least one cell", emptySelection.Message);
    }

    [Fact]
    public void ListKeys_ReturnsEveryKeyTheCellsCarry_OnceAndInAStableOrder()
    {
        var cells = new[]
        {
            Cell(0, "a", ("pony", "1"), ("biglust", "2")),
            Cell(1, "b", ("flux", "3"), ("pony", "4"))
        };

        Assert.Equal(["biglust", "flux", "pony"], ImageCellVariants.ListKeys(cells));
        Assert.Empty(ImageCellVariants.ListKeys([]));
    }

    [Fact]
    public void Read_DropsABlankVariantRatherThanOfferingIt()
    {
        var cell = Cell(0, "a", ("biglust", " "), ("pony", "tags"));

        var variants = ImageCellVariants.Read(cell.VariantsJson, cell.Name);

        Assert.False(variants.ContainsKey("biglust"));
        Assert.Equal("tags", variants["pony"]);
    }

    [Fact]
    public async Task RenderAsync_DeclaredSeed_SendsEachCellsOwnSeed()
    {
        var cells = new[]
        {
            CellWithSeed(0, "missionary", 73190, ("biglust", "A")),
            CellWithSeed(1, "doggy", 73191, ("biglust", "B"))
        };
        var (driver, assets) = Build(cells);

        var report = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId,
            [cells[0].Id, cells[1].Id],
            "biglust",
            "model-1",
            "1024x1024",
            ImageSeedSource.Declared,
            "reproducible"));

        Assert.Equal(2, report.EnqueuedCount);
        Assert.Equal(0, report.SkippedCount);
        Assert.Equal(ImageSeedSource.Declared, report.SeedSource);
        Assert.Equal(new long?[] { 73190L, 73191L }, assets.Seeds);
    }

    [Fact]
    public async Task RenderAsync_DeclaredSeed_SkipsACellThatDeclaresNoSeed()
    {
        // A reproducible run mixed with random cells would not be reproducible, so the cell without a seed is left
        // out and NAMED rather than quietly given a fresh one.
        var cells = new[]
        {
            CellWithSeed(0, "missionary", 73190, ("biglust", "A")),
            Cell(1, "doggy", ("biglust", "B"))
        };
        var (driver, assets) = Build(cells);

        var report = await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId,
            [cells[0].Id, cells[1].Id],
            "biglust",
            "model-1",
            "1024x1024",
            ImageSeedSource.Declared,
            "reproducible"));

        Assert.Equal(1, report.EnqueuedCount);
        Assert.Equal(new long?[] { 73190L }, assets.Seeds);
        Assert.Contains("declares no seed", Assert.Single(report.Skipped).Reason);
    }

    [Fact]
    public async Task RenderAsync_RandomSeed_AsksForAFreshSeedForEveryCell()
    {
        var cells = new[]
        {
            CellWithSeed(0, "missionary", 73190, ("biglust", "A")),
            CellWithSeed(1, "doggy", 73191, ("biglust", "B"))
        };
        var (driver, assets) = Build(cells);

        await driver.RenderAsync(new ImageSuiteRenderRequest(
            SuiteId,
            [cells[0].Id, cells[1].Id],
            "biglust",
            "model-1",
            "1024x1024",
            ImageSeedSource.Random,
            "explore"));

        // Null is the REQUEST for a new seed — and the render writes back the one it drew, so exploration still
        // leaves a reproducible trail instead of losing a lucky result.
        Assert.Equal(new long?[] { null, null }, assets.Seeds);
    }

    [Fact]
    public void ReadDeclared_ReadsTheSeedAndRefusesAnythingThatIsNotAWholeNumber()
    {
        Assert.Equal(73190L, ImageCellSeed.ReadDeclared("{\"seed\":73190}", "cell"));
        Assert.Null(ImageCellSeed.ReadDeclared("{}", "cell"));
        Assert.Null(ImageCellSeed.ReadDeclared(string.Empty, "cell"));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => ImageCellSeed.ReadDeclared("{\"seed\":\"73190\"}", "missionary"));
        Assert.Contains("not a whole number", thrown.Message);
    }

    private static (ImageSuiteRenderDriver Driver, RecordingAssetService Assets) Build(
        IReadOnlyList<ImageSuiteCell> cells)
    {
        var suite = new ImageSuite
        {
            Id = SuiteId,
            Name = "baseline-positions",
            Kind = ImageSuiteKind.Catalog,
            Status = ImageSuiteStatus.Draft,
            Provenance = "test fixture"
        };

        var assets = new RecordingAssetService();
        var driver = new ImageSuiteRenderDriver(
            new StubSuites(suite, cells),
            assets,
            NullLogger<ImageSuiteRenderDriver>.Instance);

        return (driver, assets);
    }

    private static ImageSuiteCell Cell(
        int ordinal,
        string name,
        params (string Key, string Prompt)[] variants)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, prompt) in variants)
        {
            map[key] = prompt;
        }

        return new ImageSuiteCell
        {
            Id = $"cell-{ordinal}",
            SuiteId = SuiteId,
            Ordinal = ordinal,
            Name = name,
            VariantsJson = JsonSerializer.Serialize(map)
        };
    }

    private static ImageSuiteCell CellWithSeed(
        int ordinal,
        string name,
        long seed,
        params (string Key, string Prompt)[] variants)
    {
        var cell = Cell(ordinal, name, variants);
        cell.SettingsJson = JsonSerializer.Serialize(new { seed, steps = 30, cfg = 5.0 });
        return cell;
    }

    private sealed class StubSuites(ImageSuite suite, IReadOnlyList<ImageSuiteCell> cells) : IImageSuiteRepository
    {
        public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ImageSuite>> ListSuitesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ImageSuite>>([suite]);

        public Task<ImageSuite?> GetSuiteAsync(string suiteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ImageSuite?>(suite.Id == suiteId ? suite : null);

        public Task UpsertSuiteAsync(ImageSuite candidate, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ImageSuiteCell>> ListCellsAsync(
            string suiteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ImageSuiteCell>>(
                cells.Where(cell => cell.SuiteId == suiteId).ToList());

        public Task UpsertCellAsync(ImageSuiteCell cell, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteCellAsync(string cellId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Records every enqueue, so the assertions are about what the render was ACTUALLY asked for rather than about what
    /// the report says. Everything else throws, so a test that accidentally depends on another call fails loudly.
    /// </summary>
    private sealed class RecordingAssetService : ISceneAssetService
    {
        public List<string> Prompts { get; } = [];

        public List<string> ModelIds { get; } = [];

        public List<string?> BatchIds { get; } = [];

        public List<string> AssetIds { get; } = [];

        public List<SceneAssetImageGenerationOptions?> Options { get; } = [];

        /// <summary>The seed each enqueue asked for, so "a pinned seed is sent" and "a fresh seed is requested" are
        /// asserted on data rather than on the report.</summary>
        public List<long?> Seeds { get; } = [];

        public string ContainerId { get; set; } = "container-1";

        public string? ContainerName { get; private set; }

        public Task<SceneAsset> CreateAssetAsync(
            string name,
            SceneAssetType type,
            string? characterProfileId = null,
            CancellationToken cancellationToken = default)
        {
            ContainerName = name;
            return Task.FromResult(new SceneAsset
            {
                Id = ContainerId,
                Name = name,
                Type = type,
                Status = SceneAssetStatus.Pending
            });
        }

        public Task<SceneAssetImage> AddGeneratedImageAsync(
            string assetId,
            string prompt,
            string modelId,
            string imageSize,
            CancellationToken cancellationToken = default,
            IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null,
            string? candidateBatchId = null,
            SceneAssetImageGenerationOptions? options = null)
        {
            Prompts.Add(prompt);
            ModelIds.Add(modelId);
            BatchIds.Add(candidateBatchId);
            AssetIds.Add(assetId);
            Options.Add(options);
            Seeds.Add(options?.Seed);

            return Task.FromResult(new SceneAssetImage
            {
                Id = $"image-{Prompts.Count}",
                AssetId = assetId,
                Status = SceneAssetStatus.Pending,
                Prompt = prompt,
                CandidateBatchId = candidateBatchId
            });
        }

        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddDerivedImageAsync(string assetId, string sourceImageId, MediaEditOperationKind operation, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImageValidationResultAsync(string imageId, string? validationResultJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> ApproveImageForProductionAsync(string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
}
