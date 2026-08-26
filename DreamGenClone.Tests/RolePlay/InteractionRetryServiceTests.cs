using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.Models;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-088: verifies that retry/rewrite commands preserve the original prompt variant —
/// narrative interactions retry through the narrative builder (Narrative variant +
/// validation pipeline), character interactions keep the character rewrite path, and
/// Retry-as "Narrative" is an explicit narrative override.
/// </summary>
public sealed class InteractionRetryServiceTests
{
    // ──────────────────────────────────────────────────────────────────
    // Fakes
    // ──────────────────────────────────────────────────────────────────

    private sealed class RecordingContinuationService : IRolePlayContinuationService
    {
        public int NarrativeAlternativeCallCount { get; private set; }
        public string? LastNarrativeActorName { get; private set; }
        public string? LastNarrativeDirective { get; private set; }
        public string? LastNarrativeCommand { get; private set; }

        public Task<RolePlayInteraction> ContinueAsync(
            RolePlaySession session,
            ContinueAsActor actor,
            string? customActorName,
            PromptIntent intent,
            string promptText,
            Func<string, Task>? onChunk = null,
            CancellationToken cancellationToken = default,
            int? turnIndex = null,
            int? positionInTurn = null,
            int? turnActorCount = null)
            => throw new NotImplementedException();

        public Task<ContinueAsResult> ContinueBatchAsync(
            RolePlaySession session,
            IReadOnlyList<ContinueAsActor> actors,
            bool includeNarrative,
            string? customActorName,
            string promptText,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<RolePlayInteraction> ContinueNarrativeAsync(
            RolePlaySession session,
            string actorName,
            string promptText,
            CancellationToken cancellationToken = default,
            int? turnIndex = null,
            int? turnActorCount = null)
            => throw new NotImplementedException();

        public Task<RolePlayInteraction> ContinueNarrativeAsAlternativeAsync(
            RolePlaySession session,
            string actorName,
            string promptText,
            ResolvedModel resolved,
            string command,
            CancellationToken cancellationToken = default)
        {
            NarrativeAlternativeCallCount++;
            LastNarrativeActorName = actorName;
            LastNarrativeDirective = promptText;
            LastNarrativeCommand = command;
            return Task.FromResult(new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = actorName,
                Content = "Narrative retry output",
                GeneratedByCommand = command,
                GeneratedVariant = PromptVariant.Narrative,
                PromptText = "captured-narrative-prompt"
            });
        }
    }

    private sealed class CapturingCompletionClient : ICompletionClient
    {
        public int GenerateWithReasoningCallCount { get; private set; }
        public string? LastPrompt { get; private set; }

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
            string prompt,
            ResolvedModel resolved,
            CancellationToken cancellationToken = default)
        {
            GenerateWithReasoningCallCount++;
            LastPrompt = prompt;
            return Task.FromResult<(string, string?)>(("character retry output", null));
        }

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class CapturingModelResolver : IModelResolutionService
    {
        public string? LastSessionModelId { get; private set; }

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
        {
            LastSessionModelId = sessionModelId;
            return Task.FromResult(new ResolvedModel(
                "http://localhost:1234",
                "/v1/chat/completions",
                30,
                null,
                sessionModelId ?? "model-default",
                0.7,
                0.9,
                500,
                "provider",
                sessionModelId is not null));
        }

        public Task<ResolvedModel> ResolveImagePromptModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Scene image resolution is not exercised by this test.");

        public Task<ResolvedImageModel> ResolveImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Scene image resolution is not exercised by this test.");

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Identity image resolution is not exercised by this test.");
    }

    private sealed class StubModelSettingsService : IModelSettingsService
    {
        public ModelSettings GetSettings(string sessionId) => new();
        public void UpdateSettings(string sessionId, ModelSettings settings) { }
        public void ClearSettings(string sessionId) { }
    }

    private sealed class FakeRetryEngineService : IRolePlayEngineService
    {
        public int SaveCallCount { get; private set; }

        public Task<RolePlaySession> SaveSessionAsync(RolePlaySession session, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            return Task.FromResult(session);
        }

