using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// T075: Regression tests for the encounter-memory enrichment fix (session e12d27a6).
/// 1) The handler must use the reasoning-aware completion path and persist ONLY the content
///    (never chain-of-thought reasoning), matching SemanticEventInferenceService /
///    RolePlayContinuationService. Previously a plain GenerateAsync fell back to reasoning as
///    content, producing 35K-char "memories" that ballooned the prompt to 149K.
/// 2) An over-length response is rejected (keeps the template summary) via MaxLlmSummaryChars.
/// </summary>
public sealed class EncounterSummaryJobHandlerReasoningTests
{
    private const string RecordId = "abcdef0123456789abcdef0123456789";

    private static readonly ResolvedModel TestModel = new(
        ProviderBaseUrl: "http://localhost",
        ChatCompletionsPath: "/v1/chat/completions",
        ProviderTimeoutSeconds: 30,
        ApiKeyEncrypted: null,
        ModelIdentifier: "test-model",
        Temperature: 0.7,
        TopP: 0.9,
        MaxTokens: 512,
        ProviderName: "test",
        IsSessionOverride: false);

    private static RolePlaySession MakeSession()
    {
        return new RolePlaySession
        {
            Interactions =
            [
                new RolePlayInteraction
                {
                    ActorName = "Becky",
                    Content = "She finally surrendered, letting him lead her to the counter.",
                    InteractionType = InteractionType.Npc
                }
            ]
        };
    }

    private static EncounterSummaryRecord MakeRecord()
    {
        return new EncounterSummaryRecord
        {
            Id = RecordId,
            SessionId = "test-session",
            CharacterId = "Becky",
            SummaryType = EncounterSummaryType.EncounterCompletion,
            EncounterNumber = 1,
            StartInteractionIndex = 0,
            EndInteractionIndex = 0
        };
    }

    private static EncounterSummaryJobHandler BuildHandler(
        ICompletionClient completionClient,
        CapturingSummaryService summaryService,
        int maxLlmSummaryChars = 4000)
    {
        var options = Options.Create(new RolePlayMemoryOptions
        {
            EnableLlmSummaryEnhancement = true,
            MaxLlmSummaryChars = maxLlmSummaryChars
        });

        return new EncounterSummaryJobHandler(
            new StubSessionService(),
            new StubStateRepository(),
            summaryService,
            completionClient,
            new StubModelResolutionService(),
            options,
            NullLogger<EncounterSummaryJobHandler>.Instance);
    }

