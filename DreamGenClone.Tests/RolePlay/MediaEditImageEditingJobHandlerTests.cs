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
        public string EditedImageId { get; private set; } = string.Empty;
        public string CroppedImageId { get; private set; } = string.Empty;
        public string CropSourceImageId { get; private set; } = string.Empty;
        public string CropSourceSha256 { get; private set; } = string.Empty;
        public string EditSessionId { get; private set; } = string.Empty;
        public string _editorStorageRoot => Path.Combine(_root, "assets");

        public static async Task<Fixture> CreateAsync()
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
            var fixture = new Fixture(dbPath, root, assets, edits);

            await assets.UpsertAsync(new SceneAsset
            {
                Id = "asset-1", Name = "Asset one", Type = SceneAssetType.CharacterFace, Status = SceneAssetStatus.Complete
            });

            var png = CreatePng(4, 3);
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
                NullLogger<MediaEditImageEditingJobHandler>.Instance);
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
            NullLogger<MediaEditImageEditingJobHandler>.Instance);

    /// <summary>
    /// A subject writer that records the order of the seam calls, so "claimed before anything ran" is
    /// asserted instead of assumed.
    /// </summary>
    private sealed class RecordingSubjectWriter : IMediaEditSubjectWriter
    {
        private static readonly byte[] SourceBytes = CreatePng(4, 3);

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
        public Exception? ThrowOnEdit { get; init; }

        public Task<byte[]> EditAsync(ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowOnEdit is not null)
                throw ThrowOnEdit;
            return Task.FromResult(CreatePng(4, 3));
        }

        public Task<byte[]> EditWithReferencesAsync(ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction, IReadOnlyList<ImageEditingReference> references, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowOnEdit is not null)
                throw ThrowOnEdit;
            return Task.FromResult(CreatePng(4, 3));
        }
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
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("An identity run must always send its references.");

        public Task<byte[]> EditWithReferencesAsync(
            ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction,
            IReadOnlyList<ImageEditingReference> references, CancellationToken cancellationToken = default)
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

    private static byte[] CreatePng(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        return bytes;
    }
}
