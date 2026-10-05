using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The writer seam + the ONE image-editing job (B-124 B124-012). The asset kind owns provenance
/// validation and the write block; the job owns model resolution, the editor call and failure marking.
/// </summary>
public sealed class MediaEditImageEditingJobHandlerTests
{
    [Fact]
    public async Task AssetRun_WritesTheResultAndCompletesTheEditSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.EnqueueAssetRunAsync();
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildHandler(editor);

        await handler.HandleAsync(job);

        var edited = await fixture.Assets.GetImageAsync(fixture.EditedImageId);
        Assert.Equal(SceneAssetStatus.Complete, edited!.Status);
        Assert.NotNull(edited.FileRelativePath);
        Assert.Equal($"{fixture.EditedImageId}.png", Path.GetFileName(edited.FileRelativePath));
        Assert.Equal(1, editor.Calls);
        Assert.Equal(SceneAssetImageEditSessionStatus.Completed, (await fixture.Edits.GetSessionAsync(fixture.EditSessionId))!.Status);
        Assert.Contains("Qwen-Rapid-AIO-NSFW-v23.safetensors", edited.ModelSnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssetRun_IsIdempotentWhenTheImageAlreadyCompleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.EnqueueAssetRunAsync();
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildHandler(editor);

        await handler.HandleAsync(job);
        await handler.HandleAsync(job);

        // A redelivered job must not pay for the same edit twice.
        Assert.Equal(1, editor.Calls);
        var edited = await fixture.Assets.GetImageAsync(fixture.EditedImageId);
        Assert.Equal(SceneAssetStatus.Complete, edited!.Status);
    }

