using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageEditCompilationJobTests
{
    [Fact]
    public async Task ReadyCompilation_CreatesRevisionZeroAndIsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync(ReadyResponse);
        var (editSession, attempt) = await fixture.CreateAndEnqueueAsync("change the shirt to red");
        var queued = Assert.Single(fixture.Queue.Enqueued);
        Assert.Equal(BackgroundJobTypes.SceneImageEditPromptCompilation, queued.JobType);
        Assert.Equal(attempt.Id, queued.DedupeKey);
        Assert.DoesNotContain("enc:secret", attempt.ResolvedModelSnapshotJson, StringComparison.Ordinal);

        await fixture.Handler.HandleAsync(queued.Envelope, CancellationToken.None);

        var completed = await fixture.EditRepository.GetAttemptAsync(attempt.Id);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Ready, completed!.Status);
        var revision = Assert.Single(await fixture.EditRepository.ListRevisionsAsync(attempt.Id));
        Assert.Equal(0, revision.Ordinal);
        Assert.Equal(SceneImageEditPromptRevisionKind.CompilerOutput, revision.RevisionKind);
        Assert.Equal("Change the foreground woman's blue shirt to red; preserve everything else.", revision.Prompt);
        Assert.Equal(SceneImageEditSessionStatus.Ready, (await fixture.EditRepository.GetSessionAsync(editSession.Id))!.Status);
        Assert.Equal(1, fixture.Completion.GenerateCalls);
        Assert.Equal(1, fixture.Completion.HealthCalls);

        await fixture.Handler.HandleAsync(queued.Envelope, CancellationToken.None);

        Assert.Equal(1, fixture.Completion.GenerateCalls);
        Assert.Single(await fixture.EditRepository.ListRevisionsAsync(attempt.Id));
        Assert.All(fixture.Debug.Events, record =>
        {
            Assert.DoesNotContain("base64", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("vision.internal", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Clarification_CreatesNewAttemptWithStructuredHistory()
    {
        await using var fixture = await Fixture.CreateAsync(ClarificationResponse);
        var (editSession, firstAttempt) = await fixture.CreateAndEnqueueAsync("change her shirt");
        await fixture.Handler.HandleAsync(Assert.Single(fixture.Queue.Enqueued).Envelope, CancellationToken.None);

        Assert.Equal(SceneImageEditCompilationAttemptStatus.ClarificationRequired, (await fixture.EditRepository.GetAttemptAsync(firstAttempt.Id))!.Status);
        Assert.Empty(await fixture.EditRepository.ListRevisionsAsync(firstAttempt.Id));
        fixture.Queue.Enqueued.Clear();

        var secondAttempt = await fixture.Service.EnqueueCompilationAsync(new EnqueueSceneImageEditCompilationRequest
        {
            EditSessionId = editSession.Id,
            RawIntent = "change her shirt",
            ClarificationHistory = ["The woman in the foreground on the left."]
        });

        Assert.Equal(1, secondAttempt.Ordinal);
        Assert.Equal("[\"The woman in the foreground on the left.\"]", secondAttempt.ClarificationContextJson);
        Assert.Equal(SceneImageEditSessionStatus.Active, (await fixture.EditRepository.GetSessionAsync(editSession.Id))!.Status);
        Assert.Equal(secondAttempt.Id, Assert.Single(fixture.Queue.Enqueued).DedupeKey);
    }

    [Fact]
    public async Task UserRevision_AppendsToLatestReadyAttempt()
    {
        await using var fixture = await Fixture.CreateAsync(ReadyResponse);
        var (editSession, attempt) = await fixture.CreateAndEnqueueAsync("change the shirt to red");
        await fixture.Handler.HandleAsync(Assert.Single(fixture.Queue.Enqueued).Envelope, CancellationToken.None);

        var revision = await fixture.Service.AppendPromptRevisionAsync(new AppendSceneImageEditPromptRevisionRequest
        {
            EditSessionId = editSession.Id,
            CompilationAttemptId = attempt.Id,
            Prompt = "  Change only the foreground woman's shirt to crimson; preserve everything else.  "
        });

        Assert.Equal(1, revision.Ordinal);
        Assert.Equal(SceneImageEditPromptRevisionKind.UserEdited, revision.RevisionKind);
        Assert.Equal("Change only the foreground woman's shirt to crimson; preserve everything else.", revision.Prompt);
        Assert.Equal(64, revision.PromptSha256.Length);
    }

    [Fact]
    public async Task MalformedCompilerOutput_PersistsFailedAttemptWithoutRetry()
    {
        await using var fixture = await Fixture.CreateAsync("not-json");
        var (editSession, attempt) = await fixture.CreateAndEnqueueAsync("change the shirt");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Handler.HandleAsync(Assert.Single(fixture.Queue.Enqueued).Envelope, CancellationToken.None));

        var failed = await fixture.EditRepository.GetAttemptAsync(attempt.Id);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Failed, failed!.Status);
        Assert.NotNull(failed.CompletedUtc);
        Assert.Equal("not-json", failed.RawModelResponse);
        Assert.Equal(SceneImageEditSessionStatus.Failed, (await fixture.EditRepository.GetSessionAsync(editSession.Id))!.Status);
        Assert.Equal(1, fixture.Completion.GenerateCalls);
        Assert.Contains(fixture.Debug.Events, record => record.EventKind == "SceneImageEditCompilationFailed");
    }

    [Fact]
    public async Task EditingWorker_RejectsRevisionMadeStaleAfterEnqueueBeforeQwenCall()
    {
        await using var fixture = await Fixture.CreateAsync(ReadyResponse);
        var (editSession, firstAttempt) = await fixture.CreateAndEnqueueAsync("change the shirt to red");
        await fixture.Handler.HandleAsync(Assert.Single(fixture.Queue.Enqueued).Envelope, CancellationToken.None);
        var revision = Assert.Single(await fixture.EditRepository.ListRevisionsAsync(firstAttempt.Id));
        var editedImage = new SceneImageRecord
        {
            SessionId = fixture.Source.SessionId,
            InteractionId = fixture.Source.InteractionId,
            PromptRecordId = fixture.Source.PromptRecordId,
            PromptSnapshot = revision.Prompt,
            Status = SceneImageStatus.Pending,
            Operation = SceneImageOperation.Edit,
            SourceImageId = fixture.Source.Id,
            EditSessionId = editSession.Id,
            EditCompilationAttemptId = firstAttempt.Id,
            EditPromptRevisionId = revision.Id,
            EditIntentSnapshot = firstAttempt.RawIntent,
            EditCompilerProvenanceJson = JsonSerializer.Serialize(new
            {
                sourceImageSha256 = editSession.SourceImageSha256,
                promptSha256 = revision.PromptSha256
            })
        };
        await fixture.ImageRepository.InsertImageAsync(editedImage);
        fixture.Queue.Enqueued.Clear();
        var newerAttempt = await fixture.Service.EnqueueCompilationAsync(new EnqueueSceneImageEditCompilationRequest
        {
            EditSessionId = editSession.Id,
            RawIntent = "change the shirt to green"
        });
        var editor = new RecordingImageEditor();
        var editingHandler = new SceneImageEditingJobHandler(
            fixture.ImageRepository,
            fixture.EditRepository,
            fixture.Storage,
            new FailingImageEditorResolver(),
            editor,
            NullLogger<SceneImageEditingJobHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => editingHandler.HandleAsync(new BackgroundJobEnvelope
        {
            JobType = BackgroundJobTypes.SceneImageEditing,
            PayloadJson = JsonSerializer.Serialize(new SceneImageEditingJobPayload
            {
                SessionId = editedImage.SessionId,
                InteractionId = editedImage.InteractionId,
                ImageRecordId = editedImage.Id
            })
        }, CancellationToken.None));

        Assert.Equal(0, editor.Calls);
        Assert.Equal(SceneImageStatus.Failed, (await fixture.ImageRepository.GetImageAsync(editedImage.Id))!.Status);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Pending, (await fixture.EditRepository.GetAttemptAsync(newerAttempt.Id))!.Status);
        Assert.Equal(SceneImageEditSessionStatus.Active, (await fixture.EditRepository.GetSessionAsync(editSession.Id))!.Status);
    }

    private const string ReadyResponse = """
        {"schemaVersion":"scene-image-edit-compiler-v1","status":"ready","sourceSummary":"A woman in a blue shirt stands in the foreground.","targets":[{"key":"foreground-woman","visibleLocator":"woman in blue shirt in the foreground","region":null}],"requestedChanges":["Change the blue shirt to red."],"preserve":["Everything else."],"clarificationQuestion":null,"invalidReason":null,"compiledPrompt":"Change the foreground woman's blue shirt to red; preserve everything else."}
        """;

    private const string ClarificationResponse = """
        {"schemaVersion":"scene-image-edit-compiler-v1","status":"clarification_required","sourceSummary":"Two women are visible.","targets":[],"requestedChanges":[],"preserve":[],"clarificationQuestion":"Which visible woman should be edited?","invalidReason":null,"compiledPrompt":null}
        """;

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _dbPath;
        private readonly string _root;

        private Fixture(
            string dbPath,
            string root,
            SceneImageEditCompilationService service,
            SceneImageEditCompilationJobHandler handler,
            SceneImageEditRepository editRepository,
            SceneImageRepository imageRepository,
            SceneImageStorageService storage,
            CapturingQueue queue,
            StubCompletion completion,
            RecordingDebugSink debug,
            SceneImageRecord source)
        {
            _dbPath = dbPath;
            _root = root;
            Service = service;
            Handler = handler;
            EditRepository = editRepository;
            ImageRepository = imageRepository;
            Storage = storage;
            Queue = queue;
            Completion = completion;
            Debug = debug;
            Source = source;
        }

        public SceneImageEditCompilationService Service { get; }
        public SceneImageEditCompilationJobHandler Handler { get; }
        public SceneImageEditRepository EditRepository { get; }
        public SceneImageRepository ImageRepository { get; }
        public SceneImageStorageService Storage { get; }
        public CapturingQueue Queue { get; }
        public StubCompletion Completion { get; }
        public RecordingDebugSink Debug { get; }
        public SceneImageRecord Source { get; }

        public static async Task<Fixture> CreateAsync(string response)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"scene-image-edit-job-{Guid.NewGuid():N}.db");
            var root = Path.Combine(Path.GetTempPath(), $"scene-image-edit-job-files-{Guid.NewGuid():N}");
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False", SceneImageRoot = root });
            await new SqlitePersistence(
                options,
                Options.Create(new LmStudioOptions()),
                Options.Create(new StoryAnalysisOptions()),
                Options.Create(new ScenarioAdaptationOptions()),
                NullLogger<SqlitePersistence>.Instance).InitializeAsync();
            var imageRepository = new SceneImageRepository(options);
            var editRepository = new SceneImageEditRepository(options);
            var storage = new SceneImageStorageService(options, NullLogger<SceneImageStorageService>.Instance);
            var png = CreatePngHeader(4, 3);
            await using var imageStream = new MemoryStream(png);
            var relativePath = await storage.SaveAsync("s1", "source.png", imageStream);
            var source = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = "prompt",
                PromptSnapshot = "source prompt",
                Status = SceneImageStatus.Complete,
                FileRelativePath = relativePath
            };
            await imageRepository.InsertImageAsync(source);

            var resolver = new StubResolver(CreateResolvedModel());
            var queue = new CapturingQueue();
            var compiler = new QwenSceneImageEditPromptCompiler();
            var completion = new StubCompletion(response);
            var debug = new RecordingDebugSink();
            var service = new SceneImageEditCompilationService(imageRepository, editRepository, storage, resolver, compiler, queue);
            var handler = new SceneImageEditCompilationJobHandler(editRepository, imageRepository, storage, resolver, completion, compiler, debug);
            return new Fixture(dbPath, root, service, handler, editRepository, imageRepository, storage, queue, completion, debug, source);
        }

        public async Task<(SceneImageEditSession Session, SceneImageEditCompilationAttempt Attempt)> CreateAndEnqueueAsync(string intent)
        {
            var session = await Service.CreateSessionAsync(new CreateSceneImageEditSessionRequest
            {
                SessionId = Source.SessionId,
                InteractionId = Source.InteractionId,
                SourceImageId = Source.Id
            });
            var attempt = await Service.EnqueueCompilationAsync(new EnqueueSceneImageEditCompilationRequest
            {
                EditSessionId = session.Id,
                RawIntent = intent
            });
            return (session, attempt);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); } catch (IOException) { }
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubResolver(ResolvedMultimodalModel model) : IMultimodalModelResolutionService
    {
        public Task<ResolvedMultimodalModel> ResolveAsync(AppFunction function, CancellationToken cancellationToken = default)
        {
            Assert.Equal(AppFunction.RolePlaySceneImageEditPromptCompiler, function);
            return Task.FromResult(model);
        }
    }

    private sealed class StubCompletion(string response) : IMultimodalCompletionClient
    {
        public int GenerateCalls { get; private set; }
        public int HealthCalls { get; private set; }

        public Task<MultimodalCompletionResult> GenerateAsync(ResolvedMultimodalModel model, MultimodalCompletionRequest request, CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            Assert.Equal("image/png", request.Image.MediaType);
            Assert.Equal(4, request.Image.Width);
            Assert.Equal(3, request.Image.Height);
            Assert.Equal(24, request.Image.Bytes.Length);
            return Task.FromResult(new MultimodalCompletionResult(response, model.ModelIdentifier, TimeSpan.FromMilliseconds(10)));
        }

        public Task CheckHealthAsync(ResolvedMultimodalModel model, CancellationToken cancellationToken = default)
        {
            HealthCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingQueue : IBackgroundJobQueue
    {
        public List<(string JobType, string PayloadJson, string? DedupeKey, BackgroundJobEnvelope Envelope)> Enqueued { get; } = [];

        public bool Enqueue(string jobType, string payloadJson, string? dedupeKey = null)
        {
            Enqueued.Add((jobType, payloadJson, dedupeKey, new BackgroundJobEnvelope { JobType = jobType, PayloadJson = payloadJson, DedupeKey = dedupeKey }));
            return true;
        }

        public ValueTask<BackgroundJobEnvelope> DequeueAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void MarkProcessing(string jobId) { }
        public void MarkCompleted(string jobId) { }
        public void MarkFailed(string jobId, string errorMessage) { }
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

    private sealed class FailingImageEditorResolver : IImageEditorModelResolver
    {
        public Task<ResolvedImageEditorModel> ResolveAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The stale revision must fail before editor model resolution.");
    }

    private sealed class RecordingImageEditor : IImageEditingClient
    {
        public int Calls { get; private set; }

        public Task<byte[]> EditAsync(ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    private static ResolvedMultimodalModel CreateResolvedModel() => new(
        "provider-1",
        "model-1",
        "https://vision.internal",
        "/v1/chat/completions",
        "/v1/models",
        "{\"model\":\"qwen-vl\"}",
        30,
        420,
        30,
        "vision-api-key",
        "enc:secret",
        "qwen-vl",
        "Vision",
        ImageContentPolicy.AdultAllowed,
        ModelLifecycleStrategy.ScheduledSinglePod,
        1,
        1024,
        4096,
        64,
        new HashSet<string>(["image/png", "image/jpeg", "image/webp"], StringComparer.OrdinalIgnoreCase),
        1024,
        1,
        4,
        0.2,
        0.8,
        512,
        "vllm-revision",
        "model-revision");

    private static byte[] CreatePngHeader(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        return bytes;
    }
}