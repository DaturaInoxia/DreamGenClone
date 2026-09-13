using System.Text;
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
/// The ONE compile handler and the ONE description handler (B-124 B124-012). The asset path used to
/// have no instrumentation at all, and its payload never bound because the queue writes camelCase
/// while the handler read with default options — both are pinned here.
/// </summary>
public sealed class MediaEditJobHandlerTests
{
    private const string ReadyResponse = """
        {"schemaVersion":"scene-image-edit-compiler-v1","status":"ready","sourceSummary":"A woman in a blue shirt stands in the foreground.","targets":[{"key":"foreground-woman","visibleLocator":"woman in blue shirt in the foreground","region":null}],"requestedChanges":["Change the blue shirt to red."],"preserve":["Everything else."],"clarificationQuestion":null,"invalidReason":null,"compiledPrompt":"Change the foreground woman's blue shirt to red; preserve everything else."}
        """;

    private const string ClarificationResponse = """
        {"schemaVersion":"scene-image-edit-compiler-v1","status":"clarification_required","sourceSummary":"Two women are visible.","targets":[],"requestedChanges":[],"preserve":[],"clarificationQuestion":"Which visible woman should be edited?","invalidReason":null,"compiledPrompt":null}
        """;

    [Fact]
    public async Task ReadyCompilation_CreatesRevisionZeroAndIsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync(ReadyResponse);
        var (session, attempt) = await fixture.CreateAndEnqueueSceneAsync("change the shirt to red");
        var job = fixture.Queue.Single();

        Assert.Equal(BackgroundJobTypes.MediaEditPromptCompilation, job.JobType);
        Assert.DoesNotContain("enc:secret", attempt.ResolvedModelSnapshotJson, StringComparison.Ordinal);

        await fixture.CompileHandler.HandleAsync(job);