        public Task<RolePlaySession> CreateSessionAsync(string title, string? scenarioId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RolePlaySession> CreateSessionAsync(CreateRolePlaySessionRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RolePlaySession>> GetSessionsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public void InvalidateSessionCache(string sessionId) { }
        public Task<RolePlaySession> OpenSessionAsync(string sessionId, RolePlaySessionOpenAction action, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RolePlaySession> RebuildAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RolePlaySession> OverrideAdaptiveThemeAsync(string sessionId, string requestedThemeId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> UpdateBehaviorModeAsync(string sessionId, BehaviorMode mode, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RolePlayInteraction> AddInteractionAsync(string sessionId, ContinueAsActor actor, string content, string? customActorName = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RolePlayInteraction> ContinueAsync(string sessionId, ContinueAsActor actor, string? customActorName = null, string? instruction = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RolePlayInteraction> SubmitPromptAsync(
            UnifiedPromptSubmission submission,
            Func<string, Task>? onChunk = null,
            Func<RolePlayInteraction, int, int, bool, Task>? onInteractionCompleted = null,
            Func<int, string, int, Task>? onActorStart = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<ContinueAsResult> ContinueAsAsync(
            ContinueAsRequest request,
            Func<string, Task>? onChunk = null,
            Func<RolePlayInteraction, int, int, bool, Task>? onInteractionCompleted = null,
            Func<int, string, int, Task>? onActorStart = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RolePlayPendingDecisionPrompt?> GetPendingDecisionPromptAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<RolePlayPendingDecisionPrompt>> GetDeferredDecisionPromptsAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<DecisionOutcome?> ApplyDecisionAsync(string sessionId, string decisionPointId, string optionId, string? customResponseText = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> DeferDecisionPointAsync(string sessionId, string decisionPointId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> RestoreDeferredDecisionPointAsync(string sessionId, string decisionPointId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> SkipDecisionPointAsync(string sessionId, string decisionPointId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────

    private static InteractionRetryService CreateService(
        RecordingContinuationService? continuation = null,
        CapturingCompletionClient? completion = null,
        CapturingModelResolver? resolver = null,
        FakeRetryEngineService? engine = null)
    {
        return new InteractionRetryService(
            engine ?? new FakeRetryEngineService(),
            continuation ?? new RecordingContinuationService(),
            completion ?? new CapturingCompletionClient(),
            resolver ?? new CapturingModelResolver(),
            new StubModelSettingsService(),
            new RolePlayTestFactory.NullScenarioService(),
            NullLogger<InteractionRetryService>.Instance);
    }

    private static RolePlaySession CreateSession(RolePlayInteraction original)
    {
        var session = new RolePlaySession
        {
            Id = "session-retry-test",
            Title = "Retry Test",
            PersonaName = "Ken"
        };
        session.Interactions.Add(original);
        return session;
    }

    private static RolePlayInteraction NarrativeInteraction(bool typedVariant = true) => new()
    {
        InteractionType = InteractionType.System,
        ActorName = "Narrative",
        Content = "The trailer surrendered to the heat. Ken watched the light filter through the blinds.",
        GeneratedByCommand = "Narrative",
        GeneratedVariant = typedVariant ? PromptVariant.Narrative : null,
        PromptText = "full-narrative-prompt"
    };

    private static RolePlayInteraction CharacterInteraction() => new()
    {
        InteractionType = InteractionType.Npc,
        ActorName = "Becky",
        Content = "The night had not given me anything but a stiff neck.",
        GeneratedByCommand = "Continue",
        GeneratedVariant = PromptVariant.Character,
        PromptText = "full-character-prompt"
    };

    // ──────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryAsync_NarrativeInteraction_GoesThroughNarrativeBuilder()
    {
        var continuation = new RecordingContinuationService();
        var completion = new CapturingCompletionClient();
        var service = CreateService(continuation, completion);
        var session = CreateSession(NarrativeInteraction());
        var original = session.Interactions[0];

        var alternative = await service.RetryAsync(session, original.Id);

        Assert.Equal(1, continuation.NarrativeAlternativeCallCount);
        Assert.Equal("Retry", continuation.LastNarrativeCommand);
        Assert.Equal("Narrative", continuation.LastNarrativeActorName);
        Assert.Equal(0, completion.GenerateWithReasoningCallCount);

        Assert.Equal(original.Id, alternative.ParentInteractionId);
        Assert.Equal(1, alternative.AlternativeIndex);
        Assert.Equal(1, original.ActiveAlternativeIndex);
        Assert.Equal(InteractionType.System, alternative.InteractionType);
        Assert.Equal("Narrative", alternative.ActorName);
        Assert.Equal(PromptVariant.Narrative, alternative.GeneratedVariant);
        Assert.Equal("Retry", alternative.GeneratedByCommand);
        Assert.Equal("captured-narrative-prompt", alternative.PromptText);
    }

    [Fact]
    public async Task RetryAsync_LegacyNarrativeInteraction_HeuristicRoutesToNarrative()
    {
        var continuation = new RecordingContinuationService();
        var service = CreateService(continuation);
        var session = CreateSession(NarrativeInteraction(typedVariant: false));
        var original = session.Interactions[0];

        await service.RetryAsync(session, original.Id);

        Assert.Equal(1, continuation.NarrativeAlternativeCallCount);
        Assert.Equal("Retry", continuation.LastNarrativeCommand);
    }

    [Fact]
    public async Task RetryAsync_CharacterInteraction_UsesCharacterPath()
    {
        var continuation = new RecordingContinuationService();
        var completion = new CapturingCompletionClient();
        var service = CreateService(continuation, completion);
        var session = CreateSession(CharacterInteraction());
        var original = session.Interactions[0];

        var alternative = await service.RetryAsync(session, original.Id);

        Assert.Equal(0, continuation.NarrativeAlternativeCallCount);
        Assert.Equal(1, completion.GenerateWithReasoningCallCount);
        Assert.NotNull(completion.LastPrompt);

        Assert.Equal(original.Id, alternative.ParentInteractionId);
        Assert.Equal(1, alternative.AlternativeIndex);
        Assert.Equal(PromptVariant.Character, alternative.GeneratedVariant);
        Assert.Equal("Retry", alternative.GeneratedByCommand);
        Assert.Equal(InteractionType.Npc, alternative.InteractionType);
        Assert.Equal("Becky", alternative.ActorName);
    }

    [Theory]
    [InlineData("MakeLonger", "significantly longer")]
    [InlineData("MakeShorter", "shorter and more concise")]
    public async Task RewriteCommands_Narrative_AppendDirectiveAndGoThroughNarrative(
        string command,
        string directiveFragment)
    {
        var continuation = new RecordingContinuationService();
        var service = CreateService(continuation);
        var session = CreateSession(NarrativeInteraction());
        var original = session.Interactions[0];

        RolePlayInteraction alternative = command switch
        {
            "MakeLonger" => await service.MakeLongerAsync(session, original.Id),
            _ => await service.MakeShorterAsync(session, original.Id)
        };

        Assert.Equal(1, continuation.NarrativeAlternativeCallCount);
        Assert.Equal(command, continuation.LastNarrativeCommand);
        Assert.Contains(directiveFragment, continuation.LastNarrativeDirective);
        Assert.Equal(PromptVariant.Narrative, alternative.GeneratedVariant);
    }

    [Fact]
    public async Task AskToRewrite_Narrative_UsesInstructionDirective()
    {
        var continuation = new RecordingContinuationService();
        var service = CreateService(continuation);
        var session = CreateSession(NarrativeInteraction());
        var original = session.Interactions[0];

        var alternative = await service.AskToRewriteAsync(session, original.Id, "add more tension");

        Assert.Equal(1, continuation.NarrativeAlternativeCallCount);
        Assert.Equal("AskToRewrite", continuation.LastNarrativeCommand);
        Assert.Contains("Rewrite instruction: add more tension", continuation.LastNarrativeDirective);
        Assert.Equal(PromptVariant.Narrative, alternative.GeneratedVariant);
    }

    [Fact]
    public async Task RetryWithModel_Narrative_PassesModelOverrideThroughNarrative()
    {
        var continuation = new RecordingContinuationService();
        var resolver = new CapturingModelResolver();
        var service = CreateService(continuation, resolver: resolver);
        var session = CreateSession(NarrativeInteraction());
        var original = session.Interactions[0];

        await service.RetryWithModelAsync(session, original.Id, "model-x");

        Assert.Equal(1, continuation.NarrativeAlternativeCallCount);
        Assert.Equal("RetryWithModel", continuation.LastNarrativeCommand);
        Assert.Equal("model-x", resolver.LastSessionModelId);
    }

    [Fact]
    public async Task RetryAs_Narrative_RoutesThroughNarrativeBuilder()
    {
        var continuation = new RecordingContinuationService();
        var completion = new CapturingCompletionClient();
        var service = CreateService(continuation, completion);
        var session = CreateSession(NarrativeInteraction());
        var original = session.Interactions[0];

        await service.RetryAsAsync(session, original.Id, ContinueAsActor.Npc, "Narrative");

        Assert.Equal(1, continuation.NarrativeAlternativeCallCount);
        Assert.Equal("RetryAs", continuation.LastNarrativeCommand);
        Assert.Equal(0, completion.GenerateWithReasoningCallCount);
    }

    [Fact]
    public async Task RetryAs_CharacterOverride_StaysOnCharacterPath()
    {
        var continuation = new RecordingContinuationService();
        var completion = new CapturingCompletionClient();
        var service = CreateService(continuation, completion);
        var session = CreateSession(NarrativeInteraction());
        var original = session.Interactions[0];

        var alternative = await service.RetryAsAsync(session, original.Id, ContinueAsActor.Npc, "Dean");

        Assert.Equal(0, continuation.NarrativeAlternativeCallCount);
        Assert.Equal(1, completion.GenerateWithReasoningCallCount);
        Assert.Equal(PromptVariant.Character, alternative.GeneratedVariant);
        Assert.Equal("Dean", alternative.ActorName);
    }
}
