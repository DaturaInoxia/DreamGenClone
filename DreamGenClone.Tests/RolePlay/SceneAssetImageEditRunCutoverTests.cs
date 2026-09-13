using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-124 B124-012: the asset studio's edit run must be queued through the ONE shared editing pipeline.
/// This service used to enqueue its own <c>scene-asset-image-editing</c> job with its own handler, which
/// is how the asset edit path stayed broken for two weeks — nothing covered it.
/// </summary>
public sealed class SceneAssetImageEditRunCutoverTests
{
    [Fact]
    public async Task EnqueueEditAsync_QueuesTheSharedEditingJobAndNoLegacyOne()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new RecordingMediaEditCompilationService();
        var queue = new CapturingDurableQueue();
        var service = fixture.BuildService(recorder, queue);

        var image = await service.EnqueueEditAsync(fixture.BuildRunRequest());

        // The run reaches the shared pipeline with everything the subject writer needs.
        var run = Assert.Single(recorder.Runs);
        Assert.Equal(MediaEditSubjectKind.AssetImage, run.SubjectKind);
        Assert.Equal(image.Id, run.ImageId);
        Assert.Equal(Fixture.EditorModelId, run.EditorModelId);

        // And the retired job type is never produced again: there is exactly one run path.
        Assert.Empty(queue.Enqueued);

        // The subject-specific part — the queued image row and its provenance — is still written here.
        var persisted = await fixture.Assets.GetImageAsync(image.Id);
        Assert.NotNull(persisted);
        Assert.Equal(SceneAssetKind.Edited, persisted!.Kind);
        Assert.Equal(SceneAssetStatus.Pending, persisted.Status);
        Assert.Equal("Change the shirt to red.", persisted.Prompt);
        Assert.Equal(fixture.SourceImageId, persisted.SourceImageId);
        Assert.Contains(fixture.AttemptId, persisted.SourceProvenanceJson!, StringComparison.Ordinal);
        Assert.Contains(fixture.RevisionId, persisted.SourceProvenanceJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnqueueEditAsync_RefusesAStaleSourceChecksum()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new RecordingMediaEditCompilationService();
        var service = fixture.BuildService(recorder, new CapturingDurableQueue());

        var request = fixture.BuildRunRequest();
        request.SourceImageSha256 = new string('B', 64);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueEditAsync(request));

        Assert.Contains("stale", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(recorder.Runs);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string AssetId = "asset-1";

        public const string EditorModelId = "22222222-2222-2222-2222-222222222222";

        private readonly string _dbPath;
        private readonly string _root;
        private readonly OptionsWrapper<PersistenceOptions> _options;

        private Fixture(
            string dbPath,
            string root,
            OptionsWrapper<PersistenceOptions> options,
            SceneAssetRepository assets,
            SceneAssetImageEditRepository edits,
            SceneAssetStorageService storage)
        {
            _dbPath = dbPath;
            _root = root;
            _options = options;
            Assets = assets;
            Edits = edits;
            Storage = storage;
        }

        public SceneAssetRepository Assets { get; }
        public SceneAssetImageEditRepository Edits { get; }
        public SceneAssetStorageService Storage { get; }
        public string SourceImageId { get; private set; } = string.Empty;
        public string SourceSha256 { get; private set; } = string.Empty;
        public string EditSessionId { get; private set; } = string.Empty;
        public string AttemptId { get; private set; } = string.Empty;
        public string RevisionId { get; private set; } = string.Empty;
        public string PromptSha256 { get; private set; } = string.Empty;

        public static async Task<Fixture> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"asset-edit-run-{Guid.NewGuid():N}.db");
            var root = Path.Combine(Path.GetTempPath(), $"asset-edit-run-files-{Guid.NewGuid():N}");
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
            var fixture = new Fixture(dbPath, root, (OptionsWrapper<PersistenceOptions>)options, assets, edits, storage);

            await assets.UpsertAsync(new SceneAsset
            {
                Id = AssetId,
                Name = "Asset one",
                Type = SceneAssetType.CharacterFace,
                Status = SceneAssetStatus.Complete
            });

