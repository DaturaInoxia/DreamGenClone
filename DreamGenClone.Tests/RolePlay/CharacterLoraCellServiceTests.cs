using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The render half of the cell workspace. Two things matter and are pinned here: a cell can only ever see its OWN
/// attempts (the batch id is derived from the dataset and the cell key, never shared), and a render is refused
/// before it is queued when the prompt or the model is missing rather than dispatched with a substitute.
/// </summary>
public sealed class CharacterLoraCellServiceTests
{
    private const string DatasetId = "dataset-1";
    private const string CellKey = "core.front.cu.1";
    private const string PackId = "pack-1";

    // ------------------------------------------------------------------ the batch boundary

    [Fact]
    public void CellBatchIdFor_IsStableAndDistinctPerCell()
    {
        var service = Build();

        var first = service.CellBatchIdFor(DatasetId, CellKey);

        Assert.Equal(first, service.CellBatchIdFor(DatasetId, CellKey));
        Assert.NotEqual(first, service.CellBatchIdFor(DatasetId, "core.front.cu.2"));
        Assert.NotEqual(first, service.CellBatchIdFor("dataset-2", CellKey));
        Assert.StartsWith(CharacterLoraCellService.CellBatchPrefix, first, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cell's deck must never show another pipeline's images: the front, angle and body batches all use different
    /// prefixes, and this batch id may not collide with any of them.
    /// </summary>
    [Fact]
    public void CellBatchIdFor_DoesNotCollideWithTheOtherPipelines()
    {
        var batchId = Build().CellBatchIdFor(DatasetId, CellKey);

        Assert.DoesNotContain("front-", batchId, StringComparison.Ordinal);
        Assert.DoesNotContain("angle-", batchId, StringComparison.Ordinal);
        Assert.DoesNotContain("body-", batchId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", CellKey)]
    [InlineData("   ", CellKey)]
    [InlineData(DatasetId, "")]
    [InlineData(DatasetId, "   ")]
    public void CellBatchIdFor_RequiresBothHalves(string datasetId, string cellKey)
    {
        Assert.Throws<InvalidOperationException>(() => Build().CellBatchIdFor(datasetId, cellKey));
    }

    // ------------------------------------------------------------------ the identity decision

    /// <summary>
    /// The cell is conditioned on the reference for ITS OWN angle. An angled cell conditioned on the frontal
    /// reference trains the trigger token on a face that does not match the view it was learned from.
    /// </summary>
    [Fact]
    public void ResolveIdentityConditioning_UsesTheReferenceForTheCellsOwnAngle()
    {
        var record = Cell(SceneImageReferenceFaceView.ProfileLeft);
        var assets = new[]
        {
            FaceReference("front-ref", SceneImageReferenceFaceView.Front),
            FaceReference("profile-ref", SceneImageReferenceFaceView.ProfileLeft)
        };

        var conditioning = CharacterLoraCellService.ResolveIdentityConditioning(record, PackId, assets);

        Assert.NotNull(conditioning);
        Assert.Equal(PackId, conditioning!.PackId);
        Assert.Equal("profile-ref", conditioning.FaceAssetId);
    }

    /// <summary>
    /// A view from behind has no face to hold, so it carries no identity conditioning. Passing one would tell the
    /// render path to hold a face the frame is not supposed to contain.
    /// </summary>
    [Fact]
    public void ResolveIdentityConditioning_ForAViewWithNoFace_IsNull()
    {
        var record = Cell(slot: null);
        var assets = new[] { FaceReference("front-ref", SceneImageReferenceFaceView.Front) };

        Assert.Null(CharacterLoraCellService.ResolveIdentityConditioning(record, PackId, assets));
    }

    /// <summary>
    /// A missing reference for the angle is a hard error naming the pack and the angle. Substituting another angle,
    /// or rendering unconditioned, puts a face that is not this character's into the training set under its token.
    /// </summary>
    [Fact]
    public void ResolveIdentityConditioning_RefusesAnUnguardedAngle_RatherThanFallingBack()
    {
        var record = Cell(SceneImageReferenceFaceView.ProfileRight);
        var assets = new[] { FaceReference("front-ref", SceneImageReferenceFaceView.Front) };

        var error = Assert.Throws<InvalidOperationException>(
            () => CharacterLoraCellService.ResolveIdentityConditioning(record, PackId, assets));

        Assert.Contains(PackId, error.Message, StringComparison.Ordinal);
        Assert.Contains("ProfileRight", error.Message, StringComparison.Ordinal);
        Assert.Contains("Faces tab", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An unapproved reference is not a reference: the render path re-reads the pack and would refuse it.</summary>
    [Fact]
    public void ResolveIdentityConditioning_IgnoresUnapprovedReferences()
    {
        var record = Cell(SceneImageReferenceFaceView.Front);
        var assets = new[] { FaceReference("draft-ref", SceneImageReferenceFaceView.Front, approved: false) };

        Assert.Throws<InvalidOperationException>(
            () => CharacterLoraCellService.ResolveIdentityConditioning(record, PackId, assets));
    }

    /// <summary>When a pack holds more than one approved reference for an angle, the newest is the one in force.</summary>
    [Fact]
    public void ResolveIdentityConditioning_PrefersTheNewestApprovedReference()
    {
        var record = Cell(SceneImageReferenceFaceView.Front);
        var assets = new[]
        {
            FaceReference("older", SceneImageReferenceFaceView.Front, createdUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            FaceReference("newer", SceneImageReferenceFaceView.Front, createdUtc: new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc))
        };

        Assert.Equal("newer", CharacterLoraCellService.ResolveIdentityConditioning(record, PackId, assets)!.FaceAssetId);
    }

    /// <summary>A body reference is not a face reference, however well it matches the angle.</summary>
    [Fact]
    public void ResolveIdentityConditioning_IgnoresBodyReferences()
    {
        var record = Cell(SceneImageReferenceFaceView.Front);
        var assets = new[]
        {
            new SceneImageReferenceAsset
            {
                Id = "body-ref",
                AssetKind = SceneImageReferenceAssetKind.FullBody,
                BodyView = SceneImageReferenceBodyView.Front,
                BodyState = SceneImageReferenceBodyState.Clothed,
                IsApproved = true
            }
        };

        Assert.Throws<InvalidOperationException>(
            () => CharacterLoraCellService.ResolveIdentityConditioning(record, PackId, assets));
    }

    [Fact]
    public void ResolveIdentityConditioning_RequiresAPack()
    {
        var record = Cell(SceneImageReferenceFaceView.Front);

        Assert.Throws<InvalidOperationException>(
            () => CharacterLoraCellService.ResolveIdentityConditioning(record, "  ", []));
    }

    // ------------------------------------------------------------------ the body decision

    /// <summary>
    /// The cell is conditioned on the body reference ITS OWN rule names — the canonical slot the plan recorded and
    /// the state the cell depicts. This is what keeps the character's build out of the model's imagination.
    /// </summary>
    [Fact]
    public void ResolveBodyConditioning_UsesTheSlotAndStateTheCellNames()
    {
        var record = Cell(SceneImageReferenceFaceView.ProfileLeft);
        var assets = new[]
        {
            BodyReference("front-clothed", SceneImageReferenceBodyView.Front, SceneImageReferenceBodyState.Clothed),
            BodyReference("profile-clothed", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Clothed)
        };

        var conditioning = CharacterLoraCellService.ResolveBodyConditioning(record, PackId, assets);

        Assert.Equal(PackId, conditioning.PackId);
        Assert.Equal("profile-clothed", conditioning.BodyAssetId);
    }

    /// <summary>
    /// State is matched, not merely the slot. A reference IMAGE carries its state with it — verified 2026-09-23, a
    /// bare-shouldered reference made a clothed render come out unclothed — so handing this clothed cell the
    /// unclothed reference of the same slot is the exact defect this rule exists to prevent.
    /// </summary>
    [Fact]
    public void ResolveBodyConditioning_NeverHandsAClothedCellTheUnclothedReference()
    {
        var record = Cell(SceneImageReferenceFaceView.ProfileLeft);
        var assets = new[]
        {
            BodyReference("profile-unclothed", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Unclothed),
            BodyReference("profile-clothed", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Clothed)
        };

        Assert.Equal(
            "profile-clothed",
            CharacterLoraCellService.ResolveBodyConditioning(record, PackId, assets).BodyAssetId);
    }

    /// <summary>A state the pack does not hold is a hard error, never a same-slot substitute.</summary>
    [Fact]
    public void ResolveBodyConditioning_RefusesAMissingStateRatherThanSubstitutingTheOtherOne()
    {
        var record = Cell(SceneImageReferenceFaceView.ProfileLeft);
        var assets = new[]
        {
            BodyReference("profile-unclothed", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Unclothed)
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => CharacterLoraCellService.ResolveBodyConditioning(record, PackId, assets));

        Assert.Contains(PackId, error.Message, StringComparison.Ordinal);
        Assert.Contains("ProfileLeft", error.Message, StringComparison.Ordinal);
        Assert.Contains("Clothed", error.Message, StringComparison.Ordinal);
        Assert.Contains("Body tab", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unlike the face, the body decision is ALWAYS present: the back view is a canonical slot, so a frame with no
    /// face in it still conditions on the character's build.
    /// </summary>
    [Fact]
    public void ResolveBodyConditioning_ServesAViewWithNoFaceFromTheBackSlot()
    {
        var record = Cell(slot: null);
        var assets = new[]
        {
            BodyReference("back-clothed", SceneImageReferenceBodyView.Back, SceneImageReferenceBodyState.Clothed)
        };

        Assert.Equal(
            "back-clothed",
            CharacterLoraCellService.ResolveBodyConditioning(record, PackId, assets).BodyAssetId);
    }

    /// <summary>An unapproved reference is not a reference: the render path re-reads the pack and would refuse it.</summary>
    [Fact]
    public void ResolveBodyConditioning_IgnoresUnapprovedReferences()
    {
        var record = Cell(SceneImageReferenceFaceView.ProfileLeft);
        var assets = new[]
        {
            BodyReference("draft-body", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Clothed, approved: false)
        };

        Assert.Throws<InvalidOperationException>(
            () => CharacterLoraCellService.ResolveBodyConditioning(record, PackId, assets));
    }

    /// <summary>A face reference is not a body reference, however well its view matches.</summary>
    [Fact]
    public void ResolveBodyConditioning_IgnoresFaceReferences()
    {
        var record = Cell(SceneImageReferenceFaceView.ProfileLeft);
        var assets = new[] { FaceReference("profile-ref", SceneImageReferenceFaceView.ProfileLeft) };

        Assert.Throws<InvalidOperationException>(
            () => CharacterLoraCellService.ResolveBodyConditioning(record, PackId, assets));
    }

    /// <summary>When a pack holds more than one approved reference for a slot and state, the newest is in force.</summary>
    [Fact]
    public void ResolveBodyConditioning_PrefersTheNewestApprovedReference()
    {
        var record = Cell(SceneImageReferenceFaceView.ProfileLeft);
        var assets = new[]
        {
            BodyReference("older", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Clothed,
                createdUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            BodyReference("newer", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Clothed,
                createdUtc: new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc))
        };

        Assert.Equal(
            "newer",
            CharacterLoraCellService.ResolveBodyConditioning(record, PackId, assets).BodyAssetId);
    }

    [Fact]
    public void ResolveBodyConditioning_RequiresAPack()
    {
        var record = Cell(SceneImageReferenceFaceView.Front);

        Assert.Throws<InvalidOperationException>(
            () => CharacterLoraCellService.ResolveBodyConditioning(record, "  ", []));
    }

    /// <summary>
    /// The render carries BOTH conditionings: the face that makes the frame this character and the state-matched
    /// body reference that makes the build hers. Dropping either would be a silent quality regression, so the
    /// options the render is queued with are asserted, not the source text.
    /// </summary>
    [Fact]
    public async Task RenderCellAsync_CarriesTheStateMatchedBodyReferenceBesideTheFace()
    {
        var assets = new RecordingAssetService();
        var identity = new StubIdentityService();
        identity.PackAssets.Add(FaceReference("profile-ref", SceneImageReferenceFaceView.ProfileLeft));
        identity.PackAssets.Add(BodyReference("profile-clothed", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Clothed));
        identity.PackAssets.Add(BodyReference("profile-unclothed", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Unclothed));

        var service = Build(assets: assets, identity: identity, datasets: new StubLoraRepository());

        await service.RenderCellAsync(DatasetId, CellKey, "a composed prompt", "qwen-image-2.1", "1024x1024");

        Assert.NotNull(assets.LastOptions);
        Assert.Equal("profile-ref", assets.LastOptions!.Identity!.FaceAssetId);
        Assert.Equal("profile-clothed", assets.LastOptions.BodyReference!.BodyAssetId);
        Assert.Equal(PackId, assets.LastOptions.BodyReference.PackId);
    }

    /// <summary>
    /// A cell whose body state the pack cannot serve is refused BEFORE anything is queued. A freed render would put
    /// an unconditioned frame in the deck next to the conditioned ones and read as a successful attempt.
    /// </summary>
    [Fact]
    public async Task RenderCellAsync_RefusesACellWithNoMatchingBodyReferenceBeforeQueueing()
    {
        var assets = new RecordingAssetService();
        var identity = new StubIdentityService();
        identity.PackAssets.Add(FaceReference("profile-ref", SceneImageReferenceFaceView.ProfileLeft));

        var service = Build(assets: assets, identity: identity, datasets: new StubLoraRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RenderCellAsync(DatasetId, CellKey, "a composed prompt", "qwen-image-2.1", "1024x1024"));

        Assert.Null(assets.LastOptions);
    }

    // ------------------------------------------------------------------ the attempt deck

    [Fact]
    public async Task ListCellAttemptsAsync_ReturnsOnlyThisCellsAttempts_NewestFirst()
    {
        var older = Attempt("a1", "batch-of-another-cell", new DateTime(2026, 9, 24, 1, 0, 0, DateTimeKind.Utc));
        var newer = Attempt("a2", Build().CellBatchIdFor(DatasetId, CellKey), new DateTime(2026, 9, 25, 1, 0, 0, DateTimeKind.Utc));
        var middle = Attempt("a3", Build().CellBatchIdFor(DatasetId, CellKey), new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc));
        var assets = new RecordingAssetService { Images = [older, newer, middle] };

        var attempts = await Build(assets).ListCellAttemptsAsync(DatasetId, CellKey);

        Assert.Equal(["a2", "a3"], attempts.Select(attempt => attempt.Id));
    }

    [Fact]
    public async Task DiscardAttemptAsync_DeletesTheAttemptImage()
    {
        var assets = new RecordingAssetService();

        await Build(assets).DiscardAttemptAsync("image-7");

        Assert.Equal("image-7", Assert.Single(assets.DeletedImages));
    }

    [Fact]
    public async Task DiscardAttemptAsync_RequiresAnImageId()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Build().DiscardAttemptAsync("  "));
    }

    // ------------------------------------------------------------------ the model setting

    [Fact]
    public async Task CellModel_RoundTripsThroughTheSettingsStore()
    {
        var settings = new RecordingTemplateService();
        var service = Build(settings: settings);

        await service.SaveCellModelAsync("character-1", "biglust-v2");

        Assert.Equal("biglust-v2", await service.ResolveCellModelAsync("character-1"));
    }

    /// <summary>
    /// Saving the cell model must not clear the other persisted controls: the settings row is read and written back
    /// with one field changed, so choosing a LoRA model cannot silently reset the front model or a gate threshold.
    /// </summary>
    [Fact]
    public async Task SaveCellModelAsync_LeavesTheOtherSettingsAlone()
    {
        var settings = new RecordingTemplateService
        {
            Settings = new ReferenceWorkflowSettings
            {
                FrontModelId = "front-model",
                BodyModelId = "body-model",
                BodyImageSize = "1024x1536",
                AngleYawMinAbsPercent = 7.5,
                QualityGateMinSharpness = 321
            }
        };
        var service = Build(settings: settings);

        await service.SaveCellModelAsync("character-1", "lora-model");

        Assert.Equal("front-model", settings.Settings.FrontModelId);
        Assert.Equal("body-model", settings.Settings.BodyModelId);
        Assert.Equal("1024x1536", settings.Settings.BodyImageSize);
        Assert.Equal(7.5, settings.Settings.AngleYawMinAbsPercent);
        Assert.Equal(321, settings.Settings.QualityGateMinSharpness);
        Assert.Equal("lora-model", settings.Settings.LoraCellModelId);
    }

    [Fact]
    public async Task ResolveCellModelAsync_ReturnsNullWhenNoModelWasEverChosen()
    {
        Assert.Null(await Build().ResolveCellModelAsync("character-1"));
    }

    [Fact]
    public async Task ResolveCellModelAsync_WithoutACharacter_ReturnsNullRatherThanReadingTheGlobalRow()
    {
        var settings = new RecordingTemplateService();
        await settings.SaveSettingsAsync(new ReferenceWorkflowSettings { LoraCellModelId = "global-model" });

        Assert.Null(await Build(settings: settings).ResolveCellModelAsync("  "));
    }

    [Fact]
    public async Task SaveCellModelAsync_RequiresACharacterAndAModel()
    {
        var service = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveCellModelAsync("", "model"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveCellModelAsync("character-1", "   "));
    }

    // ------------------------------------------------------------------ fixtures

    private static CharacterLoraCellService Build(
        RecordingAssetService? assets = null,
        RecordingTemplateService? settings = null,
        StubIdentityService? identity = null,
        StubLoraRepository? datasets = null)
        => new(
            assets ?? new RecordingAssetService(),
            settings ?? new RecordingTemplateService(),
            datasets ?? new StubLoraRepository(),
            identity ?? new StubIdentityService());

    private static CoverageRecord Cell(SceneImageReferenceFaceView? slot) => new()
    {
        Key = CellKey,
        Role = LoraCoverageCellRole.Core,
        AngleFamily = slot is null ? LoraCoverageAngleFamily.Behind : LoraCoverageAngleFamily.Profile,
        AngleYawDeg = slot is null ? 180 : -90,
        FaceVisible = slot is not null,
        FaceCanonicalSlot = slot,
        BodyCanonicalSlot = slot is null ? SceneImageReferenceBodyView.Back : SceneImageReferenceBodyView.ProfileLeft,
        BodyState = SceneImageReferenceBodyState.Clothed,
        Distance = LoraCoverageDistance.CloseUp,
        WardrobeState = LoraCoverageWardrobeState.Clothed,
        PoseClass = LoraCoveragePoseClass.Standing,
        ExpressionKey = "lora.vocabulary.expression.neutral",
        LightingKey = "lora.vocabulary.lighting.indoor-dim",
        BackgroundKey = "lora.vocabulary.background.plain-wall",
        OutfitKey = "lora.vocabulary.outfit.casual",
        Aspect = "1024x1024",
        Seed = 41000,
        Split = CharacterLoraDatasetSplit.Train
    };

    private static SceneImageReferenceAsset FaceReference(
        string id,
        SceneImageReferenceFaceView view,
        bool approved = true,
        DateTime? createdUtc = null) => new()
    {
        Id = id,
        IdentityPackId = PackId,
        AssetKind = SceneImageReferenceAssetKind.Face,
        FaceView = view,
        IsApproved = approved,
        CreatedUtc = createdUtc ?? new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
    };

    private static SceneImageReferenceAsset BodyReference(
        string id,
        SceneImageReferenceBodyView view,
        SceneImageReferenceBodyState state,
        bool approved = true,
        DateTime? createdUtc = null) => new()
    {
        Id = id,
        IdentityPackId = PackId,
        AssetKind = SceneImageReferenceAssetKind.FullBody,
        BodyView = view,
        BodyState = state,
        IsApproved = approved,
        FileRelativePath = $"pack/{id}.png",
        Sha256 = new string('A', 64),
        CreatedUtc = createdUtc ?? new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
    };

    private static SceneAssetImage Attempt(string id, string batchId, DateTime createdUtc) => new()
    {
        Id = id,
        AssetId = $"asset-{id}",
        CandidateBatchId = batchId,
        CreatedUtc = createdUtc,
        Status = SceneAssetStatus.Complete,
        FileRelativePath = $"lora/{id}.png"
    };

    /// <summary>Records the two calls the attempt deck makes, and serves back whatever images a test seeds.</summary>
    private sealed class RecordingAssetService : ISceneAssetService
    {
        public List<string> DeletedImages { get; } = [];

        public List<SceneAssetImage> Images { get; set; } = [];

        /// <summary>The conditioning the render was queued with, so "both references travel" is asserted on data.</summary>
        public SceneAssetImageGenerationOptions? LastOptions { get; private set; }

        public string? LastBatchId { get; private set; }

        public string? LastPrompt { get; private set; }

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(
            string candidateBatchId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                Images.Where(image => string.Equals(image.CandidateBatchId, candidateBatchId, StringComparison.Ordinal)).ToList());

        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
        {
            DeletedImages.Add(imageId);
            return Task.CompletedTask;
        }

        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new SceneAsset { Id = "container-new", Name = name, Type = type, Status = SceneAssetStatus.Pending });

        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null)
        {
            LastOptions = options;
            LastBatchId = candidateBatchId;
            LastPrompt = prompt;
            return Task.FromResult(new SceneAssetImage
            {
                Id = "attempt-1",
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
        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImageValidationResultAsync(string imageId, string? validationResultJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> ApproveImageForProductionAsync(string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> CreateFromPromptAsync(string name, string prompt, SceneAssetType type, string modelId, string imageSize, string? candidateBatchId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> CreateFromUploadAsync(string name, SceneAssetType type, string fileName, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> EnqueueEditAsync(string sourceAssetId, string name, string editPrompt, string modelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnqueueProfilePackAsync(SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneAsset?>(new SceneAsset { Id = assetId, Name = "LoRA dataset container", Type = SceneAssetType.Character });
        public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(string identityPackId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Not reached by these tests. The identity-conditioned render path itself is proven by its own suite
    /// (`SceneAssetGenerationJobHandlerIdentityReferenceTests`), and the workspace contract test asserts this service
    /// reaches it with the conditioning set.
    /// </summary>
    /// <summary>
    /// Serves exactly one draft dataset whose stored plan holds this suite's cell. Everything else throws, so a test
    /// that accidentally depends on another repository call fails loudly rather than passing on a stub.
    /// </summary>
    private sealed class StubLoraRepository : DreamGenClone.Application.RolePlay.ICharacterLoraRepository
    {
        private readonly CharacterLoraDataset _dataset = new()
        {
            Id = DatasetId,
            CharacterTemplateId = "becky",
            IdentityPackId = PackId,
            Version = 1,
            Status = CharacterLoraDatasetStatus.Draft,
            TriggerToken = "becky_token",
            TargetModelFamily = "SDXL",
            ContainerAssetId = "container-1",
            CoveragePlanJson = PlanJson()
        };

        private static string PlanJson()
        {
            var plan = new CoveragePlan
            {
                CharacterProfileId = "becky",
                IdentityPackId = PackId,
                IdentityPackVersion = 1,
                TriggerToken = "becky_token",
                TargetModelFamily = "SDXL",
                SeedRangeStart = 41000,
                GeneratedUtc = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc),
                Records = [Cell(SceneImageReferenceFaceView.ProfileLeft)],
                Vocabulary = new Dictionary<string, string>(StringComparer.Ordinal) { ["any"] = "a phrase" }
            };
            return plan.ToJson();
        }

        public Task<CharacterLoraDataset?> GetDatasetAsync(string datasetId, CancellationToken cancellationToken = default)
            => Task.FromResult<CharacterLoraDataset?>(
                string.Equals(datasetId, _dataset.Id, StringComparison.Ordinal) ? _dataset : null);

        public Task<CharacterLoraDataset> SetDatasetContainerAsync(string datasetId, string containerAssetId, CancellationToken cancellationToken = default)
        {
            _dataset.ContainerAssetId = containerAssetId;
            return Task.FromResult(_dataset);
        }

        public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingProfile> CreateTrainingProfileAsync(CharacterLoraTrainingProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingProfile?> GetTrainingProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraTrainingProfile>> ListTrainingProfilesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingProfile> QualifyTrainingProfileAsync(string profileId, string qualificationEvidenceJson, DateTime qualifiedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurationPolicy> ResolveCurationPolicyAsync(string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurationPolicy> SaveCurationPolicyAsync(CurationPolicy policy, string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CurationPolicy> ResetCurationPolicyToSeedAsync(string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDataset> CreateDatasetAsync(CharacterLoraDataset dataset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraDataset>> ListDatasetsAsync(string characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddDatasetMemberAsync(CharacterLoraDatasetMember member, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraDatasetMember>> ListDatasetMembersAsync(string datasetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDatasetMember> CurateDatasetMemberAsync(CharacterLoraDatasetMember member, int expectedCaptionRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraDataset> FreezeDatasetAsync(string datasetId, string frozenBy, DateTime frozenUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingJob> CreateTrainingJobAsync(CharacterLoraTrainingJob job, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingJob?> GetTrainingJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraTrainingJob>> ListTrainingJobsAsync(string datasetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingJob> TransitionTrainingJobAsync(string jobId, CharacterLoraTrainingJobStatus expectedStatus, CharacterLoraTrainingJobStatus nextStatus, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> CreateTrainingAttemptAsync(CharacterLoraTrainingAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt?> GetTrainingAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> RecordTrainingSubmissionAsync(string attemptId, string providerKey, string providerRequestId, string providerStatusUrl, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> TransitionTrainingAttemptAsync(string attemptId, CharacterLoraTrainingAttemptStatus expectedStatus, CharacterLoraTrainingAttemptStatus nextStatus, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> RecordTrainingResultAsync(string attemptId, string outputFileRelativePath, string outputSha256, long outputByteLength, string statusHistoryJson, string logManifestJson, string sampleManifestJson, string checkpointManifestJson, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraTrainingAttempt> RecordTrainingFailureAsync(string attemptId, CharacterLoraTrainingAttemptStatus expectedStatus, CharacterLoraTrainingAttemptStatus failureStatus, string failureCode, string failureDiagnostic, string statusHistoryJson, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraTrainingAttempt>> ListTrainingAttemptsAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraArtifact> CreateArtifactAsync(CharacterLoraArtifact artifact, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraArtifact?> GetArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CharacterLoraArtifact>> ListArtifactsAsync(string characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterLoraArtifact> SetArtifactStatusAsync(string artifactId, CharacterLoraArtifactStatus status, string decisionEvidenceJson, DateTime decidedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateIdentityStrategyBindingAsync(IdentityStrategyBinding binding, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IdentityStrategyBinding>> ListIdentityStrategyBindingsAsync(string compiledRequestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>The pack's reference assets, chosen per test, so a decision can be exercised without a database.</summary>
    private sealed class StubIdentityService : ICharacterImageIdentityService
    {
        public List<SceneImageReferenceAsset> PackAssets { get; } = [];

        public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageReferenceAsset>>(PackAssets);

        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(string characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> CreateDraftPackAsync(string characterProfileId, CharacterImageIdentityPackScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> SetDraftPackScopeAsync(string packId, CharacterImageIdentityPackScope scope, string? reason = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> ApprovePackAsync(string packId, string descriptorSnapshotJson, string? approvedBy = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> SupersedePackAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageReferenceAsset> UploadAssetAsync(string packId, SceneImageReferenceAssetKind kind, string fileName, Stream content, SceneImageReferenceFaceView? faceView = null, SceneImageReferenceBodyView? bodyView = null, SceneImageReferenceBodyState? bodyState = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageReferenceSlotWrite> ReplaceSlotAssetAsync(string packId, SceneImageReferenceAssetKind kind, string fileName, Stream content, SceneImageReferenceFaceView? faceView = null, SceneImageReferenceBodyView? bodyView = null, SceneImageReferenceBodyState? bodyState = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAssetProvenanceAsync(string packId, string assetId, SceneImageReferenceConsentState consentState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAssetQualityAsync(string assetId, SceneImageReferenceQuality quality, string notes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageReferenceAsset> AnalyzeAssetQualityAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>An in-memory settings row, so the round-trip and the "other fields survive" rule are both provable.</summary>
    private sealed class RecordingTemplateService : IImageWorkflowTemplateService
    {
        public ReferenceWorkflowSettings Settings { get; set; } = new();

        public Task<ReferenceWorkflowSettings> ResolveSettingsAsync(string? characterProfileId, CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        public Task SaveSettingsAsync(ReferenceWorkflowSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }

        public Task<ImageWorkflowPromptTemplate> ResolveAsync(string key, string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImageWorkflowPromptTemplate> ResetToSeedAsync(string key, ImageWorkflowPromptTemplateScope scope, string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ImageWorkflowPromptTemplate>> ListTemplatesAsync(string? characterProfileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveTemplateAsync(ImageWorkflowPromptTemplate template, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
