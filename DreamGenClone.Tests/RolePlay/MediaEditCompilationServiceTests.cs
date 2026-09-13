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
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The ONE compilation service (B-124 B124-012). Both subject kinds must run through the same code
/// path with the same lifecycle, so a fix can no longer land on just one of them.
/// </summary>
public sealed class MediaEditCompilationServiceTests
{
    [Fact]
    public async Task SceneImageSession_CapturesTheChecksumFromTheStoredFile()
    {
        await using var fixture = await Fixture.CreateAsync();

        var session = await fixture.CreateSceneSessionAsync();

        Assert.Equal(MediaEditSubjectKind.SceneImage, session.SubjectKind);
        Assert.Equal("i1", session.SubjectId);
        Assert.Equal("s1", session.SubjectScopeId);
        Assert.Equal(64, session.SourceImageSha256.Length);
        Assert.DoesNotContain("sha", fixture.Queue.Payloads(), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await fixture.EditRepository.GetSessionAsync(session.Id));
    }

    [Fact]
    public async Task AssetImageSession_RunsThroughTheSamePath()
    {
        await using var fixture = await Fixture.CreateAsync();

        var session = await fixture.CreateAssetSessionAsync();

        Assert.Equal(MediaEditSubjectKind.AssetImage, session.SubjectKind);
        Assert.Equal("asset-1", session.SubjectId);
        Assert.Null(session.SubjectScopeId);
        Assert.Equal(64, session.SourceImageSha256.Length);
    }

    [Fact]
    public async Task Session_RefusesASourceImageOwnedByAnotherSubject()
    {
        await using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateSessionAsync(
            new CreateMediaEditSessionRequest
            {
                SubjectKind = MediaEditSubjectKind.SceneImage,
                SubjectId = "some-other-interaction",
                SubjectScopeId = "s1",
                SourceImageId = fixture.SceneSource.Id
            }));

        Assert.Contains("owned by the selected interaction", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compilation_AllocatesOrdinalsAndQueuesTheSharedJob()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = await fixture.CreateSceneSessionAsync();

        var attempt = await fixture.Service.EnqueueCompilationAsync(new EnqueueMediaEditCompilationRequest
        {
            EditSessionId = session.Id,
            RawIntent = "change the shirt to red"
        });

        Assert.Equal(0, attempt.Ordinal);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Pending, attempt.Status);
        Assert.DoesNotContain("enc:secret", attempt.ResolvedModelSnapshotJson, StringComparison.Ordinal);
        var job = Assert.Single(fixture.Queue.Jobs);
        Assert.Equal(BackgroundJobTypes.MediaEditPromptCompilation, job.JobType);
        Assert.Equal($"{BackgroundJobTypes.MediaEditPromptCompilation}:{attempt.Id}", job.DedupeKey);
        Assert.Equal(attempt.Id, JsonSerializer.Deserialize<MediaEditCompilationJobPayload>(job.PayloadJson, Json)! .AttemptId);

        var second = await fixture.Service.EnqueueCompilationAsync(new EnqueueMediaEditCompilationRequest
        {
            EditSessionId = session.Id,
            RawIntent = "change her shirt",
            ClarificationHistory = ["The woman in the foreground on the left."]
        });

