using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
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
        var repo = new SceneImageRepository(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" }));
        var storage = new SceneImageStorageService(
            Options.Create(new PersistenceOptions { SceneImageRoot = root }),
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
            storage,
            queue,
            new SceneImageTurnResolver(stateRepository),
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
