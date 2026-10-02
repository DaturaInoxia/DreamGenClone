using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The wardrobe library's contract (operator request 2026-09-29): create a garment, keep several images of it, mark
/// the ones that should be referenceable, and do all of it WITHOUT the character-ownership, version or license
/// ceremony that guards a character's own approved references.
///
/// The rules pinned here decide whether the operator can reach their dress at all: an item has no character owner (so
/// any character may wear it), an image they marked usable carries the platform's provenance without them typing it,
/// and an image that IS usable cannot be deleted out from under a render. The asset service under test is the REAL
/// one, so "may this be deleted?" is answered by production code rather than by a copy of the rule.
/// </summary>
public sealed class WardrobeItemServiceTests
{
    private const string QwenModelId = "3f1c9a52-7d4e-4c8b-9a21-6b0e5d2c8f41";

    [Fact]
    public async Task CreateItemAsync_CreatesAnOwnerlessWardrobeAsset()
    {
        var repository = new RecordingSceneAssetRepository();
        var world = new World(repository);

        var item = await world.Wardrobe.CreateItemAsync("Yellow sundress");

        Assert.Equal(SceneAssetType.Wardrobe, item.Type);
        Assert.Null(item.CharacterProfileId);
        Assert.Equal("Yellow sundress", item.Name);
        Assert.Contains(repository.Assets, asset => asset.Id == item.Id);
    }

