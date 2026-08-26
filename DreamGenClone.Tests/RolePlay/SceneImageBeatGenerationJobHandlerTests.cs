using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageBeatGenerationJobHandlerTests
{
    private const string SessionId = "session-1";
    private const string InteractionId = "interaction-1";
    private const string AnalysisId = "analysis-1";

    [Fact]
    public async Task HandleAsync_UsesReasoningAwareContentAndNeverPlainGeneration()
    {
        const string content = "{\"beats\":[{\"schemaVersion\":3,\"beatId\":\"b1\",\"order\":1,\"label\":\"Arrival\",\"visualDescription\":\"She steps into the hall.\",\"interactionIds\":[\"interaction-1\",\"narrative-1\"],\"characters\":[{\"name\":\"Wife\",\"profileId\":null,\"involvement\":\"active\",\"physicalLocation\":\"hall\",\"position\":\"near the door\",\"actionOrObservation\":\"steps into the hall\",\"sightline\":\"forward into the hall\",\"visibleCharacterNames\":[],\"clothing\":\"blue shirt\"}],\"location\":\"hall\",\"timeOfDay\":\"evening\",\"lighting\":\"warm light\",\"environment\":\"entry hall\",\"mood\":\"expectant\"}]}";
        var completion = new CapturingCompletionClient(content, "This reasoning is deliberately not JSON.");
        var fixture = await CreateFixtureAsync(completion);
        try
        {
            await fixture.Handler.HandleAsync(CreateJob(), CancellationToken.None);

            Assert.True(completion.ReasoningCalled);
            Assert.False(completion.PlainGenerateCalled);
            Assert.False(completion.StreamReasoningCalled);
            var persisted = await fixture.ImageRepository.GetBeatAnalysisByTurnAsync(SessionId, fixture.TurnId);
            Assert.NotNull(persisted);
            Assert.Equal(SceneImageBeatAnalysisStatus.Complete, persisted!.Status);
            Assert.Equal(content, persisted.RawModelResponse);
            Assert.Equal("This reasoning is deliberately not JSON.", persisted.ReasoningContent);
            Assert.Contains("\"beatId\":\"b1\"", persisted.BeatsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("reasoning", persisted.RawModelResponse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenFinalContentIsMalformed_PersistsExactContentAsFailure()
    {
        const string malformedContent = "{\"beats\":[}";
        const string validJsonInReasoning = "{\"beats\":[]}";
        var completion = new CapturingCompletionClient(malformedContent, validJsonInReasoning);
        var fixture = await CreateFixtureAsync(completion);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => fixture.Handler.HandleAsync(CreateJob(), CancellationToken.None));

            Assert.True(completion.ReasoningCalled);
            Assert.False(completion.PlainGenerateCalled);
            Assert.False(completion.StreamReasoningCalled);
            var persisted = await fixture.ImageRepository.GetBeatAnalysisByTurnAsync(SessionId, fixture.TurnId);
            Assert.NotNull(persisted);
            Assert.Equal(SceneImageBeatAnalysisStatus.Failed, persisted!.Status);
            Assert.Equal(malformedContent, persisted.RawModelResponse);
            Assert.Equal(validJsonInReasoning, persisted.ReasoningContent);
            Assert.Equal("test-model", persisted.ModelIdentifier);
            Assert.False(string.IsNullOrWhiteSpace(persisted.ErrorMessage));
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static async Task<TestFixture> CreateFixtureAsync(CapturingCompletionClient completion)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scene-image-beat-handler-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={databasePath}" });
        var imageRepository = new SceneImageRepository(options);
        var stateRepository = new RolePlayStateRepository(options);
        var sessionService = new RolePlayTestFactory.FakeSessionService();
        await sessionService.SaveRolePlaySessionAsync(new RolePlaySession
        {
            Id = SessionId,
            Interactions =
            {
                new RolePlayInteraction
                {
                    Id = InteractionId,
                    ActorName = "Wife",
                    Content = "She steps into the hall.",
                    CreatedAt = DateTime.UtcNow
                },
                new RolePlayInteraction
                {
                    Id = "narrative-1",
                    ActorName = "Narrative",
                    Content = "She steps into the hall.",
                    CreatedAt = DateTime.UtcNow.AddSeconds(1)
                }
            }
        });
        var turn = await stateRepository.StartTurnAsync(SessionId, "Test", "Test", null, InteractionId);
        await stateRepository.CompleteTurnAsync(SessionId, turn.TurnId, [InteractionId, "narrative-1"], succeeded: true);
        await imageRepository.UpsertBeatAnalysisAsync(new SceneImageBeatAnalysisRecord
        {
            Id = AnalysisId,
            SessionId = SessionId,
            TurnId = turn.TurnId,
            AnchorInteractionId = InteractionId,
            Status = SceneImageBeatAnalysisStatus.Pending
        });

        var handler = new SceneImageBeatGenerationJobHandler(
            sessionService,
            imageRepository,
            new StubModelResolutionService(),
            completion,
            new RolePlayTestFactory.NullScenarioService(),
            new SceneImageTurnResolver(stateRepository),
            new SceneImageBeatAnalysisService(),
            new RolePlayTestFactory.NullRolePlayDebugEventSink(),
            NullLogger<SceneImageBeatGenerationJobHandler>.Instance);
        return new TestFixture(handler, imageRepository, turn.TurnId, databasePath);
    }

    private static BackgroundJobEnvelope CreateJob() => new()
    {
        JobType = BackgroundJobTypes.SceneImageBeatGeneration,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(new SceneImageBeatGenerationJobPayload
        {
            SessionId = SessionId,
            InteractionId = InteractionId,
            AnalysisRecordId = AnalysisId
        })
    };

    private static void Cleanup(string databasePath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix); } catch { }
        }
    }

    private sealed record TestFixture(
        SceneImageBeatGenerationJobHandler Handler,
        SceneImageRepository ImageRepository,
        string TurnId,
        string DatabasePath);

    private sealed class StubModelResolutionService : IModelResolutionService
    {
        private static readonly ResolvedModel Model = new(
            "http://localhost",
            "/v1/chat/completions",
            30,
            null,
            "test-model",
            0.2,
            0.9,
            8000,
            "test-provider",
            false);

        public Task<ResolvedModel> ResolveAsync(AppFunction function, string? sessionModelId = null, double? sessionTemperature = null, double? sessionTopP = null, int? sessionMaxTokens = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Model);

        public Task<ResolvedModel> ResolveImagePromptModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Model);

        public Task<ResolvedImageModel> ResolveImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CapturingCompletionClient : ICompletionClient
    {
        private readonly Queue<string> _contents;
        private readonly string? _reasoning;

        public CapturingCompletionClient(string content, string? reasoning)
            : this([content], reasoning)
        {
        }

        public CapturingCompletionClient(IEnumerable<string> contents, string? reasoning)
        {
            _contents = new Queue<string>(contents);
            _reasoning = reasoning;
        }

        public bool PlainGenerateCalled { get; private set; }
        public bool ReasoningCalled { get; private set; }
        public bool StreamReasoningCalled { get; private set; }

        private string NextContent()
            => _contents.Count > 1 ? _contents.Dequeue() : _contents.Peek();

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            PlainGenerateCalled = true;
            return Task.FromResult(NextContent());
        }

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            PlainGenerateCalled = true;
            return Task.FromResult(NextContent());
        }

        public Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default) => Task.FromResult(NextContent());
        public Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default) => Task.FromResult(NextContent());
        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default) => Task.FromResult((true, "ok"));
        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            ReasoningCalled = true;
            return Task.FromResult((NextContent(), _reasoning));
        }

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            ReasoningCalled = true;
            return Task.FromResult((NextContent(), _reasoning));
        }
        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            StreamReasoningCalled = true;
            return Task.FromResult((NextContent(), _reasoning));
        }

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            StreamReasoningCalled = true;
            return Task.FromResult((NextContent(), _reasoning));
        }
    }
}