    [Fact]
    public async Task AssetRun_RefusesProvenanceThatDoesNotMatchTheAcceptedRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var staleImageId = await fixture.CreateStaleEditedImageAsync();
        var job = await fixture.EnqueueAssetRunAsync(staleImageId);
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildHandler(editor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job));

        Assert.Equal(0, editor.Calls);
        var stale = await fixture.Assets.GetImageAsync(staleImageId);
        Assert.Equal(SceneAssetStatus.Failed, stale!.Status);
        Assert.Contains("was not found", stale.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssetRun_FailsTheImageWhenTheEditorModelRejectsTheRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.EnqueueAssetRunAsync();
        var editor = new RecordingImageEditor { ThrowOnEdit = new InvalidOperationException("model refused") };
        var handler = fixture.BuildHandler(editor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job));

        var edited = await fixture.Assets.GetImageAsync(fixture.EditedImageId);
        Assert.Equal(SceneAssetStatus.Failed, edited!.Status);
        Assert.Equal("model refused", edited.ErrorMessage);
        Assert.Equal(SceneAssetImageEditSessionStatus.Failed, (await fixture.Edits.GetSessionAsync(fixture.EditSessionId))!.Status);
    }

    /// <summary>
    /// THE REPORTED FAILURE (2026-10-03). An approved Location bound to an ASSET edit reached the resolver, which
    /// demanded <c>ReferenceConditioning</c> — the IP-Adapter/PuLID identity MECHANISM, which no editor graph
    /// implements — while the editor model declares only <c>NativeMultiReference</c>. Every reference-carrying asset
    /// edit failed with "Reference strategy 'NativeMultiReference' for 'Location' is qualified but has no implemented
    /// graph in this editor", so a face, body or wardrobe reference would have failed identically.
    ///
    /// <para>
    /// The gate now reads the strategies the SURFACE implements from `ReferenceStrategyCatalogue`, so the model's own
    /// qualification is what decides.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AssetEdit_WithABoundApprovedLocation_CarriesTheReference()
    {
        await using var fixture = await Fixture.CreateAsync();
        var location = await fixture.GivenAnApprovedLocationAsync();
        var resolver = new MediaEditReferenceResolver(
            fixture.Assets, fixture.Storage, new QualifiedEditStrategies());
        var strategies = await resolver.ResolveAsync(
            Fixture.EditorModelId, [location], ReferenceStrategyCatalogue.ReferenceImageSurface.Edit);

        var reference = Assert.Single(strategies);
        Assert.Equal(1, reference.Ordinal);
        Assert.Equal(ImageStepSlotKind.Location, reference.SlotKind);
        Assert.Contains("location continuity", reference.Description, StringComparison.Ordinal);
        Assert.Contains("Indoor Front", reference.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// A strategy the EDIT surface does not implement is still refused, by name, and the message says what it does
    /// implement. Weakening the gate must not turn "this graph cannot carry it" into a wrong render.
    /// </summary>
    [Fact]
    public async Task AssetEdit_WithAStrategyTheSurfaceDoesNotImplement_RefusesAndNamesWhatItDoesImplement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var location = await fixture.GivenAnApprovedLocationAsync();
        var resolver = new MediaEditReferenceResolver(
            fixture.Assets, fixture.Storage, new QualifiedEditStrategies { ResolveEveryBindingAs = "ControlNet" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
            Fixture.EditorModelId, [location], ReferenceStrategyCatalogue.ReferenceImageSurface.Edit));

        Assert.Contains("ControlNet", error.Message, StringComparison.Ordinal);
        Assert.Contains("editor", error.Message, StringComparison.Ordinal);
        Assert.Contains("NativeMultiReference", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A character's approved identity-PACK build travels as a reference on an edit. It used to be filtered out with
    /// no error at all: `UsesReference` is false for a pack binding (a pack image is a `SceneImageReferenceAsset`
    /// addressed by its own pack id, so it carries no scene asset id), so the asset branch could never serve it and
    /// every edit lost the character's build in silence.
    /// </summary>
    [Fact]
    public async Task AssetEdit_WithABoundPackBuild_CarriesIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var identityStorage = new RecordingIdentityStorage();
        var resolver = new MediaEditReferenceResolver(
            fixture.Assets, fixture.Storage, new QualifiedEditStrategies(),
            identity: new StubIdentityRepository(), identityStorage: identityStorage);

        var resolved = await resolver.ResolveAsync(
            Fixture.EditorModelId, [Fixture.PackBuildBinding()], ReferenceStrategyCatalogue.ReferenceImageSurface.Edit);

        var reference = Assert.Single(resolved);
        Assert.Equal(ImageStepSlotKind.Body, reference.SlotKind);
        Assert.Equal("body-1.png", reference.FileName);
        Assert.Equal(Fixture.IdentityFaceSha256, reference.Sha256);
        await using var bytes = await reference.OpenAsync(CancellationToken.None);
        Assert.True(bytes.Length > 0);
    }

    /// <summary>
    /// A pack carries faces and builds only, so a pack binding on any other element is refused BY NAME rather than
    /// resolved as a guess.
    /// </summary>
    [Fact]
    public async Task AssetEdit_WithAPackBindingOnAWardrobe_RefusesByName()
    {
        await using var fixture = await Fixture.CreateAsync();
        var resolver = new MediaEditReferenceResolver(
            fixture.Assets, fixture.Storage, new QualifiedEditStrategies(),
            identity: new StubIdentityRepository(), identityStorage: new RecordingIdentityStorage());

        var wardrobe = Fixture.PackBuildBinding();
        wardrobe.Kind = nameof(ImageStepSlotKind.Wardrobe);
        wardrobe.ElementKey = "Wardrobe";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
            Fixture.EditorModelId, [wardrobe], ReferenceStrategyCatalogue.ReferenceImageSurface.Edit));

        Assert.Contains("faces and builds only", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scratch-image binding has no implementable route on any surface today. It is refused with the channel named
    /// rather than filtered out, because a reference the operator bound and the model never received is
    /// indistinguishable from one that was applied and ignored.
    /// </summary>
    [Fact]
    public async Task AssetEdit_WithAScratchImageBinding_RefusesByName()
    {
        await using var fixture = await Fixture.CreateAsync();
        var resolver = new MediaEditReferenceResolver(
            fixture.Assets, fixture.Storage, new QualifiedEditStrategies());

        var scratch = Fixture.PackBuildBinding();
        scratch.Source = nameof(ImageStepReferenceSourceKind.ScratchImage);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
            Fixture.EditorModelId, [scratch], ReferenceStrategyCatalogue.ReferenceImageSurface.Edit));

        Assert.Contains("scratch image", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The Asset Manager editor's identity run — the tab the asset store did not have. The row's bound approved
    /// faces are sent as references and the model carried on the job is used as-is: re-resolving a default here
    /// would let the run use a model the user never chose, which is exactly what carrying the chosen model stops.
    /// </summary>
    [Fact]
    public async Task AssetIdentityRun_SendsTheBoundFacesAndTheChosenModel()
    {
        await using var fixture = await Fixture.CreateAsync();
        var identityImageId = await fixture.CreateAssetIdentityImageAsync();
        var job = await fixture.EnqueueAssetRunAsync(identityImageId);
        var identityStorage = new RecordingIdentityStorage();
        var editor = new ReferenceRecordingImageEditor();
        var resolver = new ChosenModelOnlyResolver();

        await fixture.BuildIdentityHandler(editor, identityStorage, resolver).HandleAsync(job);

        // The chosen model, resolved once by the id the form carried — never the configured default.
        Assert.Equal(1, resolver.ByIdCalls);
        Assert.Equal(0, resolver.DefaultCalls);
        var sent = Assert.Single(editor.ReferenceCalls);
        Assert.Equal(Fixture.EditorModelId, sent.Model.RegisteredModelId);

        // The approved packed face, at its recorded ordinal, read from identity storage.
        var reference = Assert.Single(sent.References);
        Assert.Equal(1, reference.Ordinal);
        Assert.Equal("becky-1.png", reference.FileName);
        Assert.Equal(Fixture.IdentityFaceSha256, reference.Checksum);
        Assert.Contains("selected face identity reference for Becky", reference.SemanticRole, StringComparison.Ordinal);
        Assert.Contains(Fixture.IdentityFaceRelativePath, identityStorage.Opened);

        // And the service-authored identity instruction is the prompt that ran.
        Assert.Contains("Apply the face of the person shown in Picture 2", sent.Instruction, StringComparison.Ordinal);
        Assert.Contains("exactly unchanged", sent.Instruction, StringComparison.Ordinal);

        var edited = await fixture.Assets.GetImageAsync(identityImageId);
        Assert.Equal(SceneAssetStatus.Complete, edited!.Status);
        Assert.Contains(Fixture.EditorModelId, edited.ModelSnapshotJson!, StringComparison.Ordinal);
    }

    /// <summary>A row that reaches the run without the form's model is a refusal, not a silent default.</summary>
    [Fact]
    public async Task AssetIdentityRun_RefusesARowWithoutTheChosenModel()
    {
        await using var fixture = await Fixture.CreateAsync();
        var identityImageId = await fixture.CreateAssetIdentityImageAsync();
        var job = fixture.JobWithoutEditorModel(identityImageId);
        var editor = new ReferenceRecordingImageEditor();

        var handler = fixture.BuildIdentityHandler(editor, new RecordingIdentityStorage(), new ChosenModelOnlyResolver());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job));

        Assert.Empty(editor.ReferenceCalls);
        var edited = await fixture.Assets.GetImageAsync(identityImageId);
        Assert.Equal(SceneAssetStatus.Failed, edited!.Status);
        Assert.Contains("editor model", edited.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Payload_CarriesAnExplicitSubjectKind()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.EnqueueAssetRunAsync();

        var payload = JsonSerializer.Deserialize<MediaEditImageEditingJobPayload>(
            job.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(MediaEditSubjectKind.AssetImage, payload.SubjectKind);
        Assert.Equal(fixture.EditedImageId, payload.ImageId);
        Assert.Equal(Fixture.EditorModelId, payload.EditorModelId);

        var unknown = new MediaEditSubjectWriterResolver([]);
        var error = Assert.Throws<InvalidOperationException>(() => unknown.Resolve(MediaEditSubjectKind.SceneImage));
        Assert.Contains("SceneImage", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CropRun_WritesANewImageAndLeavesTheSourceUntouched()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (cropImageId, job) = await fixture.EnqueueAssetCropAsync();
        var handler = fixture.BuildCropHandler(new RecordingImageEditor());

        await handler.HandleAsync(job);

        var cropped = await fixture.Assets.GetImageAsync(cropImageId);
        Assert.Equal(SceneAssetStatus.Complete, cropped!.Status);
        Assert.NotNull(cropped.FileRelativePath);
        Assert.Equal($"{cropImageId}.png", Path.GetFileName(cropped.FileRelativePath));

        // The input is untouched: a crop is a new derived image, never an in-place rewrite.
        var source = await fixture.Assets.GetImageAsync(fixture.CropSourceImageId);
        Assert.Equal(SceneAssetStatus.Complete, source!.Status);
        Assert.Equal(fixture.CropSourceSha256, source.Sha256);

        // A crop is not a render, so the row must not claim a model produced it.
        Assert.Null(cropped.ModelSnapshotJson);
    }

    [Fact]
    public async Task CropRun_NeverResolvesAModelOrCallsTheEditorClient()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (_, job) = await fixture.EnqueueAssetCropAsync();
        var editor = new RecordingImageEditor { ThrowOnEdit = new InvalidOperationException("the editor must not be called") };

        // The resolver throws on any use, so the run completing at all proves nothing resolved a model.
        var handler = fixture.BuildCropHandler(editor);
        await handler.HandleAsync(job);

        Assert.Equal(0, editor.Calls);
    }

    [Fact]
    public async Task CropRun_HeadAwareWithoutAMeasurement_MarksTheImageFailed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (cropImageId, job) = await fixture.EnqueueAssetCropAsync(mode: ImageCropMode.HeadAware, measurement: null);
        var handler = fixture.BuildCropHandler(new RecordingImageEditor());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job));

        Assert.Contains("head-aware", error.Message, StringComparison.OrdinalIgnoreCase);
        var cropped = await fixture.Assets.GetImageAsync(cropImageId);
        Assert.Equal(SceneAssetStatus.Failed, cropped!.Status);
        Assert.Contains("head-aware", cropped.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Payload_WithoutAnOperationKind_FailsFastWithoutTouchingTheImage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (cropImageId, _) = await fixture.EnqueueAssetCropAsync();
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildCropHandler(editor);

        var job = new DurableBackgroundJob
        {
            JobType = BackgroundJobTypes.MediaEditImageEditing,
            PayloadJson = JsonSerializer.Serialize(new MediaEditImageEditingJobPayload
            {
                SubjectKind = MediaEditSubjectKind.AssetImage,
                ImageId = cropImageId
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job));

        Assert.Contains("operation kind", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, editor.Calls);
        var cropped = await fixture.Assets.GetImageAsync(cropImageId);
        Assert.Equal(SceneAssetStatus.Pending, cropped!.Status);
    }

    [Fact]
    public async Task CropRun_IsIdempotentWhenTheImageAlreadyCompleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (cropImageId, job) = await fixture.EnqueueAssetCropAsync();
        var handler = fixture.BuildCropHandler(new RecordingImageEditor());

        await handler.HandleAsync(job);
        var first = await fixture.Assets.GetImageAsync(cropImageId);

        await handler.HandleAsync(job);
        var second = await fixture.Assets.GetImageAsync(cropImageId);

        Assert.Equal(SceneAssetStatus.Complete, second!.Status);
        Assert.Equal(first!.Sha256, second.Sha256);
    }

    // ---- B-133 preset edit pass -------------------------------------------------------------------------

    [Fact]
    public async Task PresetRun_SendsTheAssembledInstructionAndCompletes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var imageId = await fixture.CreateAssetPresetImageAsync(
            ImagePresetKeys.LightingIndoorDim, Fixture.PresetInstruction);
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildPresetHandler(editor, new StubPresetService(Fixture.PresetInstruction));

        await handler.HandleAsync(await fixture.EnqueueAssetRunAsync(imageId));

        // The instruction that reached the model is the one the preset assembled - byte for byte, because a preset is
        // never handed to the compiler to be rewritten.
        Assert.Equal(Fixture.PresetInstruction, Assert.Single(editor.Instructions));
        var image = await fixture.Assets.GetImageAsync(imageId);
        Assert.Equal(SceneAssetStatus.Complete, image!.Status);
    }

    /// <summary>
    /// The one thing a queued instruction must never do is run after the wording it was assembled from changed.
    /// </summary>
    [Fact]
    public async Task PresetRun_WhenThePresetWordingChanged_RefusesInsteadOfRenderingTheStaleText()
    {
        await using var fixture = await Fixture.CreateAsync();
        var imageId = await fixture.CreateAssetPresetImageAsync(
            ImagePresetKeys.LightingIndoorDim,
            Fixture.PresetInstruction,
            MediaEditPresetProvenance.InstructionSha256("a different lighting wording entirely"));
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildPresetHandler(editor, new StubPresetService(Fixture.PresetInstruction));
        var job = await fixture.EnqueueAssetRunAsync(imageId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job));

        Assert.Contains(ImagePresetKeys.LightingIndoorDim, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, editor.Calls);
    }

    [Fact]
    public async Task PresetRun_WhenTheRowIsNotWhatThePresetAssembles_IsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();
        var instruction = Fixture.PresetInstruction;
        // The recorded checksum matches the preset, but the row's own prompt does not: the row was written by
        // something that does not agree with the preset, which is exactly the mismatch to refuse.
        var imageId = await fixture.CreateAssetPresetImageAsync(
            ImagePresetKeys.LightingIndoorDim, instruction + " And something else.",
            MediaEditPresetProvenance.InstructionSha256(instruction));
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildPresetHandler(editor, new StubPresetService(instruction));
        var job = await fixture.EnqueueAssetRunAsync(imageId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(job));

        Assert.Contains("prompt", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, editor.Calls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string EditorModelId = "22222222-2222-2222-2222-222222222222";

        private readonly string _dbPath;
        private readonly string _root;

        private Fixture(string dbPath, string root, SceneAssetRepository assets, SceneAssetImageEditRepository edits)
        {
            _dbPath = dbPath;
            _root = root;
            Assets = assets;
            Edits = edits;
        }

        public SceneAssetRepository Assets { get; }
        public SceneAssetImageEditRepository Edits { get; }

        /// <summary>The real asset storage the writers and the resolver read approved images through.</summary>
        public ISceneAssetStorageService Storage { get; private set; } = null!;

        /// <summary>
        /// An APPROVED location image with a user-entered name, exactly as B-145 makes one offerable as a reference:
        /// Complete + Approved + a production version + a checksum + a stored file.
        /// </summary>
        public async Task<ReferenceApplicationSelection> GivenAnApprovedLocationAsync()
        {
            // The container the image belongs to (a location asset created the way the app makes one).
            await Assets.UpsertAsync(new SceneAsset
            {
                Id = LocationAssetId,
                Name = "Maintenance Shed",
                Type = SceneAssetType.Location,
                Status = SceneAssetStatus.Complete
            });

            var image = new SceneAssetImage
            {
                Id = "location-image-1",
                AssetId = LocationAssetId,
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                DisplayName = "Indoor Front",
                Prompt = "A weathered maintenance shed, indoors.",
                FileRelativePath = "assets/location-image-1.png",
                ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved,
                ProductionVersion = 1,
                Sha256 = LocationSha256,
                ByteLength = 4
            };
            await Assets.UpsertImageAsync(image);

            return new ReferenceApplicationSelection
            {
                ElementKey = "Location",
                Kind = nameof(ImageStepSlotKind.Location),
                SemanticRole = "location continuity",
                Source = nameof(ImageStepReferenceSourceKind.ApprovedSceneAsset),
                Strategy = ReferenceStrategyCatalogue.NativeMultiReference,
                SceneAssetId = LocationAssetId,
                SceneAssetImageId = image.Id,
                SceneAssetVersion = 1,
                SceneAssetSha256 = LocationSha256,
                Ordinal = 1,
                ReferenceLabel = "Indoor Front"
            };
        }

        /// <summary>A bound approved identity-PACK build, the channel an edit could not carry at all.</summary>
        public static ReferenceApplicationSelection PackBuildBinding() => new()
        {
            ElementKey = "Body",
            Kind = nameof(ImageStepSlotKind.Body),
            SemanticRole = "character body",
            Source = nameof(ImageStepReferenceSourceKind.IdentityPackAsset),
            Strategy = ReferenceStrategyCatalogue.NativeMultiReference,
            Ordinal = 1,
            IdentityPackId = "pack-1",
            ReferenceAssetId = "body-1",
            ReferenceLabel = "Front · Unclothed"
        };

        public const string LocationAssetId = "location-asset-1";
        public const string LocationSha256 = "1B2C3D4E";
        public string EditedImageId { get; private set; } = string.Empty;
        public string CroppedImageId { get; private set; } = string.Empty;
        public string CropSourceImageId { get; private set; } = string.Empty;
        public string CropSourceSha256 { get; private set; } = string.Empty;
        public string EditSessionId { get; private set; } = string.Empty;
        public string _editorStorageRoot => Path.Combine(_root, "assets");

        public static async Task<Fixture> CreateAsync(int sourceWidth = 8, int sourceHeight = 6, Rgb24? sourceColour = null)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"media-edit-image-{Guid.NewGuid():N}.db");
            var root = Path.Combine(Path.GetTempPath(), $"media-edit-image-files-{Guid.NewGuid():N}");
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(root, "scene-images")
            });
            await new SqlitePersistence(
                options,
                Options.Create(new LmStudioOptions()),
                Options.Create(new StoryAnalysisOptions()),
                Options.Create(new ScenarioAdaptationOptions()),
                NullLogger<SqlitePersistence>.Instance).InitializeAsync();

            var assets = new SceneAssetRepository(options);
            var edits = new SceneAssetImageEditRepository(options);
            var storage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);
            var fixture = new Fixture(dbPath, root, assets, edits) { Storage = storage };

            await assets.UpsertAsync(new SceneAsset
            {
                Id = "asset-1", Name = "Asset one", Type = SceneAssetType.CharacterFace, Status = SceneAssetStatus.Complete
            });

            // A real, decodable PNG: the editor client never decodes the source, but the region-mask engine
            // (exercised by the region-edit test) does measure it.
            var png = MakeDecodablePng(sourceWidth, sourceHeight, sourceColour);
            await using (var sourceContent = new MemoryStream(png))
            {
                var stored = await storage.SaveAsync("asset-source.png", sourceContent);
                var source = new SceneAssetImage
                {
                    AssetId = "asset-1",
                    Kind = SceneAssetKind.Uploaded,
                    Status = SceneAssetStatus.Complete,
                    FileRelativePath = stored.RelativePath,
                    Sha256 = stored.Sha256
                };
                await assets.UpsertImageAsync(source);
                fixture.SourceImageId = source.Id;
                fixture.SourceSha256 = stored.Sha256;
            }

            var session = new SceneAssetImageEditSession
            {
                AssetId = "asset-1",
                SourceImageId = fixture.SourceImageId,
                SourceImageSha256 = fixture.SourceSha256,
                Status = SceneAssetImageEditSessionStatus.Ready
            };
            await edits.CreateSessionAsync(session);
            fixture.EditSessionId = session.Id;

            var attempt = new SceneAssetImageEditCompilationAttempt
            {
                EditSessionId = session.Id,
                Ordinal = 0,
                RawIntent = "make it red",
                SourceImageSha256 = fixture.SourceSha256,
                Status = SceneImageEditCompilationAttemptStatus.Pending,
                ResolvedModelSnapshotJson = "{}",
                CompilerSchemaVersion = "schema-1",
                SystemPromptVersion = "prompt-1"
            };
            await edits.CreateAttemptAsync(attempt);
            attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
            attempt.StartedUtc = DateTime.UtcNow;
            await edits.UpdateAttemptAsync(attempt);
            attempt.Status = SceneImageEditCompilationAttemptStatus.Ready;
            attempt.CompletedUtc = DateTime.UtcNow;
            await edits.UpdateAttemptAsync(attempt);
            var revision = new SceneAssetImageEditPromptRevision
            {
                CompilationAttemptId = attempt.Id,
                Ordinal = 0,
                Prompt = "Change the shirt to red.",
                RevisionKind = SceneImageEditPromptRevisionKind.CompilerOutput,
                PromptSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("Change the shirt to red.")))
            };
            await edits.CreateRevisionAsync(revision);

            var edited = new SceneAssetImage
            {
                AssetId = "asset-1",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Pending,
                Prompt = revision.Prompt,
                SourceImageId = fixture.SourceImageId,
                SourceProvenanceJson = JsonSerializer.Serialize(new
                {
                    editSessionId = session.Id,
                    compilationAttemptId = attempt.Id,
                    promptRevisionId = revision.Id,
                    sourceImageSha256 = fixture.SourceSha256,
                    promptSha256 = revision.PromptSha256
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            await assets.UpsertImageAsync(edited);
            fixture.EditedImageId = edited.Id;

            return fixture;
        }

        public string SourceImageId { get; private set; } = string.Empty;
        public string SourceSha256 { get; private set; } = string.Empty;

        public const string IdentityFaceRelativePath = "identity/becky/front.png";
        public const string IdentityFaceSha256 = "A1B2C3D4";

        /// <summary>The exact text a preset assembles for the tests, preserve clause included.</summary>
        public const string PresetInstruction =
            "Relight this photograph to the following lighting: dim, low-key indoor light from one warm lamp just "
            + "outside the frame to camera left, the near side of the face and body lit with visible detail while the "
            + "far side and the background fall into deep shadow. Keep the person identical - the same face, body, "
            + "skin, hair and marks - and keep the pose, the camera angle, the framing, the crop, the clothing and the "
            + "setting itself unchanged; only the lighting changes.";
        public const string IdentityInstruction =
            "Apply the face of the person shown in Picture 2 to the man on the left at left third of the frame. "
            + "Keep that person's facial identity consistent with Picture 2 for the entire image; do not change "
            + "anyone else. Keep the pose, bodies, position, clothing, lighting, and everything else in the image "
            + "exactly unchanged except the selected face.";

        public MediaEditImageEditingJobHandler BuildHandler(IImageEditingClient editor)
        {
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(_root, "scene-images")
            });
            var storage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);
            var references = new MediaEditReferenceResolver(Assets, storage, new StubReferenceStrategies());
            var writer = new SceneAssetMediaEditSubjectWriter(Assets, Edits, storage, references);
            return new MediaEditImageEditingJobHandler(
                new MediaEditSubjectWriterResolver([writer]),
                OperationResolver(),
                new StubImageEditorResolver(),
                editor,
                // The real, stateless region-mask engine: the production pixel code, not a stub of it.
                new ImageRegionMaskEngine(),
                NullLogger<MediaEditImageEditingJobHandler>.Instance);
        }

        /// <summary>
        /// The real writer armed with the 2.1-native resolver a region edit requires: the shared stub resolves a
        /// merged checkpoint, which the region path rejects before any render is paid for.
        /// </summary>
        public MediaEditImageEditingJobHandler BuildRegionHandler(IImageEditingClient editor)
        {
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(_root, "scene-images")
            });
            var storage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);
            var references = new MediaEditReferenceResolver(Assets, storage, new StubReferenceStrategies());
            var writer = new SceneAssetMediaEditSubjectWriter(Assets, Edits, storage, references);
            return new MediaEditImageEditingJobHandler(
                new MediaEditSubjectWriterResolver([writer]),
                OperationResolver(),
                new Qwen21ImageEditorResolver(),
                editor,
                new ImageRegionMaskEngine(),
                NullLogger<MediaEditImageEditingJobHandler>.Instance);
        }

        /// <summary>
        /// The bytes of a frame the run produced, straight out of the editor storage the writer saves into - so a test
        /// can compare what the host rendered with what the run decided to keep.
        /// </summary>
        public async Task<byte[]> ReadStoredImageAsync(SceneAssetImage image)
        {
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(_root, "scene-images")
            });
            var storage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);
            await using var stream = await storage.OpenReadAsync(image.FileRelativePath!, CancellationToken.None);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }

        /// <summary>
        /// The queued identity row exactly as the asset identity enqueue writes it: the service-authored
        /// face-only instruction, plus the bound approved faces in its provenance and no compiler artifact.
        /// </summary>
        public async Task<string> CreateAssetIdentityImageAsync()
        {
            var bindings = new[]
            {
                new MediaEditIdentityBinding(
                    1, "becky-1", "Becky", "pack-1", 3, "face-1",
                    IdentityFaceRelativePath, IdentityFaceSha256, "man on the left", "left third of the frame")
            };
            var image = new SceneAssetImage
            {
                AssetId = "asset-1",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Pending,
                Prompt = IdentityInstruction,
                SourceImageId = SourceImageId,
                SourceProvenanceJson = JsonSerializer.Serialize(new
                {
                    operation = MediaEditProvenance.EditValue,
                    sourceImageSha256 = SourceSha256,
                    identityReferences = bindings
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            await Assets.UpsertImageAsync(image);
            return image.Id;
        }

        /// <summary>
        /// The queued PRESET row exactly as the asset preset enqueue writes it (B-133): the instruction the preset
        /// assembled, plus the preset and its instruction checksum in the provenance and no compiler artifact.
        /// </summary>
        public async Task<string> CreateAssetPresetImageAsync(string presetKey, string instruction, string? instructionSha256 = null)
        {
            var preset = new MediaEditPresetInstruction(
                presetKey,
                instructionSha256 ?? MediaEditPresetProvenance.InstructionSha256(instruction));
            var image = new SceneAssetImage
            {
                AssetId = "asset-1",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Pending,
                Prompt = instruction,
                SourceImageId = SourceImageId,
                SourceProvenanceJson = JsonSerializer.Serialize(new
                {
                    operation = MediaEditProvenance.EditValue,
                    sourceImageSha256 = SourceSha256,
                    presetInstruction = preset
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            await Assets.UpsertImageAsync(image);
            return image.Id;
        }

        /// <summary>The handler an asset preset run needs: the writer armed with the preset resolver.</summary>
        public MediaEditImageEditingJobHandler BuildPresetHandler(
            IImageEditingClient editor,
            IImagePresetService presets)
        {
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(_root, "scene-images")
            });
            var storage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);
            var references = new MediaEditReferenceResolver(Assets, storage, new StubReferenceStrategies());
            var writer = new SceneAssetMediaEditSubjectWriter(
                Assets, Edits, storage, references, identityStorage: null, presets: presets);
            return new MediaEditImageEditingJobHandler(
                new MediaEditSubjectWriterResolver([writer]),
                OperationResolver(),
                new StubImageEditorResolver(),
                editor,
                new ImageRegionMaskEngine(),
                NullLogger<MediaEditImageEditingJobHandler>.Instance);
        }

        /// <summary>A run queued without the model the editor form carries.</summary>
        public DurableBackgroundJob JobWithoutEditorModel(string imageId)
            => new()
            {
                JobType = BackgroundJobTypes.MediaEditImageEditing,
                PayloadJson = JsonSerializer.Serialize(new MediaEditImageEditingJobPayload
                {
                    SubjectKind = MediaEditSubjectKind.AssetImage,
                    ImageId = imageId,
                    OperationKind = MediaEditOperationKind.Edit
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };

        /// <summary>The job an identity run needs an identity-capable editor and the identity face storage.</summary>
        public MediaEditImageEditingJobHandler BuildIdentityHandler(
            IImageEditingClient editor,
            ICharacterImageAssetStorageService identityStorage,
            IImageEditorModelResolver modelResolver)
        {
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(_root, "scene-images")
            });
            var storage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);
            var references = new MediaEditReferenceResolver(Assets, storage, new StubReferenceStrategies());
            var writer = new SceneAssetMediaEditSubjectWriter(Assets, Edits, storage, references, identityStorage);
            return new MediaEditImageEditingJobHandler(
                new MediaEditSubjectWriterResolver([writer]),
                OperationResolver(),
                modelResolver,
                editor,
                new ImageRegionMaskEngine(),
                NullLogger<MediaEditImageEditingJobHandler>.Instance);
        }

        public async Task<DurableBackgroundJob> EnqueueAssetRunAsync()
            => await EnqueueAssetRunAsync(EditedImageId);

        public async Task<DurableBackgroundJob> EnqueueAssetRunAsync(string imageId)
        {
            await Task.CompletedTask;
            return new DurableBackgroundJob
            {
                JobType = BackgroundJobTypes.MediaEditImageEditing,
                PayloadJson = JsonSerializer.Serialize(new MediaEditImageEditingJobPayload
                {
                    SubjectKind = MediaEditSubjectKind.AssetImage,
                    ImageId = imageId,
                    OperationKind = MediaEditOperationKind.Edit,
                    EditorModelId = EditorModelId
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
        }

        /// <summary>
        /// Queues a crop of a real, decodable source image the way the asset enqueue does: a new pending
        /// derived row plus a job whose operation is explicit.
        ///
        /// The fixture's own source is a header-only PNG stub, which is fine for the editor client (it
        /// never decodes) but not for a crop, which does.
        /// </summary>
        public async Task<(string ImageId, DurableBackgroundJob Job)> EnqueueAssetCropAsync(
            ImageCropMode mode = ImageCropMode.Framing,
            ImageCropHeadMeasurement? measurement = null,
            double targetAspect = 1.0,
            double headroomPercent = 50)
        {
            CropSourceImageId = await CreateDecodableSourceAsync();
            var cropSource = await Assets.GetImageAsync(CropSourceImageId);
            CropSourceSha256 = cropSource!.Sha256!;

            var operation = MediaEditOperation.ForCrop(new MediaEditCropOperation(
                mode, new ImageCropSettings(targetAspect, headroomPercent, 50), measurement));

            var image = new SceneAssetImage
            {
                AssetId = "asset-1",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Pending,
                SourceImageId = CropSourceImageId,
                SourceProvenanceJson = JsonSerializer.Serialize(
                    new { operation = "crop", sourceImageSha256 = CropSourceSha256 },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            await Assets.UpsertImageAsync(image);
            CroppedImageId = image.Id;

            var job = new DurableBackgroundJob
            {
                JobType = BackgroundJobTypes.MediaEditImageEditing,
                PayloadJson = JsonSerializer.Serialize(new MediaEditImageEditingJobPayload
                {
                    SubjectKind = MediaEditSubjectKind.AssetImage,
                    ImageId = image.Id,
                    OperationKind = MediaEditOperationKind.Crop,
                    OperationJson = JsonSerializer.Serialize(operation, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            return (image.Id, job);
        }

        /// <summary>Writes a real PNG and stores it as a complete source image row.</summary>
        public async Task<string> CreateDecodableSourceAsync(int width = 8, int height = 6)
        {
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(_root, "scene-images")
            });
            var storage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);

            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
            await using var content = new MemoryStream();
            await image.SaveAsync(content, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            content.Position = 0;

            var stored = await storage.SaveAsync("crop-source.png", content);
            var source = new SceneAssetImage
            {
                AssetId = "asset-1",
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = stored.RelativePath,
                Sha256 = stored.Sha256
            };
            await Assets.UpsertImageAsync(source);
            return source.Id;
        }

        /// <summary>An operation handler whose model resolver refuses to be used at all.</summary>
        public MediaEditImageEditingJobHandler BuildCropHandler(IImageEditingClient editor)
        {
            var options = Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={_dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(_root, "scene-images")
            });
            var storage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);
            var references = new MediaEditReferenceResolver(Assets, storage, new StubReferenceStrategies());
            var writer = new SceneAssetMediaEditSubjectWriter(Assets, Edits, storage, references);
            return new MediaEditImageEditingJobHandler(
                new MediaEditSubjectWriterResolver([writer]),
                OperationResolver(),
                new ThrowingImageEditorResolver(),
                editor,
                new ImageRegionMaskEngine(),
                NullLogger<MediaEditImageEditingJobHandler>.Instance);
        }

        /// <summary>The operation executors the shared job resolves; crop is the one these tests exercise.</summary>
        private static MediaEditOperationExecutorResolver OperationResolver()
            => new([new CropOperationExecutor(new ImageCropEngine())]);

        /// <summary>An edited image whose provenance declares a prompt checksum no revision carries.</summary>
        public async Task<string> CreateStaleEditedImageAsync()
        {
            var stale = new SceneAssetImage
            {
                AssetId = "asset-1",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Pending,
                Prompt = "Change the shirt to red.",
                SourceImageId = SourceImageId,
                SourceProvenanceJson = JsonSerializer.Serialize(new
                {
                    editSessionId = EditSessionId,
                    compilationAttemptId = "attempt-nobody-made",
                    promptRevisionId = "revision-nobody-made",
                    sourceImageSha256 = SourceSha256,
                    promptSha256 = new string('A', 64)
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            await Assets.UpsertImageAsync(stale);
            return stale.Id;
        }

        public async ValueTask DisposeAsync()
        {
            // No SqliteConnection.ClearAllPools(): process-wide and destabilises parallel tests.
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// The job claims the queued row before it pays for any work (debug record 066): a scene-image edit's
    /// completion is claim-guarded, so running without the claim would spend an editor call on a result the
    /// store could not record.
    /// </summary>
    [Fact]
    public async Task Run_ClaimsTheRowBeforeAnyWorkIsPaidFor()
    {
        var writer = new RecordingSubjectWriter();
        var editor = new RecordingImageEditor();

        await BuildWriterHandler(writer, editor).HandleAsync(JobFor(writer.ImageId));

        Assert.Equal(["claim", "complete"], writer.Order);
        Assert.Equal(1, editor.Calls);
    }

    /// <summary>A row that can no longer be claimed is already terminal: nothing is spent on it.</summary>
    [Fact]
    public async Task Run_SkipsWhenTheRowCanNoLongerBeClaimed()
    {
        var writer = new RecordingSubjectWriter { Claimable = false };
        var editor = new RecordingImageEditor();

        await BuildWriterHandler(writer, editor).HandleAsync(JobFor(writer.ImageId));

        Assert.Equal(["claim"], writer.Order);
        Assert.Equal(0, editor.Calls);
    }

    /// <summary>
    /// A masked-region edit is an EDIT, not a deterministic operation: it must run the editor path (with a mask),
    /// never the operation-executor resolver (which has no executor for <c>MaskedRegion</c>).
    /// </summary>
    [Fact]
    public async Task MaskedRegionRun_RoutesThroughTheEditorPath_WithAMask()
    {
        var writer = new RegionSubjectWriter();
        var editor = new RecordingImageEditor();
        var handler = new MediaEditImageEditingJobHandler(
            new MediaEditSubjectWriterResolver([writer]),
            // Empty on purpose: routing a MaskedRegion to the operation path would throw "no executor".
            new MediaEditOperationExecutorResolver([]),
            new Qwen21ImageEditorResolver(),
            editor,
            new ImageRegionMaskEngine(),
            NullLogger<MediaEditImageEditingJobHandler>.Instance);

        await handler.HandleAsync(JobForMaskedRegion(writer.ImageId));

        Assert.Equal(["claim", "complete"], writer.Order);
        Assert.Equal(1, editor.Calls);
        Assert.True(editor.LastEditMaskPresent, "A region edit must pass a mask to the editor.");
    }

    /// <summary>
    /// The writer must route a masked-region edit through the compiled-edit path, not the operation path: the real
    /// writer resolves the accepted prompt revision (a prompt-less operation plan would throw at the handler's
    /// <c>RequireEditParts</c>), then the run passes a mask. This is the other half of the dispatch fix above —
    /// that test uses a stub writer, which cannot catch a writer that mis-routes a region.
    /// </summary>
    [Fact]
    public async Task MaskedRegionRun_ThroughTheRealWriter_ResolvesTheCompiledPrompt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildRegionHandler(editor);

        var operation = MediaEditOperation.ForMaskedRegion(
            new MediaEditRegionOperation(10, 20, 30, 40, GrowMaskBy: 2, FeatherPixels: 3));
        var job = new DurableBackgroundJob
        {
            JobType = BackgroundJobTypes.MediaEditImageEditing,
            PayloadJson = JsonSerializer.Serialize(new MediaEditImageEditingJobPayload
            {
                SubjectKind = MediaEditSubjectKind.AssetImage,
                ImageId = fixture.EditedImageId,
                OperationKind = MediaEditOperationKind.MaskedRegion,
                OperationJson = JsonSerializer.Serialize(operation, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                EditorModelId = Fixture.EditorModelId
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        await handler.HandleAsync(job);

        var edited = await fixture.Assets.GetImageAsync(fixture.EditedImageId);
        Assert.Equal(SceneAssetStatus.Complete, edited!.Status);
        Assert.Equal(1, editor.Calls);
        Assert.Equal("Change the shirt to red.", Assert.Single(editor.Instructions));
        Assert.True(editor.LastEditMaskPresent, "A region edit must resolve its compiled prompt and pass a mask.");
    }

    /// <summary>
    /// The outpaint half of the same dispatch fix (CASE-24). An outpaint carries a compiled prompt revision exactly as a
    /// region does, so the writer must resolve it on the compiled-edit path: preparing it as a deterministic operation
    /// built a plan with no prompt at all, and the run died at the handler with "An edit run requires the compiled
    /// prompt its subject writer prepared" - which is what the operator hit when extending an Asset Studio image.
    /// </summary>
    [Fact]
    public async Task OutpaintRun_ThroughTheRealWriter_ResolvesTheCompiledPrompt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildRegionHandler(editor);

        await handler.HandleAsync(JobForOutpaint(fixture.EditedImageId, MediaEditSubjectKind.AssetImage));

        var edited = await fixture.Assets.GetImageAsync(fixture.EditedImageId);
        Assert.Equal(SceneAssetStatus.Complete, edited!.Status);
        Assert.Equal(1, editor.Calls);

        // The compiled revision AND the geometry sentence the run appends to it: proof that the instruction came from
        // the compiler output rather than from nowhere.
        var instruction = Assert.Single(editor.Instructions);
        Assert.Contains("Change the shirt to red.", instruction, StringComparison.Ordinal);
        Assert.Contains("Extend the image to the right", instruction, StringComparison.Ordinal);
        Assert.True(editor.LastEditMaskPresent, "An outpaint must pass a padded mask.");
    }

    /// <summary>
    /// An outpaint is an EDIT with a padded mask (CASE-24): it must run the editor path, build a mask at the padded
    /// canvas size, and carry the pads on the mask so the client can pad the source to match.
    /// </summary>
    [Fact]
    public async Task OutpaintRun_RoutesThroughTheEditorPath_WithAPaddedMask()
    {
        var writer = new OutpaintSubjectWriter();
        var editor = new RecordingImageEditor();
        var handler = new MediaEditImageEditingJobHandler(
            new MediaEditSubjectWriterResolver([writer]),
            new MediaEditOperationExecutorResolver([]),
            new Qwen21ImageEditorResolver(),
            editor,
            new ImageRegionMaskEngine(),
            NullLogger<MediaEditImageEditingJobHandler>.Instance);

        await handler.HandleAsync(JobForOutpaint(writer.ImageId));

        Assert.Equal(["claim", "complete"], writer.Order);
        Assert.Equal(1, editor.Calls);
        Assert.True(editor.LastEditMaskPresent, "An outpaint must pass a mask to the editor.");
    }

    /// <summary>
    /// CASE-25: the host confines with a mask it rounds to 0/1, so what it confines is a HARD rectangle - the region
    /// minus the feather that never reached it - and that edge used to survive into the render as a visible outline.
    /// A confined run now blends the render back over the frame it started from through the region's feather: the
    /// source comes back untouched everywhere outside the fade, the render owns everything inside it, and the two are
    /// mixed across the feather. This test reads the produced .png pixel by pixel, because "no outline" is exactly the
    /// kind of claim that has to be measured.
    /// </summary>
    [Fact]
    public async Task ConfinedRun_BlendsTheRenderBackOverTheSourceThroughTheFeather()
    {
        var source = new Rgb24(0, 0, 255);
        var rendered = new Rgb24(255, 0, 0);
        await using var fixture = await Fixture.CreateAsync(64, 64, source);
        var editor = new RecordingImageEditor { RenderColour = rendered };
        var handler = fixture.BuildRegionHandler(editor);

        // Drawn 16..48 on a 64px frame; the host may change 12..52 and the mask is white over 4..60.
        await handler.HandleAsync(JobForMaskedRegion(
            fixture.EditedImageId,
            new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 4, FeatherPixels: 8),
            MediaEditSubjectKind.AssetImage));

        var edited = await fixture.Assets.GetImageAsync(fixture.EditedImageId);
        Assert.Equal(SceneAssetStatus.Complete, edited!.Status);
        using var result = Image.Load<Rgb24>(await fixture.ReadStoredImageAsync(edited));

        Assert.Equal(64, result.Width);
        Assert.Equal(rendered, result[32, 32]);    // deep inside the region: the render, untouched
        Assert.Equal(rendered, result[12, 32]);    // the last fully opaque pixel
        Assert.Equal(new Rgb24(223, 0, 32), result[11, 32]);   // one pixel into the fade: 255/8 of the render
        Assert.Equal(new Rgb24(128, 0, 127), result[8, 32]);   // halfway through the fade
        Assert.Equal(source, result[4, 32]);       // the mask's white edge: the source, byte for byte
        Assert.Equal(source, result[0, 0]);        // and everywhere outside it

        // Nothing outside the fade may differ from the source at all - that is what "confined to a region" means, and
        // it is also what removes the faint halo a VAE round trip leaves over the whole frame.
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                if (x < 4 || x >= 60 || y < 4 || y >= 60)
                {
                    Assert.Equal(source, result[x, y]);
                }
            }
        }
    }

    /// <summary>
    /// An outpaint blends the same way, over the padded canvas the host renders: the newly exposed strip is kept whole,
    /// the original comes back untouched, and the seam between them fades instead of stepping.
    /// </summary>
    [Fact]
    public async Task OutpaintRun_KeepsTheNewStripAndFadesIntoTheOriginal()
    {
        var source = new Rgb24(0, 0, 255);
        var rendered = new Rgb24(255, 0, 0);
        var writer = new OutpaintSubjectWriter();
        var editor = new RecordingImageEditor { RenderColour = rendered };
        var handler = new MediaEditImageEditingJobHandler(
            new MediaEditSubjectWriterResolver([writer]),
            new MediaEditOperationExecutorResolver([]),
            new Qwen21ImageEditorResolver(),
            editor,
            new ImageRegionMaskEngine(),
            NullLogger<MediaEditImageEditingJobHandler>.Instance);

        // 50% of 8 = 4px added on the right: a 12x6 canvas, with the strip starting at x=8.
        await handler.HandleAsync(JobForOutpaint(writer.ImageId));

        Assert.Equal(["claim", "complete"], writer.Order);
        using var result = Image.Load<Rgb24>(writer.OutputBytes);

        Assert.Equal(12, result.Width);
        Assert.Equal(6, result.Height);
        Assert.Equal(rendered, result[11, 3]);     // the new strip: the render, kept whole
        Assert.Equal(rendered, result[6, 3]);      // the last fully opaque pixel of the seam (strip 8, grown by 2)
        Assert.Equal(new Rgb24(170, 0, 85), result[5, 3]);   // one pixel into the fade (feather 3)
        Assert.Equal(source, result[3, 3]);        // the mask's white edge: the source again
        Assert.Equal(source, result[0, 3]);        // and the far side of the original, untouched
    }

    /// <summary>
    /// A confined run without a feather is refused before a render is paid for, naming the persisted setting, and the
    /// queued row is marked failed rather than left looking pending. The host rounds the mask it confines with to 0/1,
    /// so a zero feather is a hard-edged rectangle whose edge shows in the picture as an outline (CASE-25).
    /// </summary>
    [Fact]
    public async Task ConfinedRun_WithoutAFeather_IsRefusedByNameAndMarksTheRow()
    {
        await using var fixture = await Fixture.CreateAsync(64, 64, new Rgb24(0, 0, 255));
        var editor = new RecordingImageEditor();
        var handler = fixture.BuildRegionHandler(editor);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            JobForMaskedRegion(
                fixture.EditedImageId,
                new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 4, FeatherPixels: 0),
                MediaEditSubjectKind.AssetImage)));

        Assert.Contains("RegionFeatherPixels", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, editor.Calls);

        var edited = await fixture.Assets.GetImageAsync(fixture.EditedImageId);
        Assert.Equal(SceneAssetStatus.Failed, edited!.Status);
        Assert.Contains("RegionFeatherPixels", edited.ErrorMessage, StringComparison.Ordinal);
    }

    private static DurableBackgroundJob JobForOutpaint(
        string imageId, MediaEditSubjectKind subjectKind = MediaEditSubjectKind.SceneImage)
    {
        var operation = MediaEditOperation.ForOutpaint(
            new MediaEditOutpaintOperation(MediaEditOutpaintDirection.Right, 50, GrowMaskBy: 2, FeatherPixels: 3));
        return new DurableBackgroundJob
        {
            JobType = BackgroundJobTypes.MediaEditImageEditing,
            PayloadJson = JsonSerializer.Serialize(new MediaEditImageEditingJobPayload
            {
                SubjectKind = subjectKind,
                ImageId = imageId,
                OperationKind = MediaEditOperationKind.Outpaint,
                OperationJson = JsonSerializer.Serialize(operation, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                EditorModelId = Fixture.EditorModelId
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }

    private static DurableBackgroundJob JobForMaskedRegion(
        string imageId,
        MediaEditRegionOperation? region = null,
        MediaEditSubjectKind subjectKind = MediaEditSubjectKind.SceneImage)
    {
        var operation = MediaEditOperation.ForMaskedRegion(
            region ?? new MediaEditRegionOperation(10, 20, 30, 40, GrowMaskBy: 2, FeatherPixels: 3));
        return new DurableBackgroundJob
        {
            JobType = BackgroundJobTypes.MediaEditImageEditing,
            PayloadJson = JsonSerializer.Serialize(new MediaEditImageEditingJobPayload
            {
                SubjectKind = subjectKind,
                ImageId = imageId,
                OperationKind = MediaEditOperationKind.MaskedRegion,
                OperationJson = JsonSerializer.Serialize(operation, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                EditorModelId = Fixture.EditorModelId
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }

    private static DurableBackgroundJob JobFor(string imageId)
        => new()
        {
            JobType = BackgroundJobTypes.MediaEditImageEditing,
            PayloadJson = JsonSerializer.Serialize(new MediaEditImageEditingJobPayload
            {
                SubjectKind = MediaEditSubjectKind.SceneImage,
                ImageId = imageId,
                OperationKind = MediaEditOperationKind.Edit,
                EditorModelId = Fixture.EditorModelId
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

    private static MediaEditImageEditingJobHandler BuildWriterHandler(
        IMediaEditSubjectWriter writer, IImageEditingClient editor)
        => new(
            new MediaEditSubjectWriterResolver([writer]),
            new MediaEditOperationExecutorResolver([]),
            new StubImageEditorResolver(),
            editor,
            new ImageRegionMaskEngine(),
            NullLogger<MediaEditImageEditingJobHandler>.Instance);

    /// <summary>
    /// A subject writer that records the order of the seam calls, so "claimed before anything ran" is
    /// asserted instead of assumed.
    /// </summary>
    private sealed class RecordingSubjectWriter : IMediaEditSubjectWriter
    {
        // A real PNG, not a header: the run hands this same stream to the editor, and the frame's size has to be
        // readable from it (the region engine identifies it, and a confined run decodes it to blend the render back).
        private static readonly byte[] SourceBytes = MakeDecodablePng(4, 3);

        public string ImageId { get; } = "scene-image-1";
        public bool Claimable { get; init; } = true;
        public List<string> Order { get; } = [];

        public MediaEditSubjectKind Kind => MediaEditSubjectKind.SceneImage;

        public Task<MediaEditRunPlan?> PrepareAsync(
            MediaEditRunContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<MediaEditRunPlan?>(new MediaEditRunPlan(
                ImageId,
                "source-1",
                _ => Task.FromResult<Stream>(new MemoryStream(SourceBytes)),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(SourceBytes)),
                MediaEditOperation.ForEdit,
                Prompt: "make the shirt red",
                References: [],
                Editor: new MediaEditEditorResolution(Fixture.EditorModelId, RequiresAdultContentPolicy: false),
                LogScope: "test"));

        public Task<bool> ClaimAsync(MediaEditRunContext context, CancellationToken cancellationToken = default)
        {
            Order.Add("claim");
            return Task.FromResult(Claimable);
        }

        public Task CompleteAsync(
            MediaEditRunPlan plan, MediaEditRunOutput output, CancellationToken cancellationToken = default)
        {
            Order.Add("complete");
            return Task.CompletedTask;
        }

        public Task FailAsync(
            MediaEditRunContext context, string error, CancellationToken cancellationToken = default)
        {
            Order.Add("fail");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingImageEditor : IImageEditingClient
    {
        public int Calls { get; private set; }

        /// <summary>Every instruction the run actually sent, in order.</summary>
        public List<string> Instructions { get; } = [];

        /// <summary>Whether the most recent edit call carried a mask (null before any edit).</summary>
        public bool? LastEditMaskPresent { get; private set; }

        public Exception? ThrowOnEdit { get; init; }

        /// <summary>
        /// The colour the stub renders, at the size of the source it was handed - which is what the real host does
        /// (it renders the frame it was given). A confined run blends that render back over the source, so a test can
        /// tell the two apart pixel by pixel and see exactly where the blend gave the source back.
        /// </summary>
        public Rgb24 RenderColour { get; init; } = new(0, 10, 20);

        public Task<byte[]> EditAsync(ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction, CancellationToken cancellationToken = default, ImageEditingMask? mask = null)
        {
            Calls++;
            Instructions.Add(instruction);
            LastEditMaskPresent = mask is not null;
            if (ThrowOnEdit is not null)
                throw ThrowOnEdit;
            return Task.FromResult(Render(sourceImage, mask));
        }

        public Task<byte[]> EditWithReferencesAsync(ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction, IReadOnlyList<ImageEditingReference> references, CancellationToken cancellationToken = default, ImageEditingMask? mask = null)
        {
            Calls++;
            Instructions.Add(instruction);
            LastEditMaskPresent = mask is not null;
            if (ThrowOnEdit is not null)
                throw ThrowOnEdit;
            return Task.FromResult(Render(sourceImage, mask));
        }

        private byte[] Render(Stream sourceImage, ImageEditingMask? mask)
        {
            // The host only needs the frame's SIZE to render a frame of its own, and several writers in this file hand
            // over a header-only PNG as their source: identifying it keeps the stub honest about the size without
            // demanding real pixels the run never needed.
            sourceImage.Position = 0;
            var info = Image.Identify(sourceImage)
                ?? throw new InvalidOperationException("The stub editor could not read the size of the frame it was handed.");
            sourceImage.Position = 0;

            // An outpaint hands the host pads with the mask, and the host extends the canvas before it renders (its own
            // pad node fills the new area with mid-grey). Reproduced here so an outpaint test renders the canvas the
            // composite has to place the result back on - and renders it at the padded size, as the host does.
            using var render = new Image<Rgb24>(
                info.Width + (mask?.LeftPad ?? 0) + (mask?.RightPad ?? 0),
                info.Height + (mask?.TopPad ?? 0) + (mask?.BottomPad ?? 0),
                RenderColour);
            using var buffer = new MemoryStream();
            render.SaveAsPng(buffer);
            return buffer.ToArray();
        }
    }

    /// <summary>
    /// The preset resolver as the writer sees it (B-133). A stub rather than the store-backed service: these tests
    /// are about the run path, and a fixed instruction is what makes "the run sent exactly the assembled text" an
    /// assertion instead of a tautology.
    /// </summary>
    private sealed class StubPresetService : IImagePresetService
    {
        private readonly string _instruction;

        public StubPresetService(string instruction) => _instruction = instruction;

        public Task<string> ResolveInstructionAsync(
            string presetKey, ImagePresetMode mode = ImagePresetMode.Change, string? characterId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_instruction);

        public Task<IReadOnlyList<ImagePresetChoice>> ListAsync(
            ImagePresetAxis? axis = null, string? characterId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ImagePresetChoice>>([]);

        public string DefaultPresetFor(string loraVocabularyKey) => ImagePresetKeys.PresetKeyFor(loraVocabularyKey);
    }

    /// <summary>
    /// A resolver that fails on any use. An operation run must never resolve an editor model, so a crop
    /// test that completes while this is wired proves the resolution path was not taken.
    /// </summary>
    private sealed class ThrowingImageEditorResolver : IImageEditorModelResolver
    {
        public Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("An operation run must not resolve an editor model.");

        public Task<ResolvedImageEditorModel> ResolveByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("An operation run must not resolve an editor model by id.");

        public Task<IReadOnlyList<SceneImageModelChoice>> ListImageEditorModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageModelChoice>>([]);
    }

    private sealed class StubImageEditorResolver : IImageEditorModelResolver
    {
        public Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ResolvedModel());

        public Task<ResolvedImageEditorModel> ResolveByIdAsync(string modelId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Fixture.EditorModelId, modelId);
            return Task.FromResult(ResolvedModel());
        }

        public Task<IReadOnlyList<SceneImageModelChoice>> ListImageEditorModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageModelChoice>>([]);

        private static ResolvedImageEditorModel ResolvedModel() => new(
            ComfyUiUrl: "http://192.168.0.16:8188",
            ProviderTimeoutSeconds: 120,
            ApiKeyEncrypted: null,
            ModelIdentifier: "Qwen-Rapid-AIO-NSFW-v23.safetensors",
            ProviderName: "Local ComfyUI",
            ContentPolicy: ImageContentPolicy.AdultAllowed,
            DiffusionModel: "diffusion.safetensors",
            TextEncoder: "text_encoder.safetensors",
            Vae: "vae.safetensors",
            Steps: 8,
            Cfg: 1.0,
            Sampler: "euler_ancestral",
            Scheduler: "beta",
            Denoise: 1.0,
            AuraFlowShift: 3.1,
            CfgNormStrength: 1.0,
            ImageProtocol: ImageProtocol.ComfyUi,
            RegisteredModelId: "22222222-2222-2222-2222-222222222222",
            GraphKind: ImageEditorGraphKind.MergedCheckpoint);
    }

    private sealed class StubReferenceStrategies : IReferenceStrategyResolver
    {
        public Task<ReferenceStrategyResolution> ResolveAsync(string registeredModelId, string strategy, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// A model that qualifies the strategy a binding declares, which is what the editor model row actually holds:
    /// `SupportedVisualStrategiesJson = ["NativeMultiReference"]` with a Qualified capability entry for it.
    /// <see cref="ResolveEveryBindingAs"/> stands in for a model whose only qualified strategy is one the edit surface
    /// has no graph for.
    /// </summary>
    private sealed class QualifiedEditStrategies : IReferenceStrategyResolver
    {
        public string? ResolveEveryBindingAs { get; init; }

        public Task<ReferenceStrategyResolution> ResolveAsync(
            string registeredModelId, string strategy, CancellationToken cancellationToken = default)
        {
            var resolved = ResolveEveryBindingAs ?? strategy;
            return Task.FromResult(new ReferenceStrategyResolution(
                ReferenceStrategyResolutionStatus.Possible,
                resolved,
                $"'{resolved}' is declared and qualified for model '{registeredModelId}'."));
        }
    }

    /// <summary>
    /// The character's approved identity pack and its approved build, which is the store a pack binding is addressed
    /// in. Mirrors the fixture's face asset so both axes resolve.
    /// </summary>
    private sealed class StubIdentityRepository : ICharacterImageIdentityRepository
    {
        private static readonly CharacterImageIdentityPack Pack = new()
        {
            Id = "pack-1",
            CharacterTemplateId = "character-1",
            Version = 3,
            Status = CharacterImageIdentityPackStatus.Approved,
            CanonicalFullBodyAssetId = "body-1"
        };

        private static readonly SceneImageReferenceAsset Body = new()
        {
            Id = "body-1",
            IdentityPackId = "pack-1",
            AssetKind = SceneImageReferenceAssetKind.FullBody,
            IsApproved = true,
            BodyView = SceneImageReferenceBodyView.Front,
            BodyState = SceneImageReferenceBodyState.Unclothed,
            FileRelativePath = Fixture.IdentityFaceRelativePath,
            Sha256 = Fixture.IdentityFaceSha256
        };

        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult<CharacterImageIdentityPack?>(packId == Pack.Id ? Pack : null);

        public Task<SceneImageReferenceAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneImageReferenceAsset?>(assetId == Body.Id ? Body : null);

        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
            string characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListApprovedPacksAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack?> GetLatestApprovedPackAsync(
            string characterProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> UpsertDraftAsync(
            CharacterImageIdentityPack value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> ApproveAsync(
            string packId, string descriptorSnapshotJson, string canonicalFaceAssetId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CharacterImageIdentityPack> SupersedeAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddAssetAsync(SceneImageReferenceAsset value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(
            string packId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAssetProvenanceAsync(
            string assetId, string sourceLabel, SceneImageReferenceConsentState consentState,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetAssetApprovalAsync(
            string assetId, bool isApproved, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAssetQualityAsync(
            string assetId, SceneImageReferenceQuality qualityRating, string qualityNotes,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountAssetsByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Serves the approved identity-pack face bytes and records which reference file was read.</summary>
    private sealed class RecordingIdentityStorage : ICharacterImageAssetStorageService
    {
        public List<string> Opened { get; } = [];

        public Task<StoredCharacterImageAsset> SaveAsync(
            string characterProfileId, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            Opened.Add(relativePath);
            return Task.FromResult<Stream>(new MemoryStream(CreatePng(4, 3)));
        }

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Records what an identity run actually sent, references included.</summary>
    private sealed class ReferenceRecordingImageEditor : IImageEditingClient
    {
        public List<(ResolvedImageEditorModel Model, string Instruction, IReadOnlyList<ImageEditingReference> References)> ReferenceCalls { get; } = [];

        public Task<byte[]> EditAsync(
            ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction,
            CancellationToken cancellationToken = default,
            ImageEditingMask? mask = null)
            => throw new InvalidOperationException("An identity run must always send its references.");

        public Task<byte[]> EditWithReferencesAsync(
            ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction,
            IReadOnlyList<ImageEditingReference> references, CancellationToken cancellationToken = default,
            ImageEditingMask? mask = null)
        {
            ReferenceCalls.Add((model, instruction, references));
            return Task.FromResult(CreatePng(4, 3));
        }
    }

    /// <summary>
    /// Resolves only by the id the run carried. An identity run that fell back to the configured default would
    /// trip the counter (and the throw), which is the defect this guards.
    /// </summary>
    private sealed class ChosenModelOnlyResolver : IImageEditorModelResolver
    {
        public int ByIdCalls { get; private set; }

        public int DefaultCalls { get; private set; }

        public Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default)
        {
            DefaultCalls++;
            throw new InvalidOperationException("An identity run must use the model carried on the job, not the default.");
        }

        public Task<ResolvedImageEditorModel> ResolveByIdAsync(string modelId, CancellationToken cancellationToken = default)
        {
            ByIdCalls++;
            Assert.Equal(Fixture.EditorModelId, modelId);
            return Task.FromResult(new ResolvedImageEditorModel(
                ComfyUiUrl: "http://192.168.0.16:8188",
                ProviderTimeoutSeconds: 120,
                ApiKeyEncrypted: null,
                ModelIdentifier: "Qwen-Rapid-AIO-NSFW-v23.safetensors",
                ProviderName: "Local ComfyUI",
                ContentPolicy: ImageContentPolicy.AdultAllowed,
                DiffusionModel: "diffusion.safetensors",
                TextEncoder: "text_encoder.safetensors",
                Vae: "vae.safetensors",
                Steps: 8,
                Cfg: 1.0,
                Sampler: "euler_ancestral",
                Scheduler: "beta",
                Denoise: 1.0,
                AuraFlowShift: 3.1,
                CfgNormStrength: 1.0,
                ImageProtocol: ImageProtocol.ComfyUi,
                RegisteredModelId: Fixture.EditorModelId,
                GraphKind: ImageEditorGraphKind.MergedCheckpoint));
        }

        public Task<IReadOnlyList<SceneImageModelChoice>> ListImageEditorModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageModelChoice>>([]);
    }

    /// <summary>
    /// A subject writer whose plan is a MASKED-REGION edit (an edit that also carries a region), with a decodable
    /// source so the region-mask engine can measure it.
    /// </summary>
    private sealed class RegionSubjectWriter : IMediaEditSubjectWriter
    {
        private static readonly byte[] SourceBytes = MakeDecodablePng(8, 6);

        public string ImageId { get; } = "scene-region-1";
        public List<string> Order { get; } = [];

        public MediaEditSubjectKind Kind => MediaEditSubjectKind.SceneImage;

        public Task<MediaEditRunPlan?> PrepareAsync(
            MediaEditRunContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<MediaEditRunPlan?>(new MediaEditRunPlan(
                ImageId,
                "source-1",
                _ => Task.FromResult<Stream>(new MemoryStream(SourceBytes)),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(SourceBytes)),
                MediaEditOperation.ForMaskedRegion(new MediaEditRegionOperation(10, 20, 30, 40, GrowMaskBy: 2, FeatherPixels: 3)),
                Prompt: "change the shirt to red inside the region",
                References: [],
                Editor: new MediaEditEditorResolution(Fixture.EditorModelId, RequiresAdultContentPolicy: false),
                LogScope: "test"));

        public Task<bool> ClaimAsync(MediaEditRunContext context, CancellationToken cancellationToken = default)
        {
            Order.Add("claim");
            return Task.FromResult(true);
        }

        public Task CompleteAsync(
            MediaEditRunPlan plan, MediaEditRunOutput output, CancellationToken cancellationToken = default)
        {
            Order.Add("complete");
            return Task.CompletedTask;
        }

        public Task FailAsync(
            MediaEditRunContext context, string error, CancellationToken cancellationToken = default)
        {
            Order.Add("fail");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A subject writer whose plan is an OUTPAINT (an edit that also carries a direction + percent), with a decodable
    /// source so the region-mask engine can build the strip mask at the padded size.
    /// </summary>
    private sealed class OutpaintSubjectWriter : IMediaEditSubjectWriter
    {
        private static readonly byte[] SourceBytes = MakeDecodablePng(8, 6, new Rgb24(0, 0, 255));

        public string ImageId { get; } = "scene-outpaint-1";
        public List<string> Order { get; } = [];

        /// <summary>The bytes the run decided to keep, so a test can read what the composite produced.</summary>
        public byte[]? OutputBytes { get; private set; }

        public MediaEditSubjectKind Kind => MediaEditSubjectKind.SceneImage;

        public Task<MediaEditRunPlan?> PrepareAsync(
            MediaEditRunContext context, CancellationToken cancellationToken = default)
            => Task.FromResult<MediaEditRunPlan?>(new MediaEditRunPlan(
                ImageId,
                "source-1",
                _ => Task.FromResult<Stream>(new MemoryStream(SourceBytes)),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(SourceBytes)),
                MediaEditOperation.ForOutpaint(
                    new MediaEditOutpaintOperation(MediaEditOutpaintDirection.Right, 50, GrowMaskBy: 2, FeatherPixels: 3)),
                Prompt: "extend the image to the right",
                References: [],
                Editor: new MediaEditEditorResolution(Fixture.EditorModelId, RequiresAdultContentPolicy: false),
                LogScope: "test"));

        public Task<bool> ClaimAsync(MediaEditRunContext context, CancellationToken cancellationToken = default)
        {
            Order.Add("claim");
            return Task.FromResult(true);
        }

        public Task CompleteAsync(
            MediaEditRunPlan plan, MediaEditRunOutput output, CancellationToken cancellationToken = default)
        {
            Order.Add("complete");
            OutputBytes = output.Bytes;
            return Task.CompletedTask;
        }

        public Task FailAsync(
            MediaEditRunContext context, string error, CancellationToken cancellationToken = default)
        {
            Order.Add("fail");
            return Task.CompletedTask;
        }
    }

    /// <summary>A region edit needs the 2.1 native graph; the shared stub resolves a merged checkpoint.</summary>
    private sealed class Qwen21ImageEditorResolver : IImageEditorModelResolver
    {
        public Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Model());

        public Task<ResolvedImageEditorModel> ResolveByIdAsync(string modelId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Fixture.EditorModelId, modelId);
            return Task.FromResult(Model());
        }

        public Task<IReadOnlyList<SceneImageModelChoice>> ListImageEditorModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageModelChoice>>([]);

        private static ResolvedImageEditorModel Model() => new(
            "http://192.168.0.16:8188", 120, null, "qwen-image-2.1", "Local ComfyUI", ImageContentPolicy.AdultAllowed,
            "diffusion.safetensors", "text_encoder.safetensors", "vae.safetensors", 25, 1.0, "euler", "simple", 1.0, 0.0, 0.0,
            GraphKind: ImageEditorGraphKind.QwenImage21Native);
    }

    private static byte[] MakeDecodablePng(int width, int height, Rgb24? colour = null)
    {
        var fill = colour is { } chosen ? new Rgba32(chosen.R, chosen.G, chosen.B, 255) : new Rgba32(0, 0, 0, 0);
        using var image = new Image<Rgba32>(width, height, fill);
        using var content = new MemoryStream();
        image.Save(content, new PngEncoder());
        return content.ToArray();
    }

    private static byte[] CreatePng(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        return bytes;
    }
}