    [Fact]
    public async Task CreateItemAsync_DuplicateName_FailsPointingAtTheItem()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(new SceneAsset { Id = "a1", Name = "Yellow sundress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Wardrobe.CreateItemAsync("  yellow sundress  "));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole point of "use as reference": the operator never fills in a form. The platform's provenance fields are
    /// written for them, because a feature that asked for a license label would not be the feature they asked for.
    /// </summary>
    [Fact]
    public async Task SetImageInUseAsync_WritesTheProvenanceItself()
    {
        var image = CompleteImage();
        var repository = new RecordingSceneAssetRepository(image);
        var world = new World(repository);

        await world.Wardrobe.SetImageInUseAsync(image.Id, inUse: true);

        var approval = Assert.Single(repository.ImageApprovals);
        Assert.Equal(image.Id, approval.ImageId);
        Assert.Equal(SceneAssetConsentState.NotApplicable, approval.ConsentState);
        Assert.Equal(SceneAssetLicenseState.NotApplicable, approval.LicenseState);
        Assert.Equal(WardrobeItemService.UseAsReferenceLicenseLabel, approval.LicenseLabel);
        Assert.Equal(WardrobeItemService.UseAsReferencePolicyKey, approval.ContentPolicyKey);
        Assert.True(approval.ApprovedUseScope.HasFlag(SceneAssetApprovedUseScope.CharacterWardrobe));
        Assert.True(approval.ApprovedUseScope.HasFlag(SceneAssetApprovedUseScope.ProductionSource));
    }

    [Fact]
    public async Task SetImageInUseAsync_StopUsing_ReturnsTheImageToDraft()
    {
        var image = CompleteImage();
        image.ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved;
        image.ProductionVersion = 1;
        var repository = new RecordingSceneAssetRepository(image);
        var world = new World(repository);

        await world.Wardrobe.SetImageInUseAsync(image.Id, inUse: false);

        Assert.Equal(SceneAssetProductionApprovalStatus.Draft, image.ProductionApprovalStatus);
        Assert.Empty(repository.ImageApprovals);
        Assert.Contains(image, repository.UpsertedImages);
    }

    [Fact]
    public async Task ListItemsAsync_ReportsWhichImagesAreUsable()
    {
        var usable = CompleteImage("img-usable");
        usable.ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved;
        usable.ProductionVersion = 1;
        var draft = CompleteImage("img-draft");
        var repository = new RecordingSceneAssetRepository(usable, draft);
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        var item = Assert.Single(await world.Wardrobe.ListItemsAsync());

        Assert.True(item.IsUsable);
        Assert.Equal("img-usable", Assert.Single(item.InUseImages).Image.Id);
        Assert.Equal(2, item.Images.Count);
    }

    /// <summary>
    /// Items are SHARED. A wardrobe item another character created is listed here too, which is what lets the same
    /// dress be worn by a second character without duplicating it.
    /// </summary>
    [Fact]
    public async Task ListItemsAsync_ListsItemsOwnedByAnyone()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(
            new SceneAsset { Id = "a1", Name = "Becky dress", Type = SceneAssetType.Wardrobe, CharacterProfileId = "p-becky" },
            new SceneAsset { Id = "a2", Name = "Dean shirt", Type = SceneAssetType.Wardrobe, CharacterProfileId = "p-dean" },
            new SceneAsset { Id = "a3", Name = "Shared coat", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        var items = await world.Wardrobe.ListItemsAsync();

        Assert.Equal(3, items.Count);
        Assert.Contains(items, item => item.Asset.Id == "a2");
    }

    [Fact]
    public async Task ListItemsAsync_IgnoresNonWardrobeAssets()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(
            new SceneAsset { Id = "a1", Name = "Bedroom", Type = SceneAssetType.Location },
            new SceneAsset { Id = "a2", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        Assert.Equal("a2", Assert.Single(await world.Wardrobe.ListItemsAsync()).Asset.Id);
    }

    [Fact]
    public async Task SetImageLabelAsync_StoresTheLabelOnTheImage()
    {
        var image = CompleteImage();
        var repository = new RecordingSceneAssetRepository(image);
        var world = new World(repository);

        await world.Wardrobe.SetImageLabelAsync(image.Id, " front ");

        Assert.Equal("front", image.CandidateNotes);
        Assert.Contains(image, repository.UpsertedImages);
    }

    [Fact]
    public async Task EnqueueItemImagesAsync_QueuesOneJobPerRequestedImage()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        await world.Wardrobe.EnqueueItemImagesAsync("a1", "a yellow sundress", QwenModelId, "896x1152", outputCount: 3);

        Assert.Equal(3, world.Queue.Enqueued.Count);
        Assert.All(world.Queue.Enqueued, job => Assert.Equal(BackgroundJobTypes.WardrobeItemPromptGeneration, job.JobType));
        Assert.All(world.Queue.Enqueued, job => Assert.Equal(DurableJobLane.PromptCompilation, job.Lane));
        // Independent attempts of one item, so they must not collapse onto a single job key.
        Assert.Equal(3, world.Queue.Enqueued.Select(job => job.DedupeKey).Distinct(StringComparer.Ordinal).Count());
        Assert.All(world.Queue.Enqueued, job => Assert.Contains("a yellow sundress", job.PayloadJson, StringComparison.Ordinal));
        // One ROW per requested image, created now: the operator sees three attempts immediately rather than nothing
        // until a prompt has been drafted, and each job names the row it will fill in.
        Assert.Equal(3, repository.UpsertedImages.Count);
        Assert.All(repository.UpsertedImages, image => Assert.Null(image.PromptCompilerId));
        Assert.All(repository.UpsertedImages, image => Assert.Equal(SceneAssetStatus.Pending, image.Status));
        Assert.All(repository.UpsertedImages, image => Assert.Contains(image.Id, string.Join("|", world.Queue.Enqueued.Select(job => job.PayloadJson)), StringComparison.Ordinal));
    }

    /// <summary>
    /// A model the tab cannot compile for is refused BEFORE anything is queued or created: the failure is the
    /// operator's to fix (pick another model), and spending a queue slot to tell them so is worse than saying it now.
    /// </summary>
    [Fact]
    public async Task EnqueueItemImagesAsync_UnsupportedModel_FailsBeforeQueueing()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository, imageModel: ImageModel(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Wardrobe.EnqueueItemImagesAsync("a1", "a yellow sundress", QwenModelId, "896x1152", outputCount: 1));

        Assert.Contains("No wardrobe-item prompt compiler", exception.Message, StringComparison.Ordinal);
        Assert.Empty(world.Queue.Enqueued);
        Assert.Empty(repository.UpsertedImages);
    }

    [Fact]
    public async Task EnqueueItemImagesAsync_BlankDescription_Fails()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Wardrobe.EnqueueItemImagesAsync("a1", "  ", QwenModelId, "896x1152", outputCount: 1));

        Assert.Contains("Describe the garment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListModelsAsync_OffersOnlyModelsTheTabCanCompileFor()
    {
        var repository = new RecordingSceneAssetRepository();
        var world = new World(repository, models:
        [
            new SceneImageModelChoice("m-qwen", "Qwen-Image-2.1", "qwen.safetensors", "Local", false)
            {
                Family = SceneImageModelFamily.QwenImage21,
                Dialect = SceneImagePromptDialect.NaturalLanguage
            },
            new SceneImageModelChoice("m-pony", "Pony V6", "pony.safetensors", "Local", false)
            {
                Family = SceneImageModelFamily.Pony,
                Dialect = SceneImagePromptDialect.PonyV6Tags
            }
        ]);

        Assert.Equal("m-qwen", Assert.Single(await world.Wardrobe.ListModelsAsync()).ModelId);
    }

    [Fact]
    public async Task DefaultModelIdAsync_ReturnsTheConfiguredFunctionDefault()
    {
        var repository = new RecordingSceneAssetRepository();
        var world = new World(repository, functionDefault: new FunctionModelDefault
        {
            Id = "fd-1",
            FunctionName = nameof(AppFunction.RolePlayWardrobeItem),
            ModelId = QwenModelId
        });

        Assert.Equal(QwenModelId, await world.Wardrobe.DefaultModelIdAsync());
    }

    /// <summary>
    /// An image that is IN USE as a reference image is not deletable, and that rule lives in the ONE service every
    /// asset-image delete goes through. Its approval is what a reference picker reads and what a render pinned, so
    /// deleting the bytes behind it would leave bound references naming an image that no longer exists.
    /// </summary>
    [Fact]
    public async Task DeleteImageAsync_RefusesAnImageThatIsInUse()
    {
        var image = CompleteImage();
        image.ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved;
        var repository = new RecordingSceneAssetRepository(image);
        var world = new World(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Wardrobe.DeleteImageAsync(image.Id));

        Assert.Contains("in use", exception.Message, StringComparison.Ordinal);
        Assert.Empty(repository.DeletedImages);
    }

    [Fact]
    public async Task DeleteImageAsync_AllowsAnUnusedImage()
    {
        var image = CompleteImage();
        var repository = new RecordingSceneAssetRepository(image);
        var world = new World(repository);

        await world.Wardrobe.DeleteImageAsync(image.Id);

        Assert.Equal([image.Id], repository.DeletedImages);
    }

    /// <summary>
    /// A prompt the operator edited is stored VERBATIM and rendered as a new image, with the compiler named as the
    /// hand-edited one. New row rather than an overwrite: the stored prompt is what an existing image was made from.
    /// </summary>
    [Fact]
    public async Task RenderPromptAsync_StoresTheEditedPromptVerbatimAndQueuesTheRender()
    {
        var source = CompleteImage();
        var repository = new RecordingSceneAssetRepository(source);
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);
        const string edited = "A product photograph of a yellow cotton sundress laid flat on a plain light-grey surface, "
            + "sleeveless with a scoop neckline and a full gathered skirt, even soft diffused lighting from above, sharp "
            + "focus, photorealistic e-commerce product photography, the entire dress centred and fully in frame, "
            + "no person, no hanger, no props.";

        var image = await world.Wardrobe.RenderPromptAsync(
            "a1", edited, QwenModelId, "896x1152",
            WardrobeItemService.HandEditedPromptCompilerId, handEditedFromImageId: source.Id);

        Assert.Equal(edited, image.Prompt);
        Assert.Equal(WardrobeItemService.HandEditedPromptCompilerId, image.PromptCompilerId);
        Assert.Equal(source.Id, image.SourceImageId);
        Assert.NotEqual(source.Id, image.Id);
        Assert.Contains(image, repository.UpsertedImages);

        var renderJob = Assert.Single(world.Queue.Enqueued);
        Assert.Equal(BackgroundJobTypes.SceneAssetGeneration, renderJob.JobType);
        Assert.Contains(image.Id, renderJob.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderPromptAsync_BlankPrompt_Fails()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Wardrobe.RenderPromptAsync("a1", "  ", QwenModelId, "896x1152", "x"));

        Assert.Contains("prompt is required", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(world.Queue.Enqueued);
    }

    /// <summary>
    /// The compile job's whole point: the prompt it drafted is written ON the row that already existed, with the
    /// compiler named, and that row is then queued to render. The render path uses a prompt verbatim exactly when the
    /// row names its compiler, so the two facts have to land together.
    /// </summary>
    [Fact]
    public async Task Handler_WritesTheCompiledPromptOntoTheRowAndQueuesTheRender()
    {
        var pending = PendingItemImage();
        var repository = new RecordingSceneAssetRepository(pending);
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);
        var handler = world.CreateHandler(new StubCompletionClient(new string('z', 400)));

        await handler.HandleAsync(world.Job(pending.Id));

        Assert.Equal(pending.Id, Assert.Single(repository.CompiledPrompts));
        Assert.Equal(400, pending.Prompt.Length);
        Assert.Equal("wardrobe-item-qwen-image-21-natural-language", pending.PromptCompilerId);
        // 2.1 runs cfg 1 where the negative is inert, and this compiler authors none: null means "none authored", which
        // must stay distinguishable from "the author chose an empty one".
        Assert.Null(pending.NegativePrompt);
        Assert.Contains("a yellow sundress", pending.AssociationMetadataJson, StringComparison.Ordinal);

        var renderJob = Assert.Single(world.Queue.Enqueued);
        Assert.Equal(BackgroundJobTypes.SceneAssetGeneration, renderJob.JobType);
        Assert.Equal(DurableJobLane.ImageRender, renderJob.Lane);
        Assert.Contains(pending.Id, renderJob.PayloadJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// A compile that fails is written WHERE THE OPERATOR IS LOOKING. Before this, a refused prompt left the tab saying
    /// "No images yet" - a real error that looked exactly like a dead button (reported 2026-09-29).
    /// </summary>
    [Fact]
    public async Task Handler_UnsupportedModel_MarksTheRowFailedWithTheReason()
    {
        var pending = PendingItemImage();
        var repository = new RecordingSceneAssetRepository(pending);
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(
            repository,
            imageModel: ImageModel(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags));
        var handler = world.CreateHandler(new StubCompletionClient(new string('z', 400)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(world.Job(pending.Id)));

        Assert.Contains("No wardrobe-item prompt compiler", exception.Message, StringComparison.Ordinal);
        Assert.Equal(SceneAssetStatus.Failed, pending.Status);
        Assert.Contains("No wardrobe-item prompt compiler", pending.ErrorMessage!, StringComparison.Ordinal);
        Assert.Empty(world.Queue.Enqueued);
        Assert.Empty(repository.CompiledPrompts);
    }

    /// <summary>A detail that trips the compiler's own rules is reported on the row too, not swallowed.</summary>
    [Fact]
    public async Task Handler_PromptRefusedByTheCompiler_MarksTheRowFailed()
    {
        var pending = PendingItemImage();
        var repository = new RecordingSceneAssetRepository(pending);
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);
        var drafted = "A product photograph of women's shorts laid flat on a plain light-grey surface, worn by a woman, "
            + "with a five centimetre inseam and a wide leg opening, even diffused lighting from above, sharp focus, "
            + "photorealistic e-commerce product photography, the whole garment centred and filling the frame.";
        var handler = world.CreateHandler(new StubCompletionClient(drafted));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(world.Job(pending.Id)));

        Assert.Equal(SceneAssetStatus.Failed, pending.Status);
        Assert.Contains("does not contain", pending.ErrorMessage!, StringComparison.Ordinal);
        Assert.Empty(world.Queue.Enqueued);
    }

    /// <summary>
    /// "Generate prompt" drafts the text and STOPS: the row holds the prompt for the operator to read and correct, and
    /// nothing is rendered until they ask. That is the whole point of splitting the button in two.
    /// </summary>
    [Fact]
    public async Task Handler_RenderWhenCompiledFalse_HoldsThePromptForReview()
    {
        var pending = PendingItemImage();
        var repository = new RecordingSceneAssetRepository(pending);
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);
        var handler = world.CreateHandler(new StubCompletionClient(new string('z', 400)));

        await handler.HandleAsync(world.Job(pending.Id, renderWhenCompiled: false));

        Assert.Equal(pending.Id, Assert.Single(repository.CompiledPrompts));
        Assert.Equal(SceneAssetStatus.Pending, pending.Status);
        Assert.Contains("prompt-ready", pending.AssociationMetadataJson, StringComparison.Ordinal);
        Assert.Empty(world.Queue.Enqueued);
    }

    [Fact]
    public async Task EnqueuePromptDraftAsync_CreatesOneRowAndAsksForNoRender()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        var row = await world.Wardrobe.EnqueuePromptDraftAsync("a1", "a yellow sundress", QwenModelId, "896x1152");

        Assert.Equal(row.Id, Assert.Single(repository.UpsertedImages).Id);
        Assert.Null(row.PromptCompilerId);
        var job = Assert.Single(world.Queue.Enqueued);
        Assert.Equal(BackgroundJobTypes.WardrobeItemPromptGeneration, job.JobType);
        Assert.Contains("\"renderWhenCompiled\":false", job.PayloadJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Render image" on a row that already carries a compiled prompt renders THAT row, and records that it is now
    /// rendering. A row with no compiler named is refused: the render could not tell a model-ready prompt from a
    /// description awaiting compilation.
    /// </summary>
    [Fact]
    public async Task RenderExistingImageAsync_QueuesTheRenderAndRefusesAnUncompiledRow()
    {
        var compiled = PendingItemImage();
        compiled.Prompt = new string('z', 400);
        compiled.PromptCompilerId = "wardrobe-item-qwen-image-21-natural-language";
        var uncompiled = PendingItemImage("img-uncompiled");
        var repository = new RecordingSceneAssetRepository(compiled, uncompiled);
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        await world.Wardrobe.RenderExistingImageAsync(compiled.Id);

        Assert.Contains("rendering", compiled.AssociationMetadataJson, StringComparison.Ordinal);
        var job = Assert.Single(world.Queue.Enqueued);
        Assert.Equal(BackgroundJobTypes.SceneAssetGeneration, job.JobType);
        Assert.Contains(compiled.Id, job.PayloadJson, StringComparison.Ordinal);
        // The render uses the model the row was ASKED for, read back from its own record.
        Assert.Contains(QwenModelId, job.PayloadJson, StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Wardrobe.RenderExistingImageAsync(uncompiled.Id));
        Assert.Contains("does not name the compiler", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pressing "Generate prompt" twice leaves ONE draft to read, not a pile: an unrendered draft is a prompt waiting for
    /// the operator, so a new draft supersedes it. A draft that HAS been rendered is left alone - it is the record of an
    /// image, not a draft.
    /// </summary>
    [Fact]
    public async Task EnqueuePromptDraftAsync_SecondDraft_ReplacesTheUnrenderedOne()
    {
        var repository = new RecordingSceneAssetRepository();
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        var first = await world.Wardrobe.EnqueuePromptDraftAsync("a1", "a yellow sundress", QwenModelId, "896x1152");
        var second = await world.Wardrobe.EnqueuePromptDraftAsync("a1", "a blue sundress", QwenModelId, "896x1152");

        Assert.Equal([first.Id], repository.DeletedImages);
        Assert.NotEqual(first.Id, second.Id);
        var rows = (await world.Wardrobe.ListItemsAsync()).Single().Images;
        var row = Assert.Single(rows);
        Assert.True(row.IsPromptDraft);
        Assert.Equal(second.Id, row.Image.Id);
    }

    /// <summary>
    /// A prompt draft is reported as a PROMPT, not as an attempt at an image: that is what the host needs to keep it out
    /// of the image list (operator report 2026-09-29: pressing "Generate prompt" looked like an image generation had
    /// started, because an empty card appeared for it).
    /// </summary>
    [Fact]
    public async Task ListItemsAsync_MarksPromptDraftsSeparatelyFromImages()
    {
        var draft = PendingItemImage("img-draft");
        draft.PromptCompilerId = "wardrobe-item-qwen-image-21-natural-language";
        draft.AssociationMetadataJson = "{\"source\":\"wardrobe-item\",\"stage\":\"prompt-ready\"}";
        var rendered = CompleteImage("img-rendered");
        rendered.AssociationMetadataJson = "{\"source\":\"wardrobe-item\",\"stage\":\"rendering\"}";
        var repository = new RecordingSceneAssetRepository(draft, rendered);
        repository.Seed(new SceneAsset { Id = "a1", Name = "Dress", Type = SceneAssetType.Wardrobe });
        var world = new World(repository);

        var item = Assert.Single(await world.Wardrobe.ListItemsAsync());

        Assert.True(item.Images.Single(entry => entry.Image.Id == "img-draft").IsPromptDraft);
        Assert.False(item.Images.Single(entry => entry.Image.Id == "img-rendered").IsPromptDraft);
    }

    private static ResolvedImageModel ImageModel(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect) => new(
        "http://localhost:8188",
        "/prompt",
        300,
        null,
        "qwen_image_2.1_int8_convrot.safetensors",
        ImageContentPolicy.AdultAllowed,
        "Local ComfyUI",
        false,
        family,
        dialect,
        ImageProtocol.ComfyUi);

    private static SceneAssetImage CompleteImage(string id = "img-1") => new()
    {
        Id = id,
        AssetId = "a1",
        Kind = SceneAssetKind.PromptGenerated,
        Status = SceneAssetStatus.Complete,
        FileRelativePath = $"assets/{id}.png",
        MediaType = "image/png",
        ByteLength = 1024,
        Sha256 = new string('A', 64)
    };

    /// <summary>
    /// The row the tab creates when the operator presses Generate: pending, carrying the description they typed, and
    /// with NO compiler named yet (which is what says "this text is not model-ready").
    /// </summary>
    private static SceneAssetImage PendingItemImage(string id = "img-pending") => new()
    {
        Id = id,
        AssetId = "a1",
        Kind = SceneAssetKind.PromptGenerated,
        Status = SceneAssetStatus.Pending,
        Prompt = "a yellow sundress",
        PromptCompilerId = null,
        MediaType = "image/png",
        AssociationMetadataJson = $"{{\"source\":\"wardrobe-item\",\"stage\":\"compiling\","
            + $"\"requestedModelId\":\"{QwenModelId}\",\"imageSize\":\"896x1152\"}}"
    };

    /// <summary>The service graph under test, wired exactly as the app wires it.</summary>
    private sealed class World
    {
        public World(
            RecordingSceneAssetRepository repository,
            ResolvedImageModel? imageModel = null,
            IReadOnlyList<SceneImageModelChoice>? models = null,
            FunctionModelDefault? functionDefault = null)
        {
            Repository = repository;
            Queue = new CapturingQueue();
            ImageModel = imageModel ?? ImageModel(SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage);

            var assetService = new SceneAssetService(
                repository,
                new StubStorage(),
                Queue,
                new StubDurableSettings(),
                TimeProvider.System,
                NullLogger<SceneAssetService>.Instance);

            Wardrobe = new WardrobeItemService(
                repository,
                assetService,
                new StubModelResolution(ImageModel, models ?? []),
                new StubFunctionDefaults(functionDefault),
                new WardrobeItemPromptCompilerRegistry(
                [
                    new NaturalLanguageWardrobeItemPromptCompiler(SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage)
                ]),
                Queue,
                new StubDurableSettings(),
                TimeProvider.System,
                NullLogger<WardrobeItemService>.Instance);
        }

        public RecordingSceneAssetRepository Repository { get; }

        public CapturingQueue Queue { get; }

        public ResolvedImageModel ImageModel { get; }

        public IWardrobeItemService Wardrobe { get; }

        public WardrobeItemPromptGenerationJobHandler CreateHandler(ICompletionClient completionClient) => new(
            Repository,
            new StubModelResolution(ImageModel, []),
            new WardrobeItemPromptCompilerRegistry(
            [
                new NaturalLanguageWardrobeItemPromptCompiler(SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage)
            ]),
            Wardrobe,
            completionClient,
            NullLogger<WardrobeItemPromptGenerationJobHandler>.Instance);

        /// <summary>The job the service would have queued for one garment image.</summary>
        public DurableBackgroundJob Job(string imageId, bool renderWhenCompiled = true) => new()
        {
            Id = "job-1",
            JobType = BackgroundJobTypes.WardrobeItemPromptGeneration,
            Lane = DurableJobLane.PromptCompilation,
            DedupeKey = "job-1",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(
                new WardrobeItemPromptJobPayload
                {
                    AssetId = "a1",
                    ImageId = imageId,
                    ItemDescription = "a yellow sundress",
                    ModelId = QwenModelId,
                    ImageSize = "896x1152",
                    RenderWhenCompiled = renderWhenCompiled
                },
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        };
    }

    /// <summary>
    /// The prompt-drafting model, stubbed: the compile job's contract is what it DOES with the drafted text, not how the
    /// text was produced.
    /// </summary>
    private sealed class StubCompletionClient(string response) : ICompletionClient
    {
        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
            string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult((response, (string?)null));

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
            string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult((response, (string?)null));

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
            string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
            string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult(response);

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult(response);

        public Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult((true, "ok"));
    }

    private sealed record ImageApproval(
        string ImageId,
        SceneAssetConsentState ConsentState,
        SceneAssetLicenseState LicenseState,
        string LicenseLabel,
        SceneAssetApprovedUseScope ApprovedUseScope,
        string ContentPolicyKey);

    private sealed class CapturingQueue : IDurableBackgroundJobQueue
    {
        public List<DurableBackgroundJob> Enqueued { get; } = [];

        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(job);
            return Task.FromResult(true);
        }

        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(Enqueued.SingleOrDefault(job => job.Id == jobId));

        public Task<bool> TryActivateAsync(string jobId, DateTime activatedUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(Enqueued.Any(job => job.Id == jobId));

        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task WaitForWorkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubDurableSettings : ISceneBeatAnalyzerResolver
    {
        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedSceneBeatAnalyzer(
                "fd-1",
                "m-1",
                "p-1",
                new ResolvedModel("http://localhost", "/chat/completions", 60, null, "test-prompt-model", 0.7, 0.9, 8000, "Local", false),
                StructuredOutputMode.JsonObject,
                null,
                null,
                1,
                30,
                100,
                [5, 15],
                7,
                100));
    }

    private sealed class StubFunctionDefaults(FunctionModelDefault? value) : IFunctionDefaultRepository
    {
        public Task<FunctionModelDefault?> GetByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default)
            => Task.FromResult(function == AppFunction.RolePlayWardrobeItem ? value : null);

        public Task<FunctionModelDefault> SaveAsync(FunctionModelDefault functionDefault, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<FunctionModelDefault>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(value is null ? new List<FunctionModelDefault>() : new List<FunctionModelDefault> { value });

        public Task<List<FunctionModelDefault>> GetByModelIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<FunctionModelDefault>());

        public Task<bool> DeleteByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class StubModelResolution(
        ResolvedImageModel image,
        IReadOnlyList<SceneImageModelChoice> models) : IModelResolutionService
    {
        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(
            bool identityCapableOnly, CancellationToken cancellationToken = default)
            => Task.FromResult(models);

        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(image);

        public Task<ResolvedModel> ResolveAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(PromptModel());

        public Task<ResolvedModel> ResolveImagePromptModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(PromptModel());

        public Task<ResolvedImageModel> ResolveImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedImageModel> ResolveImagePromptGenerationModelAsync(
            SceneImagePromptStyle promptStyle, string? preferredModelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private static ResolvedModel PromptModel() => new(
            "http://localhost", "/chat/completions", 60, null, "test-prompt-model", 0.7, 0.9, 8000, "Local", false);
    }

    private sealed class StubStorage : ISceneAssetStorageService
    {
        public Task<StoredSceneAsset> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string fileRelativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string fileRelativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingSceneAssetRepository(params SceneAssetImage[] images) : ISceneAssetRepository
    {
        private readonly List<SceneAssetImage> _images = [.. images];

        public List<SceneAsset> Assets { get; } = [];

        public List<ImageApproval> ImageApprovals { get; } = [];

        public List<SceneAssetImage> UpsertedImages { get; } = [];

        public List<string> DeletedImages { get; } = [];

        public List<string> CompiledPrompts { get; } = [];

        public void Seed(params SceneAsset[] assets) => Assets.AddRange(assets);

        public Task<SceneAsset?> GetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult(Assets.FirstOrDefault(asset => asset.Id == assetId));

        public Task<IReadOnlyList<SceneAsset>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAsset>>(Assets);

        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult(_images.FirstOrDefault(image => image.Id == imageId));

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(_images.Where(image => image.AssetId == assetId).ToList());

        public Task UpsertImageAsync(SceneAssetImage image, CancellationToken cancellationToken = default)
        {
            UpsertedImages.Add(image);
            _images.RemoveAll(existing => existing.Id == image.Id);
            _images.Add(image);
            return Task.CompletedTask;
        }

        public Task SetImagePromptAsync(
            string imageId, string prompt, string promptCompilerId, string? negativePrompt,
            string? associationMetadataJson, CancellationToken cancellationToken = default)
        {
            var image = _images.Single(candidate => candidate.Id == imageId);
            image.Prompt = prompt;
            image.PromptCompilerId = promptCompilerId;
            image.NegativePrompt = negativePrompt;
            image.AssociationMetadataJson = associationMetadataJson;
            CompiledPrompts.Add(imageId);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(SceneAsset asset, CancellationToken cancellationToken = default)
        {
            Assets.RemoveAll(existing => existing.Id == asset.Id);
            Assets.Add(asset);
            return Task.CompletedTask;
        }

        public Task<SceneAssetImage> ApproveImageForProductionAsync(
            string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState,
            SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope,
            string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
        {
            ImageApprovals.Add(new ImageApproval(imageId, consentState, licenseState, licenseLabel, approvedUseScope, contentPolicyKey));
            var image = _images.Single(candidate => candidate.Id == imageId);
            image.ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved;
            image.ProductionVersion ??= 1;
            image.SourceProvenanceJson = sourceProvenanceJson;
            return Task.FromResult(image);
        }

        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
        {
            DeletedImages.Add(imageId);
            _images.RemoveAll(image => image.Id == imageId);
            return Task.CompletedTask;
        }

        public Task<int> CountByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<SceneAsset>> ListByPackAsync(string identityPackId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAsset>> ListByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateCandidateFieldsAsync(string assetId, string? candidateBatchId, SceneAssetCandidateDecision? candidateDecision, string? candidateNotes, string? candidateSourceAssetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CreatePromotedAsync(SceneAsset asset, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RenameAsync(string assetId, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