        var completed = await fixture.EditRepository.GetAttemptAsync(attempt.Id);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Ready, completed!.Status);
        var revision = Assert.Single(await fixture.EditRepository.ListRevisionsAsync(attempt.Id));
        Assert.Equal(0, revision.Ordinal);
        Assert.Equal(SceneImageEditPromptRevisionKind.CompilerOutput, revision.RevisionKind);
        Assert.Equal("Change the foreground woman's blue shirt to red; preserve everything else.", revision.Prompt);
        Assert.Equal(MediaEditSessionStatus.Ready, (await fixture.EditRepository.GetSessionAsync(session.Id))!.Status);
        Assert.Equal(1, fixture.Completion.GenerateCalls);
        Assert.Contains(fixture.Debug.Events, record => record.EventKind == "MediaEditCompilationSent");
        Assert.Contains(fixture.Debug.Events, record => record.EventKind == "MediaEditCompilationCompleted");

        await fixture.CompileHandler.HandleAsync(job);

        Assert.Equal(1, fixture.Completion.GenerateCalls);
        Assert.Single(await fixture.EditRepository.ListRevisionsAsync(attempt.Id));
        Assert.All(fixture.Debug.Events, record =>
        {
            Assert.DoesNotContain("base64", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("enc:secret", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Clarification_LeavesTheAttemptWithoutARevision()
    {
        await using var fixture = await Fixture.CreateAsync(ClarificationResponse);
        var (_, attempt) = await fixture.CreateAndEnqueueSceneAsync("change her shirt");

        await fixture.CompileHandler.HandleAsync(fixture.Queue.Single());

        var clarification = await fixture.EditRepository.GetAttemptAsync(attempt.Id);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.ClarificationRequired, clarification!.Status);
        Assert.Empty(await fixture.EditRepository.ListRevisionsAsync(attempt.Id));
    }

    [Fact]
    public async Task MalformedCompilerOutput_FailsTheAttemptAndSessionWithoutRetry()
    {
        await using var fixture = await Fixture.CreateAsync("not-json");
        var (session, attempt) = await fixture.CreateAndEnqueueSceneAsync("change the shirt");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.CompileHandler.HandleAsync(fixture.Queue.Single()));

        var failed = await fixture.EditRepository.GetAttemptAsync(attempt.Id);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Failed, failed!.Status);
        Assert.NotNull(failed.CompletedUtc);
        Assert.Equal("not-json", failed.RawModelResponse);
        Assert.Equal(MediaEditSessionStatus.Failed, (await fixture.EditRepository.GetSessionAsync(session.Id))!.Status);
        Assert.Equal(1, fixture.Completion.GenerateCalls);
        Assert.Contains(fixture.Debug.Events, record => record.EventKind == "MediaEditCompilationFailed");
    }

    [Fact]
    public async Task AssetCompilation_RunsThroughTheSameHandlerAndLogsInsteadOfTheRpDebugStream()
    {
        await using var fixture = await Fixture.CreateAsync(ReadyResponse);
        var (session, attempt) = await fixture.CreateAndEnqueueAssetAsync("remove the shirt");

        await fixture.CompileHandler.HandleAsync(fixture.Queue.Single());

        Assert.Equal(MediaEditSubjectKind.AssetImage, session.SubjectKind);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Ready, (await fixture.EditRepository.GetAttemptAsync(attempt.Id))!.Status);
        Assert.Single(await fixture.EditRepository.ListRevisionsAsync(attempt.Id));
        // The RP debug stream belongs to role-play sessions; asset work is not written into it.
        Assert.Empty(fixture.Debug.Events);
    }

    [Fact]
    public async Task Description_PersistsTheParsedDescriptionAndSkipsWhenAlreadyPresent()
    {
        await using var fixture = await Fixture.CreateAsync(
            """{"description":"A person standing in a blue shirt."}""");
        var session = await fixture.CreateSceneSessionAsync();
        await fixture.Service.EnqueueDescriptionAsync(session.Id);
        var job = fixture.Queue.Single();

        await fixture.DescriptionHandler.HandleAsync(job);

        var loaded = await fixture.EditRepository.GetSessionAsync(session.Id);
        Assert.Equal("A person standing in a blue shirt.", loaded!.DescriptionText);
        Assert.Equal(1, fixture.Completion.GenerateCalls);
        Assert.Contains(fixture.Debug.Events, record => record.EventKind == "MediaEditDescriptionCompleted");

        // Re-running a job whose description already exists is a no-op.
        await fixture.DescriptionHandler.HandleAsync(job);
        Assert.Equal(1, fixture.Completion.GenerateCalls);
    }

    [Fact]
    public async Task Description_RejectsMalformedOutputAndReportsFailure()
    {
        await using var fixture = await Fixture.CreateAsync("""{"wrong":"shape"}""");
        var session = await fixture.CreateSceneSessionAsync();
        await fixture.Service.EnqueueDescriptionAsync(session.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.DescriptionHandler.HandleAsync(fixture.Queue.Single()));

        Assert.Null((await fixture.EditRepository.GetSessionAsync(session.Id))!.DescriptionText);
        Assert.Contains(fixture.Debug.Events, record => record.EventKind == "MediaEditDescriptionFailed");
    }

    [Fact]
    public async Task QueuedPayloadJson_BindsInBothHandlers()
    {
        // Regression guard: the queue serialises with Web (camelCase) options. A handler that
        // deserialises without those options silently binds nothing and rejects a valid payload —
        // which is exactly how every asset edit failed between 2026-08-30 and 2026-09-12.
        await using var fixture = await Fixture.CreateAsync(ReadyResponse);
        var (_, attempt) = await fixture.CreateAndEnqueueSceneAsync("change the shirt to red");
        var compilePayload = fixture.Queue.Single().PayloadJson;

        Assert.Contains("\"attemptId\"", compilePayload, StringComparison.Ordinal);
        Assert.NotNull(JsonSerializer.Deserialize<MediaEditCompilationJobPayload>(
            compilePayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AttemptId);
        Assert.NotNull(JsonSerializer.Deserialize<MediaEditCompilationJobPayload>(
            compilePayload, JsonSerializerOptions.Web).AttemptId);

        var session = await fixture.CreateAssetSessionAsync();
        await fixture.Service.EnqueueDescriptionAsync(session.Id);
        var descriptionPayload = fixture.Queue.Jobs[^1].PayloadJson;
        Assert.Contains("\"editSessionId\"", descriptionPayload, StringComparison.Ordinal);
        Assert.NotNull(JsonSerializer.Deserialize<MediaEditDescriptionJobPayload>(
            descriptionPayload, JsonSerializerOptions.Web)!.EditSessionId);

        Assert.Equal(attempt.Id, JsonSerializer.Deserialize<MediaEditCompilationJobPayload>(
            compilePayload, JsonSerializerOptions.Web)!.AttemptId);
    }

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
            MediaEditCompilationJobHandler compileHandler,
            MediaEditDescriptionJobHandler descriptionHandler,
            StubCompletion completion,
            RecordingDebugSink debug,
            SceneImageRecord sceneSource,
            SceneAssetImage assetSource)
        {
            _dbPath = dbPath;
            _root = root;
            Service = service;
            EditRepository = editRepository;
            Queue = queue;
            CompileHandler = compileHandler;
            DescriptionHandler = descriptionHandler;
            Completion = completion;
            Debug = debug;
            SceneSource = sceneSource;
            AssetSource = assetSource;
        }

        public MediaEditCompilationService Service { get; }
        public MediaEditRepository EditRepository { get; }
        public CapturingDurableQueue Queue { get; }
        public MediaEditCompilationJobHandler CompileHandler { get; }
        public MediaEditDescriptionJobHandler DescriptionHandler { get; }
        public StubCompletion Completion { get; }
        public RecordingDebugSink Debug { get; }
        public SceneImageRecord SceneSource { get; }
        public SceneAssetImage AssetSource { get; }

        public static async Task<Fixture> CreateAsync(string completionResponse)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"media-edit-handler-{Guid.NewGuid():N}.db");
            var root = Path.Combine(Path.GetTempPath(), $"media-edit-handler-files-{Guid.NewGuid():N}");
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
            var completion = new StubCompletion(completionResponse);
            var debug = new RecordingDebugSink();
            var modelResolver = new StubMultimodalResolver();
            var compiler = new QwenSceneImageEditPromptCompiler();
            var service = new MediaEditCompilationService(
                editRepository, sources, modelResolver, compiler, queue, new StubDurableSettings(), TimeProvider.System,
                new StubEditorModels(), new StubEndpointReadiness());
            var compileHandler = new MediaEditCompilationJobHandler(
                editRepository, sources, modelResolver, completion, compiler,
                NullLogger<MediaEditCompilationJobHandler>.Instance, debug);
            var descriptionHandler = new MediaEditDescriptionJobHandler(
                editRepository, sources, modelResolver, completion,
                NullLogger<MediaEditDescriptionJobHandler>.Instance, debug);

            return new Fixture(dbPath, root, service, editRepository, queue, compileHandler, descriptionHandler,
                completion, debug, sceneSource, assetSource);
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

        public async Task<(MediaEditSession Session, MediaEditCompilationAttempt Attempt)> CreateAndEnqueueSceneAsync(string intent)
        {
            var session = await CreateSceneSessionAsync();
            var attempt = await Service.EnqueueCompilationAsync(new EnqueueMediaEditCompilationRequest
            {
                EditSessionId = session.Id,
                RawIntent = intent
            });
            return (session, attempt);
        }

        public async Task<(MediaEditSession Session, MediaEditCompilationAttempt Attempt)> CreateAndEnqueueAssetAsync(string intent)
        {
            var session = await CreateAssetSessionAsync();
            var attempt = await Service.EnqueueCompilationAsync(new EnqueueMediaEditCompilationRequest
            {
                EditSessionId = session.Id,
                RawIntent = intent
            });
            return (session, attempt);
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

    private sealed class CapturingDurableQueue : IDurableBackgroundJobQueue
    {
        private readonly List<DurableBackgroundJob> _jobs = [];

        public IReadOnlyList<DurableBackgroundJob> Jobs => _jobs;

        public DurableBackgroundJob Single() => Assert.Single(_jobs);

        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        {
            _jobs.Add(job);
            return Task.FromResult(true);
        }

        public Task<bool> TryActivateAsync(string jobId, DateTime activatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WaitForWorkAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubCompletion(string response) : IMultimodalCompletionClient
    {
        public int GenerateCalls { get; private set; }

        public Task<MultimodalCompletionResult> GenerateAsync(
            ResolvedMultimodalModel model, MultimodalCompletionRequest request, CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            Assert.Equal(4, request.Image.Width);
            Assert.Equal(3, request.Image.Height);
            return Task.FromResult(new MultimodalCompletionResult(response, model.ModelIdentifier, TimeSpan.FromMilliseconds(5)));
        }

        public Task CheckHealthAsync(ResolvedMultimodalModel model, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingDebugSink : IRolePlayDebugEventSink
    {
        public List<RolePlayDebugEventRecord> Events { get; } = [];

        public Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default)
        {
            Events.Add(record);
            return Task.CompletedTask;
        }
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

    /// <summary>Only serverless admission probes warmth; a call here for a local model would be a defect.</summary>
    private sealed class StubEndpointReadiness : IImageEditorEndpointReadiness
    {
        public Task<bool> IsWarmAsync(ResolvedImageEditorModel model, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
