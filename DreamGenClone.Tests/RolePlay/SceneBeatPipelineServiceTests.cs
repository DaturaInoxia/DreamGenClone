using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatPipelineServiceTests
{
    [Fact]
    public async Task EnqueueCatalogue_PersistsImmutableSnapshotsAndIdsOnlyPayload()
    {
        var fixture = CreateFixture();
        try
        {
            var catalogue = await fixture.Service.EnqueueCatalogueAsync(new("session-1", "turn-1"));

            Assert.Equal(1, catalogue.Version);
            Assert.Equal(SceneBeatCatalogueStatus.Pending, catalogue.Status);
            var job = Assert.Single(fixture.Queue.AcceptedJobs);
            using var payload = JsonDocument.Parse(job.PayloadJson);
            Assert.Equal(2, payload.RootElement.EnumerateObject().Count());
            Assert.Equal(catalogue.Id, payload.RootElement.GetProperty("catalogueId").GetString());
            Assert.Equal(catalogue.CurrentAttemptId, payload.RootElement.GetProperty("attemptId").GetString());
            Assert.DoesNotContain("encrypted-secret", catalogue.ExecutionSettingsJson, StringComparison.Ordinal);

            var snapshot = JsonSerializer.Deserialize<SceneBeatCatalogueInputSnapshot>(
                catalogue.InputSnapshotJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(snapshot);
            Assert.Equal(["narrative-1", "npc-1", "user-1"],
                snapshot!.Evidence.Select(item => item.InteractionId).Order().ToArray());
            Assert.DoesNotContain(snapshot.Evidence, item => item.InteractionId == "outside-turn");

            var attempt = await fixture.Repository.GetAttemptAsync(catalogue.CurrentAttemptId!);
            Assert.NotNull(attempt);
            Assert.Contains("AUTHORITATIVE TURN EVIDENCE", attempt!.UserPrompt, StringComparison.Ordinal);
            Assert.Equal(3, job.MaxAttempts);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task EnqueueCatalogue_AnalyzerFailureDoesNotPersistOrEnqueue()
    {
        var fixture = CreateFixture(new InvalidOperationException("analyzer configuration missing"));
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.EnqueueCatalogueAsync(new("session-1", "turn-1")));

            Assert.Contains("configuration missing", exception.Message, StringComparison.Ordinal);
            Assert.Empty(fixture.Queue.AcceptedJobs);
            Assert.Null(await fixture.Repository.GetCurrentByTurnAsync("session-1", "turn-1"));
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task EnqueueCatalogue_RejectsGenerateAgainWhileCurrentVersionIsActive()
    {
        var fixture = CreateFixture();
        try
        {
            await fixture.Service.EnqueueCatalogueAsync(new("session-1", "turn-1"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.EnqueueCatalogueAsync(new("session-1", "turn-1")));

            Assert.Contains("unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(fixture.Queue.AcceptedJobs);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static TestFixture CreateFixture(Exception? analyzerFailure = null)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scene-beat-pipeline-{Guid.NewGuid():N}.db");
        var repository = new SceneBeatCatalogueRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={databasePath}"
        }));
        var now = DateTime.UtcNow;
        var session = new RolePlaySession
        {
            Id = "session-1",
            PersonaName = "Alex",
            PersonaRole = "protagonist",
            PersonaGender = "nonbinary",
            Interactions =
            [
                Interaction("user-1", "Alex", "I enter the hall.", InteractionType.User, now),
                Interaction("npc-1", "Morgan", "Morgan turns toward Alex.", InteractionType.Npc, now.AddSeconds(1)),
                Interaction("narrative-1", "Narrative", "Alex enters and Morgan turns to greet them.", InteractionType.System, now.AddSeconds(2)),
                Interaction("outside-turn", "Narrative", "This belongs to another turn.", InteractionType.System, now.AddSeconds(3))
            ]
        };
        var turn = new RolePlayTurn
        {
            TurnId = "turn-1",
            SessionId = session.Id,
            TurnIndex = 1,
            TurnKind = "UserSubmission",
            TriggerSource = "User",
            InputInteractionId = "user-1",
            OutputInteractionIds = ["npc-1", "narrative-1"],
            StartedUtc = now,
            CompletedUtc = now.AddSeconds(3),
            Status = RolePlayTurnStatus.Completed
        };
        var queue = new RecordingQueue();
        var service = new SceneBeatPipelineService(
            new SessionReader(session),
            new TurnReader(turn),
            new ScenarioReader(),
            new AnalyzerResolver(analyzerFailure),
            new SceneBeatCatalogueSnapshotBuilder(),
            new SceneBeatCatalogueContract(new SceneBeatCatalogueSnapshotBuilder()),
            repository,
            queue,
            TimeProvider.System);
        return new TestFixture(service, repository, queue, databasePath);
    }

    private static RolePlayInteraction Interaction(
        string id,
        string actor,
        string content,
        InteractionType type,
        DateTime createdAt)
        => new() { Id = id, ActorName = actor, Content = content, InteractionType = type, CreatedAt = createdAt };

    private static ResolvedSceneBeatAnalyzer CreateAnalyzer()
        => new(
            "function-default-1",
            "model-1",
            "provider-1",
            new ResolvedModel(
                "https://provider.example",
                "/v1/chat/completions",
                30,
                "encrypted-secret",
                "analyzer-model",
                0.2,
                0.9,
                2048,
                "Provider",
                false)
            {
                SupportsThinkingControl = true,
                ThinkingMode = ThinkingMode.Disabled
            },
            StructuredOutputMode.StrictJsonSchema,
            32768,
            4096,
            2,
            120,
            250,
            [5, 30],
            30,
            8);

    private static void Cleanup(string databasePath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
            }
            catch
            {
            }
        }
    }

    private sealed record TestFixture(
        SceneBeatPipelineService Service,
        SceneBeatCatalogueRepository Repository,
        RecordingQueue Queue,
        string DatabasePath);

    private sealed class SessionReader(RolePlaySession session) : ISceneBeatSessionReader
    {
        public Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<RolePlaySession?>(sessionId == session.Id ? session : null);
    }

    private sealed class TurnReader(RolePlayTurn turn) : IRolePlayTurnReader
    {
        public Task<RolePlayTurn?> GetTurnAsync(string sessionId, string turnId, CancellationToken cancellationToken = default)
            => Task.FromResult<RolePlayTurn?>(sessionId == turn.SessionId && turnId == turn.TurnId ? turn : null);
    }

    private sealed class ScenarioReader : ISceneBeatScenarioReader
    {
        public Task<IReadOnlyList<Character>?> GetCharactersAsync(string scenarioId)
            => Task.FromResult<IReadOnlyList<Character>?>([]);
    }

    private sealed class AnalyzerResolver(Exception? failure) : ISceneBeatAnalyzerResolver
    {
        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
            => failure is null
                ? Task.FromResult(CreateAnalyzer())
                : Task.FromException<ResolvedSceneBeatAnalyzer>(failure);
    }

    private sealed class RecordingQueue : IDurableBackgroundJobQueue
    {
        public List<DurableBackgroundJob> AcceptedJobs { get; } = [];

        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        {
            AcceptedJobs.Add(job);
            return Task.FromResult(true);
        }

        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(AcceptedJobs.SingleOrDefault(job => job.Id == jobId));

        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task WaitForWorkAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}