            await using (var sourceContent = new MemoryStream(CreatePng(4, 3)))
            {
                var stored = await storage.SaveAsync("asset-source.png", sourceContent);
                var source = new SceneAssetImage
                {
                    AssetId = AssetId,
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
                AssetId = AssetId,
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
            fixture.AttemptId = attempt.Id;

            const string prompt = "Change the shirt to red.";
            var revision = new SceneAssetImageEditPromptRevision
            {
                CompilationAttemptId = attempt.Id,
                Ordinal = 0,
                Prompt = prompt,
                RevisionKind = SceneImageEditPromptRevisionKind.CompilerOutput,
                PromptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)))
            };
            await edits.CreateRevisionAsync(revision);
            fixture.RevisionId = revision.Id;
            fixture.PromptSha256 = revision.PromptSha256!;

            return fixture;
        }

        public EnqueueSceneAssetImageEditRequest BuildRunRequest() => new()
        {
            AssetId = AssetId,
            SourceImageId = SourceImageId,
            EditSessionId = EditSessionId,
            CompilationAttemptId = AttemptId,
            PromptRevisionId = RevisionId,
            SourceImageSha256 = SourceSha256,
            PromptSha256 = PromptSha256,
            EditorModelId = EditorModelId
        };

        public SceneAssetImageEditCompilationService BuildService(
            IMediaEditCompilationService mediaEdits, IDurableBackgroundJobQueue queue)
            => new(
                Assets,
                Edits,
                Storage,
                new ThrowingMultimodalResolver(),
                new ThrowingPromptCompiler(),
                queue,
                new StubDurableSettingsResolver(),
                TimeProvider.System,
                mediaEdits);

        public ValueTask DisposeAsync()
        {
            // No SqliteConnection.ClearAllPools(): process-wide and destabilises parallel tests.
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); } catch (IOException) { }
            }
            return ValueTask.CompletedTask;
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

    /// <summary>Records the shared run request: the only thing the cut-over service must produce.</summary>
    private sealed class RecordingMediaEditCompilationService : IMediaEditCompilationService
    {
        public List<MediaEditRunRequest> Runs { get; } = [];

        public List<MediaEditOperationRunRequest> OperationRuns { get; } = [];

        public Task EnqueueRunAsync(MediaEditRunRequest request, CancellationToken cancellationToken = default)
        {
            Runs.Add(request);
            return Task.CompletedTask;
        }

        public Task EnqueueOperationRunAsync(
            MediaEditOperationRunRequest request, CancellationToken cancellationToken = default)
        {
            OperationRuns.Add(request);
            return Task.CompletedTask;
        }

        public Task<MediaEditSession> CreateSessionAsync(CreateMediaEditSessionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MediaEditCompilationAttempt> EnqueueCompilationAsync(EnqueueMediaEditCompilationRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnqueueDescriptionAsync(string editSessionId, bool force = false, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MediaEditPromptRevision> AppendPromptRevisionAsync(AppendMediaEditPromptRevisionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MediaEditSession?> GetSessionAsync(string editSessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MediaEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MediaEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CapturingDurableQueue : IDurableBackgroundJobQueue
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
        public Task WaitForWorkAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Neither dependency is reachable from the run enqueue; a call would be a real defect.</summary>
    private sealed class ThrowingMultimodalResolver : IMultimodalModelResolutionService
    {
        public Task<ResolvedMultimodalModel> ResolveAsync(AppFunction function, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingPromptCompiler : ISceneImageEditPromptCompiler
    {
        public SceneImageEditCompilerMessages BuildMessages(SceneImageEditCompilerContext context)
            => throw new NotSupportedException();

        public SceneImageEditCompilationResult Parse(string rawResponse, int imageWidth, int imageHeight)
            => throw new NotSupportedException();
    }

    private sealed class StubDurableSettingsResolver : ISceneBeatAnalyzerResolver
    {
        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var model = new ResolvedModel(
                "https://example.test",
                "/chat",
                30,
                null,
                "model",
                0.2,
                0.8,
                1024,
                "provider",
                false);
            return Task.FromResult(new ResolvedSceneBeatAnalyzer(
                "function",
                "model",
                "provider",
                model,
                StructuredOutputMode.StrictJsonSchema,
                4096,
                1024,
                1,
                120,
                250,
                [5, 30],
                30,
                8));
        }
    }
}