        Assert.Equal(1, second.Ordinal);
        Assert.Equal("[\"The woman in the foreground on the left.\"]", second.ClarificationContextJson);
    }

    [Fact]
    public async Task Compilation_RejectsEmptyIntentAndEmptyClarificationEntries()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = await fixture.CreateSceneSessionAsync();

        var emptyIntent = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.EnqueueCompilationAsync(new EnqueueMediaEditCompilationRequest { EditSessionId = session.Id, RawIntent = "  " }));
        Assert.Contains("non-empty edit intent", emptyIntent.Message, StringComparison.Ordinal);

        var emptyHistory = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.EnqueueCompilationAsync(new EnqueueMediaEditCompilationRequest
            {
                EditSessionId = session.Id,
                RawIntent = "change it",
                ClarificationHistory = ["  "]
            }));
        Assert.Contains("Clarification history", emptyHistory.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedSession_CannotBeRecompiled()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = await fixture.CreateSceneSessionAsync();
        await fixture.EditRepository.UpdateSessionStatusAsync(
            session.Id, MediaEditSessionStatus.Ready, DateTime.UtcNow);
        await fixture.EditRepository.UpdateSessionStatusAsync(
            session.Id, MediaEditSessionStatus.Completed, DateTime.UtcNow, DateTime.UtcNow);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.EnqueueCompilationAsync(new EnqueueMediaEditCompilationRequest
            {
                EditSessionId = session.Id,
                RawIntent = "change it"
            }));

        Assert.Contains("completed media edit session cannot be changed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Description_IsQueuedOnceAndSkippedWhenAlreadyPresent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = await fixture.CreateAssetSessionAsync();

        await fixture.Service.EnqueueDescriptionAsync(session.Id);
        var job = Assert.Single(fixture.Queue.Jobs);
        Assert.Equal(BackgroundJobTypes.MediaEditDescription, job.JobType);
        Assert.Equal(session.Id, JsonSerializer.Deserialize<MediaEditDescriptionJobPayload>(job.PayloadJson, Json)!.EditSessionId);

        // A description that already exists is not re-queued unless forced.
        await fixture.EditRepository.SetDescriptionAsync(session.Id, "A person standing.", DateTime.UtcNow);
        fixture.Queue.Jobs.Clear();
        await fixture.Service.EnqueueDescriptionAsync(session.Id);
        Assert.Empty(fixture.Queue.Jobs);

        await fixture.Service.EnqueueDescriptionAsync(session.Id, force: true);
        Assert.Single(fixture.Queue.Jobs);

        fixture.Queue.Accepting = false;
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.EnqueueDescriptionAsync(session.Id, force: true));
        Assert.Contains("already queued", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revision_AppendsToTheLatestAttemptWithAComputedChecksum()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = await fixture.CreateSceneSessionAsync();
        var attempt = await fixture.Service.EnqueueCompilationAsync(new EnqueueMediaEditCompilationRequest
        {
            EditSessionId = session.Id,
            RawIntent = "change the shirt to red"
        });

        var revision = await fixture.Service.AppendPromptRevisionAsync(new AppendMediaEditPromptRevisionRequest
        {
            EditSessionId = session.Id,
            CompilationAttemptId = attempt.Id,
            Prompt = "  Change only the foreground woman's shirt to crimson.  "
        });

        Assert.Equal(0, revision.Ordinal);
        Assert.Equal(SceneImageEditPromptRevisionKind.UserEdited, revision.RevisionKind);
        Assert.Equal("Change only the foreground woman's shirt to crimson.", revision.Prompt);
        Assert.Equal(64, revision.PromptSha256.Length);
        Assert.Single(await fixture.Service.ListRevisionsAsync(attempt.Id));

        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.AppendPromptRevisionAsync(new AppendMediaEditPromptRevisionRequest
            {
                EditSessionId = session.Id,
                CompilationAttemptId = "missing",
                Prompt = "x"
            }));
        Assert.Contains("was not found", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionCreation_AcceptsARowThatPredatesStoredChecksums()
    {
        // 351 of 607 scene images on the dev DB carry no checksum; for those the stored file is
        // authoritative, so the editor must still open.
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ClearSceneSourceChecksumAsync();

        var session = await fixture.CreateSceneSessionAsync();

        Assert.Equal(64, session.SourceImageSha256.Length);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _dbPath;
        private readonly string _root;

        private Fixture(
            string dbPath,
            string root,
            MediaEditCompilationService service,
            MediaEditRepository editRepository,
            CapturingDurableQueue queue,
            SceneImageRecord sceneSource,
            SceneAssetImage assetSource)
        {
            _dbPath = dbPath;
            _root = root;
            Service = service;
            EditRepository = editRepository;
            Queue = queue;
            SceneSource = sceneSource;
            AssetSource = assetSource;
        }

        public MediaEditCompilationService Service { get; }
        public MediaEditRepository EditRepository { get; }
        public CapturingDurableQueue Queue { get; }
        public SceneImageRecord SceneSource { get; }
        public SceneAssetImage AssetSource { get; }

        public static async Task<Fixture> CreateAsync(
            IImageEditorModelResolver? editorModels = null,
            IImageEditorEndpointReadiness? endpointReadiness = null)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"media-edit-service-{Guid.NewGuid():N}.db");
            var root = Path.Combine(Path.GetTempPath(), $"media-edit-service-files-{Guid.NewGuid():N}");
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

            var png = CreatePng(4, 3);
            var sceneStorage = new SceneImageStorageService(options, NullLogger<SceneImageStorageService>.Instance);
            await using var sceneStream = new MemoryStream(png);
            var scenePath = await sceneStorage.SaveAsync("s1", "source.png", sceneStream);
            var sceneRepository = new SceneImageRepository(options);
            var sceneSource = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = "prompt",
                PromptSnapshot = "scene source",
                Status = SceneImageStatus.Complete,
                FileRelativePath = scenePath,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png))
            };
            await sceneRepository.InsertImageAsync(sceneSource);

            var assetStorage = new SceneAssetStorageService(options, NullLogger<SceneAssetStorageService>.Instance);
            await using var assetStream = new MemoryStream(png);
            var storedAsset = await assetStorage.SaveAsync("asset-source.png", assetStream);
            var assetRepository = new SceneAssetRepository(options);
            await assetRepository.UpsertAsync(new SceneAsset
            {
                Id = "asset-1",
                Name = "Asset one",
                Type = SceneAssetType.CharacterFace,
                Status = SceneAssetStatus.Complete
            });
            var assetSource = new SceneAssetImage
            {
                AssetId = "asset-1",
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = storedAsset.RelativePath,
                Sha256 = storedAsset.Sha256
            };
            await assetRepository.UpsertImageAsync(assetSource);

            var editRepository = new MediaEditRepository(options);
            await editRepository.EnsureSchemaAsync();

            var sources = new MediaEditSubjectSourceResolver(
            [
                new SceneImageMediaEditSubjectSource(sceneRepository, sceneStorage),
                new SceneAssetMediaEditSubjectSource(assetRepository, assetStorage)
            ]);
            var queue = new CapturingDurableQueue();
            var service = new MediaEditCompilationService(
                editRepository,
                sources,
                new StubMultimodalResolver(),
                new QwenSceneImageEditPromptCompiler(),
                queue,
                new StubDurableSettings(),
                TimeProvider.System,
                editorModels ?? new StubEditorModels(),
                endpointReadiness ?? new StubEndpointReadiness());

            return new Fixture(dbPath, root, service, editRepository, queue, sceneSource, assetSource);
        }

        public Task<MediaEditSession> CreateSceneSessionAsync()
            => Service.CreateSessionAsync(new CreateMediaEditSessionRequest
            {
                SubjectKind = MediaEditSubjectKind.SceneImage,
                SubjectId = SceneSource.InteractionId,
                SubjectScopeId = SceneSource.SessionId,
                SourceImageId = SceneSource.Id
            });

        public Task<MediaEditSession> CreateAssetSessionAsync()
            => Service.CreateSessionAsync(new CreateMediaEditSessionRequest
            {
                SubjectKind = MediaEditSubjectKind.AssetImage,
                SubjectId = AssetSource.AssetId,
                SourceImageId = AssetSource.Id
            });

        public async Task ClearSceneSourceChecksumAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE SceneImages SET Sha256 = '' WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", SceneSource.Id);
            await command.ExecuteNonQueryAsync();
            SceneSource.Sha256 = string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            // No SqliteConnection.ClearAllPools() here: it is process-wide and would destabilise
            // other test classes running in parallel. These connections use Pooling=False.
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); } catch (IOException) { }
            }
        }
    }

    private sealed class CapturingDurableQueue : IDurableBackgroundJobQueue
    {
        public List<(string JobType, string PayloadJson, string DedupeKey, DurableBackgroundJobStatus Status, int MaxAttempts)> Jobs { get; } = [];
        public bool Accepting { get; set; } = true;

        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        {
            if (!Accepting)
                return Task.FromResult(false);

            Jobs.Add((job.JobType, job.PayloadJson, job.DedupeKey, job.Status, job.MaxAttempts));
            return Task.FromResult(true);
        }

        public string Payloads() => string.Join("|", Jobs.Select(job => job.PayloadJson));

        public Task<bool> TryActivateAsync(string jobId, DateTime activatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WaitForWorkAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubMultimodalResolver : IMultimodalModelResolutionService
    {
        public Task<ResolvedMultimodalModel> ResolveAsync(AppFunction function, CancellationToken cancellationToken = default)
        {
            Assert.Equal(AppFunction.RolePlaySceneImageEditPromptCompiler, function);
            return Task.FromResult(new ResolvedMultimodalModel(
                "provider-1", "model-1", "https://vision.internal", "/v1/chat/completions", "/v1/models",
                "{\"model\":\"qwen-vl\"}", 30, 420, 30, "vision-api-key", "enc:secret", "qwen-vl", "Vision",
                ImageContentPolicy.AdultAllowed, ModelLifecycleStrategy.ScheduledSinglePod, 1, 1024, 4096, 64,
                new HashSet<string>(["image/png", "image/jpeg", "image/webp"], StringComparer.OrdinalIgnoreCase),
                1024, 1, 4, 0.2, 0.8, 512, "vllm-revision", "model-revision"));
        }
    }

    /// <summary>The compile flow never picks an editor model; a call here would be a real defect.</summary>
    private sealed class StubEditorModels : IImageEditorModelResolver
    {
        public Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedImageEditorModel> ResolveByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageModelChoice>> ListImageEditorModelsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task EnqueueRunAsync_RunsImmediatelyForALocalComfyUiModel()
    {
        // A local ComfyUI endpoint is always running, so there is no cold start to wait for and the
        // run is admitted immediately. Warmth must not even be probed for it.
        await using var fixture = await Fixture.CreateAsync(
            new FixedEditorModels(DreamGenClone.Domain.ModelManager.ImageProtocol.ComfyUi),
            new FixedEndpointReadiness(warm: false));

        await fixture.Service.EnqueueRunAsync(new MediaEditRunRequest(
            MediaEditSubjectKind.AssetImage, "image-1", "22222222-2222-2222-2222-222222222222", MaxAttempts: 3));

        var job = Assert.Single(fixture.Queue.Jobs);
        Assert.Equal(BackgroundJobTypes.MediaEditImageEditing, job.JobType);
        Assert.Equal(DurableBackgroundJobStatus.Queued, job.Status);
        Assert.Equal($"{BackgroundJobTypes.MediaEditImageEditing}:image-1", job.DedupeKey);
        Assert.Equal(3, job.MaxAttempts);
        Assert.Contains("22222222-2222-2222-2222-222222222222", job.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnqueueRunAsync_StagesAServerlessRunUntilItIsWarm()
    {
        // Only a serverless endpoint can be cold: it stages for a user start, and runs when warm.
        await using var cold = await Fixture.CreateAsync(
            new FixedEditorModels(DreamGenClone.Domain.ModelManager.ImageProtocol.ComfyUiServerless),
            new FixedEndpointReadiness(warm: false));
        await cold.Service.EnqueueRunAsync(new MediaEditRunRequest(
            MediaEditSubjectKind.SceneImage, "image-2", "22222222-2222-2222-2222-222222222222", MaxAttempts: 1));
        Assert.Equal(DurableBackgroundJobStatus.Staged, Assert.Single(cold.Queue.Jobs).Status);

        await using var warm = await Fixture.CreateAsync(
            new FixedEditorModels(DreamGenClone.Domain.ModelManager.ImageProtocol.ComfyUiServerless),
            new FixedEndpointReadiness(warm: true));
        await warm.Service.EnqueueRunAsync(new MediaEditRunRequest(
            MediaEditSubjectKind.SceneImage, "image-3", "22222222-2222-2222-2222-222222222222", MaxAttempts: 1));
        Assert.Equal(DurableBackgroundJobStatus.Queued, Assert.Single(warm.Queue.Jobs).Status);
    }

    [Fact]
    public async Task EnqueueRunAsync_RefusesToChooseAModelForItself()
    {
        // The model is chosen by the user in the edit form and carried; the run never picks one.
        await using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.EnqueueRunAsync(
            new MediaEditRunRequest(MediaEditSubjectKind.AssetImage, "image-4", "   ", MaxAttempts: 1)));

        Assert.Contains("explicitly chosen", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Queue.Jobs);
    }

    /// <summary>Only serverless admission probes warmth; a call here for a local model would be a defect.</summary>
    private sealed class StubEndpointReadiness : IImageEditorEndpointReadiness
    {
        public Task<bool> IsWarmAsync(ResolvedImageEditorModel model, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FixedEditorModels : IImageEditorModelResolver
    {
        private readonly ResolvedImageEditorModel _model;

        public FixedEditorModels(DreamGenClone.Domain.ModelManager.ImageProtocol protocol) => _model = EditorModel(protocol);

        public Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_model);

        public Task<ResolvedImageEditorModel> ResolveByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(_model);

        public Task<IReadOnlyList<SceneImageModelChoice>> ListImageEditorModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageModelChoice>>([]);

        private static ResolvedImageEditorModel EditorModel(DreamGenClone.Domain.ModelManager.ImageProtocol protocol) => new(
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
            ImageProtocol: protocol,
            RegisteredModelId: "22222222-2222-2222-2222-222222222222",
            GraphKind: DreamGenClone.Domain.ModelManager.ImageEditorGraphKind.MergedCheckpoint);
    }

    private sealed class FixedEndpointReadiness : IImageEditorEndpointReadiness
    {
        private readonly bool _warm;

        public FixedEndpointReadiness(bool warm) => _warm = warm;

        public Task<bool> IsWarmAsync(ResolvedImageEditorModel model, CancellationToken cancellationToken = default)
            => Task.FromResult(_warm);
    }

    private sealed class StubDurableSettings : ISceneBeatAnalyzerResolver
    {
        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedSceneBeatAnalyzer(
                "function-default", "model-1", "provider-1",
                new ResolvedModel(
                    "https://vision.internal", "/v1/chat/completions", 30, "enc-key", "qwen-vl", 0.2, 0.8, 4096, "Vision", false),
                StructuredOutputMode.StrictJsonSchema, 1024, 512, 1, 30, 500, [5, 15], 7, 100));
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
