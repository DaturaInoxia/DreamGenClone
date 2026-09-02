using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageServiceJobTests
{
    private const string CurrentBeatsJson = "[{\"schemaVersion\":3,\"beatId\":\"beat-1\",\"order\":2,\"label\":\"Kitchen confession\",\"visualDescription\":\"She steps closer in the kitchen.\",\"interactionIds\":[\"i1\"],\"subjectCharacterNames\":[\"Wife\"],\"characters\":[{\"name\":\"Wife\",\"profileId\":null,\"involvement\":\"active\",\"physicalLocation\":\"Kitchen\",\"position\":\"beside the kitchen table\",\"actionOrObservation\":\"steps closer\",\"sightline\":\"toward the other person\",\"visibleCharacterNames\":[],\"clothing\":\"blue dress\"}],\"location\":\"Kitchen\",\"timeOfDay\":\"Evening\",\"lighting\":\"warm overhead light\",\"environment\":\"quiet kitchen\",\"mood\":\"intimate\",\"excerpt\":\"She steps closer in the kitchen.\"}]";

    private sealed class CapturingBackgroundJobQueue : IBackgroundJobQueue
    {
        public List<(string JobType, string PayloadJson, string? DedupeKey)> Enqueued { get; } = [];

        public bool Enqueue(string jobType, string payloadJson, string? dedupeKey = null)
        {
            Enqueued.Add((jobType, payloadJson, dedupeKey));
            return true;
        }

        public ValueTask<BackgroundJobEnvelope> DequeueAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used in this test.");
        public void MarkProcessing(string jobId) { }
        public void MarkCompleted(string jobId) { }
        public void MarkFailed(string jobId, string errorMessage) { }
    }

    private sealed class StubSessionService : ISessionService
    {
        private readonly RolePlaySession? _session;
        public StubSessionService(RolePlaySession? session) => _session = session;

        public Task<RolePlaySession?> LoadRolePlaySessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_session);

        public Task SaveStorySessionAsync(StorySession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveRolePlaySessionAsync(RolePlaySession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<StorySession?> LoadStorySessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<StorySession?>(null);
        public Task<IReadOnlyList<SessionListItem>> GetSessionsByTypeAsync(string sessionType, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionListItem>>([]);
        public Task<SessionExportEnvelope?> GetExportEnvelopeAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<SessionExportEnvelope?>(null);
        public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static (SceneImageService service, CapturingBackgroundJobQueue queue, SceneImageRepository repo, SceneImageStorageService storage, string dbPath, string root)
        Build(RolePlaySession? session, string beatsJson = CurrentBeatsJson)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"scene-image-svc-{Guid.NewGuid():N}.db");
        var root = Path.Combine(Path.GetTempPath(), $"scene-image-svc-files-{Guid.NewGuid():N}");
        var persistenceOptions = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False", SceneImageRoot = root });
        new SqlitePersistence(
            persistenceOptions,
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance).InitializeAsync().GetAwaiter().GetResult();
        var repo = new SceneImageRepository(persistenceOptions);
        var editRepository = new SceneImageEditRepository(persistenceOptions);
        var productionGroupRepository = new SceneImageProductionGroupRepository(persistenceOptions);
        var momentEnrichmentRepository = new SceneMomentEnrichmentRepository(persistenceOptions);
        var storage = new SceneImageStorageService(
            persistenceOptions,
            NullLogger<SceneImageStorageService>.Instance);
        var queue = new CapturingBackgroundJobQueue();
        var stateRepository = new RolePlayStateRepository(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" }));
        if (session is not null)
        {
            var turn = stateRepository.StartTurnAsync(session.Id, "Test", "Test", null, "i1").GetAwaiter().GetResult();
            stateRepository.CompleteTurnAsync(session.Id, turn.TurnId, ["i1"], succeeded: true).GetAwaiter().GetResult();
            repo.UpsertBeatAnalysisAsync(new SceneImageBeatAnalysisRecord
            {
                Id = "analysis-1",
                SessionId = session.Id,
                TurnId = turn.TurnId,
                AnchorInteractionId = "i1",
                Status = SceneImageBeatAnalysisStatus.Complete,
                BeatsJson = beatsJson
            }).GetAwaiter().GetResult();
        }
        var service = new SceneImageService(
            new StubSessionService(session),
            repo,
            editRepository,
            storage,
            queue,
            new SceneImageTurnResolver(stateRepository),
            productionGroupRepository,
            momentEnrichmentRepository,
            new CompiledMediaBriefRepository(persistenceOptions),
            NullLogger<SceneImageService>.Instance);
        return (service, queue, repo, storage, dbPath, root);
    }

    private static RolePlaySession MakeSession() => new()
    {
        Id = "s1",
        Interactions = { new RolePlayInteraction { Id = "i1", ActorName = "Wife", Content = "She stepped closer." } }
    };

    private static SceneImagePromptRecord CreatePromptRecord(string outputPrompt = "a draft") => new()
    {
        SessionId = "s1",
        InteractionId = "i1",
        BeatAnalysisId = "analysis-1",
        BeatSnapshotJson = "{\"beatId\":\"beat-1\"}",
        Pov = "Omniscient",
        OutputPrompt = outputPrompt,
        Status = SceneImagePromptStatus.Complete
    };

    private static ScenePromptRequest CreatePromptRequest() => new()
    {
        SessionId = "s1",
        InteractionId = "i1",
        Settings = new SceneImageStudioSettings { Style = "anime" },
        BeatAnalysisId = "analysis-1",
        BeatSnapshotJson = "{\"beatId\":\"beat-1\"}",
        Pov = "Omniscient"
    };

    [Fact]
    public async Task EnqueuePromptAsync_CreatesPendingRecordAndEnqueuesJob()
    {
        var session = MakeSession();
        var (service, queue, repo, _, dbPath, root) = Build(session);
        try
        {
            var request = CreatePromptRequest();
            request.Settings.ImageSize = "1024x1024";
            var record = await service.EnqueuePromptAsync(request);

            Assert.Equal(SceneImagePromptStatus.Pending, record.Status);
            Assert.Equal("s1", record.SessionId);
            Assert.Equal("i1", record.InteractionId);

            var persisted = await repo.GetPromptAsync(record.Id);
            Assert.NotNull(persisted);
            Assert.Equal(SceneImagePromptStatus.Pending, persisted!.Status);
            var persistedBeat = JsonSerializer.Deserialize<SceneImageBeat>(
                persisted.BeatSnapshotJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(persistedBeat);
            Assert.Equal("beat-1", persistedBeat!.BeatId);
            Assert.Equal(2, persistedBeat.Order);
            Assert.Equal("Kitchen confession", persistedBeat.Label);
            Assert.Equal("She steps closer in the kitchen.", persistedBeat.Description);
            Assert.Equal("Kitchen", persistedBeat.Location);
            Assert.Equal("Evening", persistedBeat.TimeOfDay);
            Assert.Equal("Wife", Assert.Single(persistedBeat.Characters).Name);
            Assert.Equal("blue dress", Assert.Single(persistedBeat.Characters).Clothing);

            Assert.Single(queue.Enqueued);
            Assert.Equal(BackgroundJobTypes.SceneImagePromptGeneration, queue.Enqueued[0].JobType);
            Assert.Contains(record.Id, queue.Enqueued[0].DedupeKey, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueuePromptAsync_MissingSession_FailsFast()
    {
        var (service, _, _, _, dbPath, root) = Build(null);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueuePromptAsync(new ScenePromptRequest
            {
                SessionId = "missing",
                InteractionId = "i1",
                Settings = new SceneImageStudioSettings()
            }));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueuePromptAsync_LegacyBeatSchema_RequiresRegeneration()
    {
        var legacyBeats = "[{\"beatId\":\"beat-1\",\"characters\":[{\"name\":\"Wife\"}]}]";
        var (service, _, _, _, dbPath, root) = Build(MakeSession(), legacyBeats);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueuePromptAsync(CreatePromptRequest()));
            Assert.Contains("older schema", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueuePromptAsync_CharacterOutsideBeat_FailsExplicitly()
    {
        var (service, _, _, _, dbPath, root) = Build(MakeSession());
        try
        {
            var request = CreatePromptRequest();
            request.Pov = "Dean";
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueuePromptAsync(request));
            Assert.Contains("not associated", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueuePromptAsync_CanonicalGroupAndStillBrief_CreatesExclusiveCanonicalLineage()
    {
        var (service, queue, repo, _, dbPath, root) = Build(MakeSession());
        try
        {
            var group = await CreateProductionGroupAsync(dbPath, "canonical-prompt", "moment-canonical-prompt");
            var brief = await CreateStillBriefAsync(dbPath, group);

            var record = await service.EnqueuePromptAsync(new ScenePromptRequest
            {
                SessionId = group.SessionId,
                InteractionId = group.InteractionId,
                ProductionGroupId = group.Id,
                CompiledMediaBriefId = brief.Id,
                Pov = group.Pov,
                Settings = new SceneImageStudioSettings { Style = "cinematic" }
            });

            Assert.Equal(group.Id, record.ProductionGroupId);
            Assert.Equal(brief.Id, record.CompiledMediaBriefId);
            Assert.Equal(string.Empty, record.BeatAnalysisId);
            Assert.Equal(string.Empty, record.BeatSnapshotJson);
            Assert.Equal(SceneImagePromptStatus.Pending, record.Status);
            Assert.Single(queue.Enqueued);
            var persisted = await repo.GetPromptAsync(record.Id);
            Assert.Equal(brief.Id, persisted!.CompiledMediaBriefId);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueuePromptAsync_CanonicalBriefFromDifferentGroup_FailsBeforeWriteOrQueue()
    {
        var (service, queue, repo, _, dbPath, root) = Build(MakeSession());
        try
        {
            var group = await CreateProductionGroupAsync(dbPath, "canonical-mismatch", "moment-canonical-mismatch");
            var otherGroup = new SceneImageProductionGroup
            {
                Id = "other-group", SessionId = group.SessionId, InteractionId = group.InteractionId,
                CatalogueId = group.CatalogueId, BeatId = "other-beat", BeatProductionPlanId = group.BeatProductionPlanId,
                BeatProductionPlanVersion = group.BeatProductionPlanVersion, MomentSetId = group.MomentSetId,
                MomentSetVersion = group.MomentSetVersion, MomentId = group.MomentId,
                MomentEnrichmentId = group.MomentEnrichmentId, MomentEnrichmentRevision = group.MomentEnrichmentRevision,
                Pov = group.Pov, CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow
            };
            var brief = await CreateStillBriefAsync(dbPath, otherGroup);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueuePromptAsync(new ScenePromptRequest
            {
                SessionId = group.SessionId, InteractionId = group.InteractionId,
                ProductionGroupId = group.Id, CompiledMediaBriefId = brief.Id, Pov = group.Pov
            }));

            Assert.Contains("does not exactly match", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(queue.Enqueued);
            Assert.Null(await repo.GetLatestCompletedProductionPromptAsync(
                group.SessionId, group.InteractionId, group.Id, brief.Id));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_CreatesPendingRecordAndEnqueuesJob()
    {
        var session = MakeSession();
        var (service, queue, repo, _, dbPath, root) = Build(session);
        try
        {
            // Seed a prompt record first (render references it).
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);

            var record = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                Prompt = "a draft"
            });

            Assert.Equal(SceneImageStatus.Pending, record.Status);
            Assert.Equal(prompt.Id, record.PromptRecordId);
            Assert.Equal("a draft", record.PromptSnapshot);

            Assert.Single(queue.Enqueued);
            Assert.Equal(BackgroundJobTypes.SceneImageRendering, queue.Enqueued[0].JobType);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_ProductionGroup_StampsCompositionLineageAndPreservesLegacyPath()
    {
        var (service, _, repo, _, dbPath, root) = Build(MakeSession());
        try
        {
            var group = await CreateProductionGroupAsync(dbPath, "production-1", "moment-1");
            var brief = await CreateStillBriefAsync(dbPath, group);
            var prompt = CreateCanonicalPromptRecord(group, brief);
            await repo.UpsertPromptAsync(prompt);

            var production = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1", InteractionId = "i1", PromptRecordId = prompt.Id, Prompt = "composition",
                ProductionGroupId = group.Id, CompiledMediaBriefId = brief.Id, Pov = group.Pov
            });
            var legacy = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1", InteractionId = "i1", PromptRecordId = prompt.Id, Prompt = "legacy"
            });

            Assert.Equal(group.Id, production.ProductionGroupId);
            Assert.Equal(brief.Id, production.CompiledMediaBriefId);
            Assert.Equal(SceneImageProductionStage.Composition, production.ProductionStage);
            Assert.Equal(SceneImageAttemptDisposition.Active, production.Disposition);
            Assert.Equal(group.BeatId, production.BeatId);
            Assert.Equal(group.CatalogueId, production.CatalogueId);
            Assert.Equal(group.BeatProductionPlanId, production.BeatProductionPlanId);
            Assert.Equal(group.BeatProductionPlanVersion, production.BeatProductionPlanVersion);
            Assert.Equal(group.MomentSetId, production.MomentSetId);
            Assert.Equal(group.MomentSetVersion, production.MomentSetVersion);
            Assert.Equal(group.MomentId, production.MomentId);
            Assert.Equal(group.MomentEnrichmentId, production.MomentEnrichmentId);
            Assert.Equal(group.MomentEnrichmentRevision, production.MomentEnrichmentRevision);
            Assert.Equal("[{\"referenceId\":\"identity-1\",\"role\":\"CharacterIdentity\",\"required\":true}]", production.TypedReferenceSnapshotJson);
            Assert.Null(legacy.ProductionGroupId);
            Assert.Null(legacy.ProductionStage);
            Assert.Null(legacy.Disposition);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_ProductionGroup_RejectsStaleEnrichment()
    {
        var (service, _, repo, _, dbPath, root) = Build(MakeSession());
        try
        {
            var group = await CreateProductionGroupAsync(dbPath, "stale", "moment-stale");
            var brief = await CreateStillBriefAsync(dbPath, group);
            var prompt = CreateCanonicalPromptRecord(group, brief);
            await repo.UpsertPromptAsync(prompt);
            _ = await CreateCompletedEnrichmentAsync(dbPath, "replacement", "moment-stale");
            var request = new SceneRenderRequest
            {
                SessionId = "s1", InteractionId = "i1", PromptRecordId = prompt.Id, Prompt = "composition",
                ProductionGroupId = group.Id, CompiledMediaBriefId = brief.Id, Pov = group.Pov
            };

            var stale = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueRenderAsync(request));
            Assert.Contains("current completed", stale.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_ProductionGroup_AllowsIdentityControlledComposition()
    {
        var (service, _, repo, _, dbPath, root) = Build(MakeSession());
        try
        {
            var group = await CreateProductionGroupAsync(dbPath, "identity", "moment-identity");
            var brief = await CreateStillBriefAsync(dbPath, group);
            var prompt = CreateCanonicalPromptRecord(group, brief);
            await repo.UpsertPromptAsync(prompt);

            var record = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1", InteractionId = "i1", PromptRecordId = prompt.Id, Prompt = "composition",
                ProductionGroupId = group.Id, CompiledMediaBriefId = brief.Id, Pov = group.Pov,
                RenderMode = SceneImageRenderMode.IdentityControlled,
                IdentityPackId = "identity-pack",
                IdentityPacks = [new IdentityPackSelection { PackId = "identity-pack", CharacterLabel = "Wife — v1" }]
            });

            Assert.Equal(SceneImageRenderMode.IdentityControlled, record.RenderMode);
            Assert.Equal(group.Id, record.ProductionGroupId);
            Assert.Equal(SceneImageProductionStage.Composition, record.ProductionStage);
            Assert.Equal("identity-pack", record.IdentityPackId);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_ProductionRegeneration_AllowsSiblingAndRejectsCrossGroupParent()
    {
        var (service, _, repo, _, dbPath, root) = Build(MakeSession());
        try
        {
            var group = await CreateProductionGroupAsync(dbPath, "branch", "moment-branch");
            var brief = await CreateStillBriefAsync(dbPath, group);
            var prompt = CreateCanonicalPromptRecord(group, brief);
            await repo.UpsertPromptAsync(prompt);
            var parent = new SceneImageRecord
            {
                SessionId = "s1", InteractionId = "i1", PromptRecordId = prompt.Id, PromptSnapshot = "parent",
                Status = SceneImageStatus.Complete, ProductionGroupId = group.Id,
                ProductionStage = SceneImageProductionStage.Composition, Disposition = SceneImageAttemptDisposition.Active
            };
            await repo.InsertImageAsync(parent);
            var request = new SceneRenderRequest
            {
                SessionId = "s1", InteractionId = "i1", PromptRecordId = prompt.Id, Prompt = "sibling",
                ProductionGroupId = group.Id, CompiledMediaBriefId = brief.Id, Pov = group.Pov, RegenerateOfId = parent.Id
            };

            var sibling = await service.EnqueueRenderAsync(request);
            Assert.Equal(parent.Id, sibling.RegenerateOfId);
            Assert.Equal(group.Id, sibling.ProductionGroupId);

            parent.Id = Guid.NewGuid().ToString();
            parent.ProductionGroupId = "other-group";
            await repo.InsertImageAsync(parent);
            request.RegenerateOfId = parent.Id;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueRenderAsync(request));
            Assert.Contains("same production group", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_IdentityMode_MissingPackId_FailsFast()
    {
        var session = MakeSession();
        var (service, _, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                Prompt = "a draft",
                RenderMode = SceneImageRenderMode.IdentityControlled
            }));
            Assert.Contains("identity pack", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_IdentityMode_PersistsRenderModeAndPackId()
    {
        var session = MakeSession();
        var (service, queue, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);

            var record = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                Prompt = "a draft",
                RenderMode = SceneImageRenderMode.IdentityControlled,
                IdentityPackId = "pack-1"
            });

            Assert.Equal(SceneImageStatus.Pending, record.Status);
            Assert.Equal(SceneImageRenderMode.IdentityControlled, record.RenderMode);
            Assert.Equal("pack-1", record.IdentityPackId);
            Assert.Single(queue.Enqueued);
            Assert.Equal(BackgroundJobTypes.SceneImageRendering, queue.Enqueued[0].JobType);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_IdentityMode_MultiPack_PersistsFullSelectionAndFirstPackId()
    {
        var session = MakeSession();
        var (service, queue, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);

            var record = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                Prompt = "two people",
                RenderMode = SceneImageRenderMode.IdentityControlled,
                IdentityPackId = "pack-1",
                IdentityPacks =
                [
                    new IdentityPackSelection { PackId = "pack-1", CharacterLabel = "Dean — v1", Strength = 0.8 },
                    new IdentityPackSelection { PackId = "pack-2", CharacterLabel = "Becky — v1" }
                ]
            });

            Assert.Equal(SceneImageRenderMode.IdentityControlled, record.RenderMode);
            Assert.Equal("pack-1", record.IdentityPackId);
            Assert.NotNull(record.IdentityPacksJson);
            var persisted = JsonSerializer.Deserialize<List<IdentityPackSelection>>(
                record.IdentityPacksJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted!.Count);
            Assert.Equal("pack-1", persisted[0].PackId);
            Assert.Equal(0.8, persisted[0].Strength);
            Assert.Equal("pack-2", persisted[1].PackId);
            Assert.Null(persisted[1].Strength);
            Assert.Single(queue.Enqueued);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueEditAsync_CreatesPendingEditRecordAndEnqueuesDedicatedJob()
    {
        var session = MakeSession();
        var (service, queue, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);
            var source = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                PromptSnapshot = "original prompt",
                Status = SceneImageStatus.Complete,
                FileRelativePath = "s1/source.png"
            };
            await repo.InsertImageAsync(source);
            var (editSession, attempt, revision) = await CreateReadyEditAsync(dbPath, source, "Change only the hand position.");

            var record = await service.EnqueueEditAsync(new SceneImageEditRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                SourceImageId = source.Id,
                EditSessionId = editSession.Id,
                CompilationAttemptId = attempt.Id,
                PromptRevisionId = revision.Id,
                SourceImageSha256 = editSession.SourceImageSha256,
                PromptSha256 = revision.PromptSha256
            });

            Assert.Equal(SceneImageStatus.Pending, record.Status);
            Assert.Equal(SceneImageOperation.Edit, record.Operation);
            Assert.Equal(source.Id, record.SourceImageId);
            Assert.Equal("Change only the hand position.", record.PromptSnapshot);
            Assert.Equal(editSession.Id, record.EditSessionId);
            Assert.Equal(attempt.Id, record.EditCompilationAttemptId);
            Assert.Equal(revision.Id, record.EditPromptRevisionId);
            Assert.Equal(attempt.RawIntent, record.EditIntentSnapshot);
            Assert.Contains(attempt.CompilerSchemaVersion, record.EditCompilerProvenanceJson, StringComparison.Ordinal);
            Assert.Single(queue.Enqueued);
            Assert.Equal(BackgroundJobTypes.SceneImageEditing, queue.Enqueued[0].JobType);
            Assert.Contains(record.Id, queue.Enqueued[0].DedupeKey, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueEditAsync_ProductionAttempt_InheritsExactLineageAsFinishAndRejectsPurgedSource()
    {
        var (service, _, repo, _, dbPath, root) = Build(MakeSession());
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);
            var source = new SceneImageRecord
            {
                SessionId = "s1", InteractionId = "i1", PromptRecordId = prompt.Id, PromptSnapshot = "source",
                Status = SceneImageStatus.Complete, FileRelativePath = "s1/source.png",
                ProductionGroupId = "group-1", ProductionStage = SceneImageProductionStage.Composition,
                Disposition = SceneImageAttemptDisposition.Shortlisted, CatalogueId = "catalogue-1", BeatId = "beat-1",
                BeatProductionPlanId = "plan-1", BeatProductionPlanVersion = 3,
                MomentSetId = "set-1", MomentSetVersion = 4, MomentId = "moment-1",
                MomentEnrichmentId = "enrichment-1", MomentEnrichmentRevision = 2,
                TypedReferenceSnapshotJson = "[{\"role\":\"CharacterIdentity\"}]"
            };
            await repo.InsertImageAsync(source);
            var (editSession, attempt, revision) = await CreateReadyEditAsync(dbPath, source, "finish only");
            var request = new SceneImageEditRequest
            {
                SessionId = "s1", InteractionId = "i1", SourceImageId = source.Id, EditSessionId = editSession.Id,
                CompilationAttemptId = attempt.Id, PromptRevisionId = revision.Id,
                SourceImageSha256 = editSession.SourceImageSha256, PromptSha256 = revision.PromptSha256
            };

            var edit = await service.EnqueueEditAsync(request);
            Assert.Equal(source.Id, edit.SourceImageId);
            Assert.Equal(SceneImageProductionStage.Finish, edit.ProductionStage);
            Assert.Equal(SceneImageAttemptDisposition.Active, edit.Disposition);
            Assert.Equal(source.ProductionGroupId, edit.ProductionGroupId);
            Assert.Equal(source.CatalogueId, edit.CatalogueId);
            Assert.Equal(source.BeatProductionPlanVersion, edit.BeatProductionPlanVersion);
            Assert.Equal(source.MomentEnrichmentId, edit.MomentEnrichmentId);
            Assert.Equal(source.MomentEnrichmentRevision, edit.MomentEnrichmentRevision);
            Assert.Equal(source.TypedReferenceSnapshotJson, edit.TypedReferenceSnapshotJson);

            source.Id = Guid.NewGuid().ToString();
            source.BytesPurgedUtc = DateTime.UtcNow;
            await repo.InsertImageAsync(source);
            request.SourceImageId = source.Id;
            var purged = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueEditAsync(request));
            Assert.Contains("purged", purged.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueEditAsync_IncompleteSource_FailsFast()
    {
        var session = MakeSession();
        var (service, _, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);
            var source = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                PromptSnapshot = "original prompt",
                Status = SceneImageStatus.Pending
            };
            await repo.InsertImageAsync(source);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueEditAsync(new SceneImageEditRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                SourceImageId = source.Id,
                EditSessionId = "edit-session",
                CompilationAttemptId = "attempt",
                PromptRevisionId = "revision",
                SourceImageSha256 = new string('A', 64),
                PromptSha256 = new string('B', 64)
            }));

            Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueEditAsync_WrongPromptChecksum_CreatesNoEditOrJob()
    {
        var session = MakeSession();
        var (service, queue, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);
            var source = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                PromptSnapshot = "original prompt",
                Status = SceneImageStatus.Complete,
                FileRelativePath = "s1/source.png"
            };
            await repo.InsertImageAsync(source);
            var (editSession, attempt, revision) = await CreateReadyEditAsync(dbPath, source, "Change only the lighting.");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueEditAsync(new SceneImageEditRequest
            {
                SessionId = source.SessionId,
                InteractionId = source.InteractionId,
                SourceImageId = source.Id,
                EditSessionId = editSession.Id,
                CompilationAttemptId = attempt.Id,
                PromptRevisionId = revision.Id,
                SourceImageSha256 = editSession.SourceImageSha256,
                PromptSha256 = new string('F', 64)
            }));

            Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(queue.Enqueued);
            Assert.Single(await repo.ListImagesByInteractionAsync(source.SessionId, source.InteractionId));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueEditAsync_MissingCompiledRevision_FailsFast()
    {
        var (service, _, _, _, dbPath, root) = Build(MakeSession());
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueEditAsync(new SceneImageEditRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                SourceImageId = "source"
            }));

            Assert.Contains("compiled edit", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    private static async Task<(SceneImageEditSession Session, SceneImageEditCompilationAttempt Attempt, SceneImageEditPromptRevision Revision)> CreateReadyEditAsync(
        string dbPath,
        SceneImageRecord source,
        string prompt)
    {
        var repository = new SceneImageEditRepository(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));
        var session = new SceneImageEditSession
        {
            SourceImageId = source.Id,
            SourceImageSha256 = new string('A', 64),
            SessionId = source.SessionId,
            InteractionId = source.InteractionId,
            Status = SceneImageEditSessionStatus.Active
        };
        await repository.CreateSessionAsync(session);
        var attempt = new SceneImageEditCompilationAttempt
        {
            EditSessionId = session.Id,
            RawIntent = "change only the hand position",
            SourceImageSha256 = session.SourceImageSha256,
            Status = SceneImageEditCompilationAttemptStatus.Pending,
            ResolvedModelSnapshotJson = "{\"modelIdentifier\":\"qwen-vl\"}",
            CompilerSchemaVersion = "scene-image-edit-compiler-v1",
            SystemPromptVersion = "qwen-edit-rules-v2"
        };
        await repository.CreateAttemptAsync(attempt);
        attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
        await repository.UpdateAttemptAsync(attempt);
        attempt.Status = SceneImageEditCompilationAttemptStatus.Ready;
        await repository.UpdateAttemptAsync(attempt);
        var revision = new SceneImageEditPromptRevision
        {
            CompilationAttemptId = attempt.Id,
            Prompt = prompt,
            RevisionKind = SceneImageEditPromptRevisionKind.CompilerOutput,
            PromptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)))
        };
        await repository.CreateRevisionAsync(revision);
        return (session, attempt, revision);
    }

    private static async Task<SceneImageProductionGroup> CreateProductionGroupAsync(
        string dbPath,
        string suffix,
        string momentId)
    {
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var enrichment = await CreateCompletedEnrichmentAsync(dbPath, suffix, momentId);
        var group = new SceneImageProductionGroup
        {
            Id = $"group-{suffix}", SessionId = "s1", InteractionId = "i1",
            CatalogueId = enrichment.CatalogueId, BeatId = enrichment.BeatId,
            BeatProductionPlanId = enrichment.BeatProductionPlanId,
            BeatProductionPlanVersion = enrichment.BeatProductionPlanVersion,
            MomentSetId = enrichment.MomentSetId, MomentSetVersion = enrichment.MomentSetVersion,
            MomentId = enrichment.MomentId, MomentEnrichmentId = enrichment.Id,
            MomentEnrichmentRevision = enrichment.Revision, Pov = "Director",
            Status = SceneImageProductionGroupStatus.Draft, IdentityPolicy = SceneImageIdentityPolicy.Required,
            CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow
        };
        await new SceneImageProductionGroupRepository(options).CreateAsync(group);
        return group;
    }

    private static SceneImagePromptRecord CreateCanonicalPromptRecord(
        SceneImageProductionGroup group,
        CompiledMediaBrief brief) => new()
    {
        SessionId = group.SessionId,
        InteractionId = group.InteractionId,
        BeatAnalysisId = string.Empty,
        BeatSnapshotJson = string.Empty,
        ProductionGroupId = group.Id,
        CompiledMediaBriefId = brief.Id,
        Pov = group.Pov,
        OutputPrompt = "canonical draft",
        Status = SceneImagePromptStatus.Complete
    };

    private static async Task<CompiledMediaBrief> CreateStillBriefAsync(
        string dbPath,
        SceneImageProductionGroup group)
    {
        var now = DateTime.UtcNow;
        var brief = new CompiledMediaBrief(
            $"brief-{Guid.NewGuid():N}", MediaProductionKind.StillImage,
            "still-profile", "1", "canonical", "deterministic", "1", "canonical-request-v1",
            new CompiledMediaLineage(
                group.CatalogueId, group.BeatId, group.BeatProductionPlanId, group.BeatProductionPlanVersion,
                group.MomentSetId, group.MomentSetVersion, group.MomentId,
                group.MomentEnrichmentId, group.MomentEnrichmentRevision),
            [group.BeatProductionPlanId, group.MomentSetId, group.MomentId, group.MomentEnrichmentId],
            "{\"typedReferences\":[{\"referenceId\":\"identity-1\",\"role\":\"CharacterIdentity\",\"required\":true}]}",
            "{}", "{\"entries\":[]}", MediaCompilerStatus.Complete, null, null, now, now);
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        await new CompiledMediaBriefRepository(options).CreateAsync(brief);
        return brief;
    }

    private static async Task<SceneMomentEnrichment> CreateCompletedEnrichmentAsync(
        string dbPath,
        string suffix,
        string momentId)
    {
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new SceneMomentEnrichmentRepository(options);
        var now = DateTime.UtcNow;
        var enrichment = new SceneMomentEnrichment
        {
            Id = $"enrichment-{suffix}", CatalogueId = "catalogue-1", BeatId = "beat-1",
            BeatProductionPlanId = "plan-1", BeatProductionPlanVersion = 3,
            MomentSetId = $"set-{momentId}", MomentSetVersion = 4, MomentId = momentId,
            SchemaVersion = 1, PromptContractVersion = "moment-enrichment-v1",
            MomentSnapshotJson = "{}", TurnEvidenceSnapshotJson = "{}", ExecutionSettingsJson = "{}",
            CreatedUtc = now, UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = $"attempt-{suffix}", OwnerRecordId = enrichment.Id, AttemptNumber = 1, JobId = $"job-{suffix}",
            SystemPrompt = "system", UserPrompt = "user", ValidationDetailsJson = "{}", InputCharacters = 1,
            CreatedUtc = now, UpdatedUtc = now
        };
        enrichment.CurrentAttemptId = attempt.Id;
        await repository.CreateRevisionAsync(enrichment, attempt);
        Assert.True(await repository.TryStartAttemptAsync(enrichment.Id, attempt.Id, "model", "provider", DateTime.UtcNow));
        Assert.True(await repository.TryCompleteAttemptAsync(enrichment.Id, attempt, new SceneMomentEnrichmentData("{}", "[]", "{}"), DateTime.UtcNow));
        return (await repository.GetAsync(enrichment.Id))!;
    }

    [Fact]
    public async Task EnqueueRenderAsync_SnapshotsSettingsAndStyle()
    {
        var session = MakeSession();
        var (service, _, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);

            var record = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                Prompt = "a draft",
                SettingsJson = "{\"Style\":\"cartoon\",\"ImageSize\":\"768x768\",\"AllowExplicitImage\":true}"
            });

            Assert.Equal("cartoon", record.Style);
            Assert.Equal("768x768", record.ImageSize);
            Assert.Contains("cartoon", record.SettingsJson, StringComparison.Ordinal);
            Assert.Contains("AllowExplicitImage", record.SettingsJson, StringComparison.Ordinal);

            var persisted = await repo.GetImageAsync(record.Id);
            Assert.NotNull(persisted);
            Assert.Equal("cartoon", persisted!.Style);
            Assert.Contains("cartoon", persisted.SettingsJson, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_EmptyPrompt_FailsFast()
    {
        var session = MakeSession();
        var (service, _, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord("draft");
            await repo.UpsertPromptAsync(prompt);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                Prompt = "   "
            }));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task DeleteImageAsync_RemovesRowAndFile()
    {
        var session = MakeSession();
        var (service, _, repo, storage, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord("draft");
            await repo.UpsertPromptAsync(prompt);

            var image = new SceneImageRecord { SessionId = "s1", InteractionId = "i1", PromptRecordId = prompt.Id, PromptSnapshot = "draft" };
            await repo.InsertImageAsync(image);

            // Save a real file under the session dir.
            await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            image.FileRelativePath = await storage.SaveAsync("s1", $"{image.Id}.png", stream);
            image.Status = SceneImageStatus.Complete;
            await repo.InsertImageAsync(image);
            Assert.True(File.Exists(Path.Combine(root, image.FileRelativePath)));

            await service.DeleteImageAsync("s1", image.Id);

            Assert.Null(await repo.GetImageAsync(image.Id));
            Assert.False(File.Exists(Path.Combine(root, image.FileRelativePath)));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueuePromptAsync_RefineInstruction_PersistedOnRecord()
    {
        var session = MakeSession();
        var (service, _, repo, _, dbPath, root) = Build(session);
        try
        {
            var request = CreatePromptRequest();
            request.RefineInstruction = "  more atmospheric  ";
            var record = await service.EnqueuePromptAsync(request);

            var persisted = await repo.GetPromptAsync(record.Id);
            Assert.NotNull(persisted);
            Assert.Equal("more atmospheric", persisted!.RefineInstruction);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueuePromptAsync_BlankRefineInstruction_StaysNull()
    {
        var session = MakeSession();
        var (service, _, repo, _, dbPath, root) = Build(session);
        try
        {
            var request = CreatePromptRequest();
            request.RefineInstruction = "   ";
            var record = await service.EnqueuePromptAsync(request);

            var persisted = await repo.GetPromptAsync(record.Id);
            Assert.NotNull(persisted);
            Assert.Null(persisted!.RefineInstruction);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task EnqueueRenderAsync_RegenerateSetsRegenerateOfId()
    {
        var session = MakeSession();
        var (service, queue, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord();
            await repo.UpsertPromptAsync(prompt);

            var parent = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                Prompt = "a draft"
            });

            var regenerated = await service.EnqueueRenderAsync(new SceneRenderRequest
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = prompt.Id,
                Prompt = "a draft - v2",
                RegenerateOfId = parent.Id
            });

            Assert.NotEqual(parent.Id, regenerated.Id);
            Assert.Equal(parent.Id, regenerated.RegenerateOfId);
            Assert.Equal(2, queue.Enqueued.Count);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task UpdatePromptOutputAsync_PersistsEditedText()
    {
        var session = MakeSession();
        var (service, _, repo, _, dbPath, root) = Build(session);
        try
        {
            var prompt = CreatePromptRecord("original");
            await repo.UpsertPromptAsync(prompt);

            await service.UpdatePromptOutputAsync("s1", prompt.Id, "edited version");

            var persisted = await repo.GetPromptAsync(prompt.Id);
            Assert.NotNull(persisted);
            Assert.Equal("edited version", persisted!.OutputPrompt);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    private static void Cleanup(string dbPath, string root)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); } catch { /* best effort */ }
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }
}