    private static BackgroundJobEnvelope MakeJob()
    {
        var payload = new EncounterSummaryJobPayload
        {
            SessionId = "test-session",
            CycleIndex = 1,
            SummaryType = nameof(EncounterSummaryType.EncounterCompletion),
            SummaryId = RecordId
        };
        return new BackgroundJobEnvelope
        {
            JobType = BackgroundJobTypes.EncounterSummaryEnhancement,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload)
        };
    }

    [Fact]
    public async Task Enrichment_UsesReasoningAwarePath_AndPersistsOnlyContent_NotReasoning()
    {
        var reasoningDump = "The user wants me to write Becky's private first-person memory after Encounter 1." +
                            " Let me parse the source material carefully." + new string('x', 30000);
        var realMemory = "He turned me over the counter and I let him. I had never felt that free before.";

        var completionClient = new CapturingCompletionClient(
            content: realMemory,
            reasoning: reasoningDump);
        var summaryService = new CapturingSummaryService();
        var handler = BuildHandler(completionClient, summaryService);

        await handler.HandleAsync(MakeJob(), CancellationToken.None);

        // The fix: the reasoning-aware stream path must be used (not plain GenerateAsync).
        Assert.True(completionClient.StreamReasoningCalled,
            "Handler must call StreamGenerateWithReasoningAsync (the reasoning-aware path).");
        Assert.False(completionClient.PlainGenerateCalled,
            "Handler must NOT call plain GenerateAsync (it falls back to reasoning-as-content).");

        // Only the content must be persisted — never the chain-of-thought.
        var persisted = Assert.Single(summaryService.Persisted);
        Assert.Equal(RecordId, persisted.SummaryId);
        Assert.Equal(realMemory, persisted.LlmSummary);
        Assert.DoesNotContain("The user wants me to write", persisted.LlmSummary);
    }

    [Fact]
    public async Task Enrichment_RejectsOverLengthResponse_KeepingTemplateSummary()
    {
        var hugeContent = new string('y', 9000); // well over MaxLlmSummaryChars = 4000
        var completionClient = new CapturingCompletionClient(
            content: hugeContent,
            reasoning: null);
        var summaryService = new CapturingSummaryService();
        var handler = BuildHandler(completionClient, summaryService, maxLlmSummaryChars: 4000);

        await handler.HandleAsync(MakeJob(), CancellationToken.None);

        // Over-length summary must NOT be persisted (template summary is kept downstream).
        Assert.Empty(summaryService.Persisted);
    }

    [Fact]
    public async Task Enrichment_PersistsWithinLengthResponse()
    {
        var shortMemory = "A short, three-sentence memory that stays under the cap.";
        var completionClient = new CapturingCompletionClient(
            content: shortMemory,
            reasoning: null);
        var summaryService = new CapturingSummaryService();
        var handler = BuildHandler(completionClient, summaryService, maxLlmSummaryChars: 4000);

        await handler.HandleAsync(MakeJob(), CancellationToken.None);

        var persisted = Assert.Single(summaryService.Persisted);
        Assert.Equal(shortMemory, persisted.LlmSummary);
    }

    // ── Stubs ──────────────────────────────────────────────────────

    private sealed class CapturingSummaryService : IEncounterSummaryService
    {
        public List<(string SummaryId, string LlmSummary)> Persisted { get; } = [];

        public Task UpdateLlmSummaryAsync(
            string summaryId,
            string llmSummary,
            DateTime llmEnhancedUtc,
            string? enrichmentPrompt = null,
            CancellationToken cancellationToken = default)
        {
            Persisted.Add((summaryId, llmSummary));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EncounterSummaryRecord>> GenerateTemplatesAsync(
            NarrativePhaseTransitionEvent transitionEvent,
            AdaptiveScenarioState v2State,
            IReadOnlySet<string>? allowedCharacterIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>([]);

        public Task<IReadOnlyList<EncounterSummaryRecord>> GenerateEncounterCompletionTemplatesAsync(
            AdaptiveScenarioState v2State,
            int encounterNumber,
            string detectionEvidence,
            int startInteractionIndex,
            int endInteractionIndex,
            IReadOnlyDictionary<string, string>? characterInteractionsTexts = null,
            IReadOnlySet<string>? allowedCharacterIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>([]);

        public Task SaveAsync(EncounterSummaryRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<EncounterSummaryRecord>> LoadForSessionAsync(
            string sessionId,
            int maxMilestones,
            int currentCycleIndex,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>([]);
    }

    private sealed class CapturingCompletionClient : ICompletionClient
    {
        private readonly string _content;
        private readonly string? _reasoning;

        public bool PlainGenerateCalled { get; private set; }
        public bool StreamReasoningCalled { get; private set; }

        public CapturingCompletionClient(string content, string? reasoning)
        {
            _content = content;
            _reasoning = reasoning;
        }

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            PlainGenerateCalled = true;
            return Task.FromResult(_content);
        }

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            PlainGenerateCalled = true;
            return Task.FromResult(_content);
        }

        public Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => Task.FromResult(_content);

        public Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => Task.FromResult(_content);

        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string Message)> CheckModelHealthAsync(
            string providerBaseUrl,
            string chatCompletionsPath,
            int timeoutSeconds,
            string? decryptedApiKey,
            string modelIdentifier,
            CancellationToken cancellationToken = default)
            => Task.FromResult((true, "ok"));

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
            string prompt,
            ResolvedModel resolved,
            CancellationToken cancellationToken = default)
            => Task.FromResult((_content, _reasoning));

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
            string systemMessage,
            string userMessage,
            ResolvedModel resolved,
            CancellationToken cancellationToken = default)
            => Task.FromResult((_content, _reasoning));

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
            string prompt,
            ResolvedModel resolved,
            Func<string, Task> onChunk,
            CancellationToken cancellationToken = default)
        {
            StreamReasoningCalled = true;
            return Task.FromResult((_content, _reasoning));
        }

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
            string systemMessage,
            string userMessage,
            ResolvedModel resolved,
            Func<string, Task> onChunk,
            CancellationToken cancellationToken = default)
        {
            StreamReasoningCalled = true;
            return Task.FromResult((_content, _reasoning));
        }
    }

    private sealed class StubSessionService : ISessionService
    {
        public Task<RolePlaySession?> LoadRolePlaySessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<RolePlaySession?>(MakeSession());

        public Task SaveStorySessionAsync(DreamGenClone.Web.Domain.Story.StorySession session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveRolePlaySessionAsync(RolePlaySession session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<DreamGenClone.Web.Domain.Story.StorySession?> LoadStorySessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<DreamGenClone.Web.Domain.Story.StorySession?>(null);

        public Task<IReadOnlyList<SessionListItem>> GetSessionsByTypeAsync(string sessionType, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SessionListItem>>([]);

        public Task<SessionExportEnvelope?> GetExportEnvelopeAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<SessionExportEnvelope?>(null);

        public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class StubStateRepository : IRolePlayStateRepository
    {
        public Task<IReadOnlyList<EncounterSummaryRecord>> LoadEncounterSummariesForSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>([MakeRecord()]);

        public Task<RolePlayTurn> StartTurnAsync(string sessionId, string turnKind, string triggerSource, string? initiatedByActorName, string? inputInteractionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new RolePlayTurn());

        public Task CompleteTurnAsync(string sessionId, string turnId, IReadOnlyList<string> outputInteractionIds, bool succeeded, string? failureReason = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RolePlayTurn>>([]);

        public Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveAdaptiveStateSemanticFieldsAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveAdaptiveStateLocationFieldsAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<AdaptiveScenarioState?>(new AdaptiveScenarioState());

        public Task SaveCandidateEvaluationsAsync(IReadOnlyList<ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScenarioCandidateEvaluation>>([]);

        public Task SaveTransitionEventAsync(NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NarrativePhaseTransitionEvent>>([]);

        public Task SaveCompletionMetadataAsync(ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveDecisionPointAsync(DecisionPoint decisionPoint, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DecisionPoint>>([]);

        public Task<IReadOnlyList<DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DecisionOption>>([]);

        public Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveFormulaVersionReferenceAsync(string sessionId, FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveUnsupportedSessionErrorAsync(UnsupportedSessionError error, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UnsupportedSessionError>>([]);

        public Task SaveThemeMachineDiagnosticEventsAsync(IReadOnlyList<ThemeMachineDiagnosticEvent> events, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ThemeMachineDiagnosticEvent>>([]);

        public Task SaveEncounterSummaryAsync(EncounterSummaryRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateEncounterSummaryLlmAsync(string summaryId, string llmSummary, DateTime llmEnhancedUtc, string? enrichmentPrompt = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubModelResolutionService : IModelResolutionService
    {
        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(TestModel);

        public Task<ResolvedModel> ResolveImagePromptModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Scene image resolution is not exercised by this test.");

        public Task<ResolvedImageModel> ResolveImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Scene image resolution is not exercised by this test.");

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Identity image resolution is not exercised by this test.");

        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Scene image resolution is not exercised by this test.");

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Identity image resolution is not exercised by this test.");

        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(bool identityCapableOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Scene image model listing is not exercised by this test.");
    }
}
