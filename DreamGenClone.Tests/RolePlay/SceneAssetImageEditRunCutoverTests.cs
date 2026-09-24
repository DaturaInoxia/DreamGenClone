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

    /// <summary>
    /// The Asset Manager editor's identity tab is the same run as the scene one, on the asset store: the
    /// bound approved faces are the run's references and the instruction is authored from the bindings, so
    /// there is no compiler artifact, no edit session and no revision involved.
    /// </summary>
    [Fact]
    public async Task EnqueueIdentityEditAsync_CreatesAChildImageCarryingTheBoundFaceReferences()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new RecordingMediaEditCompilationService();
        var queue = new CapturingDurableQueue();
        var production = new StubIdentityProductionService();
        var service = fixture.BuildService(recorder, queue, production);

        var image = await service.EnqueueIdentityEditAsync(fixture.BuildIdentityRequest());

        // The chosen face is resolved through the ONE identity-reference path; this service never re-derives
        // what an approved pack face is.
        var selection = Assert.Single(production.Selections);
        Assert.Equal("becky-1", selection.CharacterId);
        Assert.Equal(StubIdentityProductionService.FaceAssetId, selection.ReferenceAssetId);

        // The run reaches the shared pipeline carrying the model the editor form chose.
        var run = Assert.Single(recorder.Runs);
        Assert.Equal(MediaEditSubjectKind.AssetImage, run.SubjectKind);
        Assert.Equal(image.Id, run.ImageId);
        Assert.Equal(Fixture.EditorModelId, run.EditorModelId);

        // And no second job path is produced: an identity run is the same edit job as everything else.
        Assert.Empty(queue.Enqueued);

        // The child row is an edited image of the subject, in the subject's own draft group, whose instruction
        // is authored from the bindings and whose provenance records the faces it must apply.
        var persisted = await fixture.Assets.GetImageAsync(image.Id);
        Assert.NotNull(persisted);
        Assert.Equal(SceneAssetKind.Edited, persisted!.Kind);
        Assert.Equal(SceneAssetStatus.Pending, persisted.Status);
        Assert.Equal(fixture.SourceImageId, persisted.SourceImageId);
        Assert.Contains("Apply the face of the person shown in Picture 2 to the man on the left at left third of the frame", persisted.Prompt!, StringComparison.Ordinal);

        var bindings = MediaEditIdentityProvenance.TryRead(persisted.SourceProvenanceJson);
        var binding = Assert.Single(bindings!);
        Assert.Equal(1, binding.Ordinal);
        Assert.Equal("becky-1", binding.CharacterId);
        Assert.Equal("Becky", binding.CharacterName);
        Assert.Equal(StubIdentityProductionService.PackId, binding.IdentityPackId);
        Assert.Equal(StubIdentityProductionService.FaceAssetId, binding.CanonicalFaceAssetId);
        Assert.Equal(StubIdentityProductionService.FaceRelativePath, binding.FileRelativePath);
        Assert.Equal(StubIdentityProductionService.FaceSha256, binding.Sha256);
    }

    [Fact]
    public async Task EnqueueIdentityEditAsync_JoinsTheSubjectsCandidateBatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new RecordingMediaEditCompilationService();
        var service = fixture.BuildService(recorder, new CapturingDurableQueue(), new StubIdentityProductionService());

        var source = await fixture.Assets.GetImageAsync(fixture.SourceImageId);
        source!.CandidateBatchId = "batch-1";
        source.CandidateDecision = SceneAssetCandidateDecision.Undecided;
        await fixture.Assets.UpsertImageAsync(source);

        var image = await service.EnqueueIdentityEditAsync(fixture.BuildIdentityRequest());

        var persisted = await fixture.Assets.GetImageAsync(image.Id);
        Assert.Equal("batch-1", persisted!.CandidateBatchId);
    }

    [Fact]
    public async Task EnqueueIdentityEditAsync_RequiresTheEditorModelChosenInTheEditorForm()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new RecordingMediaEditCompilationService();
        var service = fixture.BuildService(recorder, new CapturingDurableQueue(), new StubIdentityProductionService());

        var request = fixture.BuildIdentityRequest();
        request.EditorModelId = "  ";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueIdentityEditAsync(request));

        Assert.Contains("editor model", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(recorder.Runs);
    }

    [Fact]
    public async Task EnqueueIdentityEditAsync_RequiresAtLeastOneBoundPerson()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new RecordingMediaEditCompilationService();
        var service = fixture.BuildService(recorder, new CapturingDurableQueue(), new StubIdentityProductionService());

        var request = fixture.BuildIdentityRequest();
        request.Selections = [];

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueIdentityEditAsync(request));

        Assert.Contains("bound person", error.Message, StringComparison.OrdinalIgnoreCase);
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

        public EnqueueSceneAssetImageIdentityEditRequest BuildIdentityRequest() => new()
        {
            AssetId = AssetId,
            SourceImageId = SourceImageId,
            EditorModelId = EditorModelId,
            Selections =
            [
                new ImageIdentitySelection(
                    "man on the left",
                    "left third of the frame",
                    null,
                    "becky-1",
                    "Becky",
                    StubIdentityProductionService.FaceAssetId)
            ]
        };

        public SceneAssetImageEditCompilationService BuildService(
            IMediaEditCompilationService mediaEdits, IDurableBackgroundJobQueue queue)
            => BuildService(mediaEdits, queue, new StubIdentityProductionService());

        public SceneAssetImageEditCompilationService BuildService(
            IMediaEditCompilationService mediaEdits,
            IDurableBackgroundJobQueue queue,
            ISceneImageProductionService productionService)
            => new(
                Assets,
                Edits,
                Storage,
                new ThrowingMultimodalResolver(),
                new ThrowingPromptCompiler(),
                queue,
                new StubDurableSettingsResolver(),
                TimeProvider.System,
                mediaEdits,
                productionService);

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

    /// <summary>
    /// The shared identity-reference resolution: the ONE place that decides whether a chosen face is an
    /// approved <c>Face</c> asset of the character's single approved pack. It records what it was asked so the
    /// enqueue test can prove the asset service forwards the user's selection rather than re-deriving the rule.
    /// </summary>
    private sealed class StubIdentityProductionService : ISceneImageProductionService
    {
        public const string PackId = "pack-1";
        public const string FaceAssetId = "face-1";
        public const string FaceRelativePath = "identity/becky/front.png";
        public const string FaceSha256 = "AB12CD34";

        public List<SceneImageIdentityReferenceSelection> Selections { get; } = [];

        public Task<IReadOnlyList<SceneImageIdentityReadiness>> ResolveCharacterIdentitySelectionsAsync(
            IReadOnlyList<SceneImageIdentityReferenceSelection> selections,
            CancellationToken cancellationToken = default)
        {
            Selections.AddRange(selections);
            return Task.FromResult<IReadOnlyList<SceneImageIdentityReadiness>>(
                selections.Select(selection => new SceneImageIdentityReadiness(
                    selection.CharacterId,
                    string.Empty,
                    PackId,
                    3,
                    FaceAssetId,
                    FaceRelativePath,
                    FaceSha256,
                    SceneImageReferenceFaceView.Front)).ToList());
        }

        // Everything below is unreachable from an identity enqueue; a call would be a real defect.
        public Task<IReadOnlyList<SceneImageIdentityReadiness>> ResolveIdentityReadinessAsync(
            string productionGroupId,
            IReadOnlyList<SceneImageIdentityReferenceSelection>? selections = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CompiledMediaBrief> GetOrCreateStillBriefAsync(string productionGroupId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageProductionGroup> GetOrCreateGroupAsync(CreateSceneImageProductionGroupRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageProductionGroup> SkipIdentityAsync(string groupId, string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageProductionGroup> ClearIdentitySkipAsync(string groupId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageProductionGroup?> GetCurrentGroupAsync(string momentEnrichmentId, string pov, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageProductionGroup?> GetGroupAsync(string groupId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageRecord>> ListAttemptsAsync(string groupId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ApprovedSceneFrameDecision>> ListApprovalDecisionsAsync(string groupId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SetDispositionAsync(string imageId, string groupId, SceneImageAttemptDisposition expectedDisposition, SceneImageAttemptDisposition nextDisposition, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ApprovedSceneFrameDecision> ApproveAsync(string groupId, string imageId, string sha256, string decidedBy, string? note, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageAttemptRetentionPolicy?> GetRetentionPolicyAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneImageAttemptRetentionPolicy> SaveRetentionPolicyAsync(SceneImageAttemptRetentionPolicy policy, long? expectedVersion, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task PurgeRejectedBytesAsync(string imageId, string requestedBy, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneAsset> PromoteApprovedFrameAsync(string groupId, string name, SceneAssetType type, string? associationMetadataJson, string? characterProfileId, string requestedBy, CancellationToken cancellationToken = default)
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
