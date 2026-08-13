using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SemanticEventInferenceServiceTests
{
    // ──────────────────────────────────────────────────────────────────
    // Stubs
    // ──────────────────────────────────────────────────────────────────

    private sealed class CapturingModelResolver : IModelResolutionService
    {
        public AppFunction? CapturedFunction { get; private set; }
        private readonly bool _shouldThrow;
        private static readonly ResolvedModel DefaultModel = new(
            ProviderBaseUrl: "http://localhost:1234",
            ChatCompletionsPath: "/v1/chat/completions",
            ProviderTimeoutSeconds: 30,
            ApiKeyEncrypted: null,
            ModelIdentifier: "test-model",
            Temperature: 0.7,
            TopP: 0.9,
            MaxTokens: 500,
            ProviderName: "Test",
            IsSessionOverride: false);

        public CapturingModelResolver(bool shouldThrow = false)
        {
            _shouldThrow = shouldThrow;
        }

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
        {
            CapturedFunction = function;
            if (_shouldThrow)
                throw new ModelResolutionException("No model configured for function 'RolePlaySemanticAnalysis'. Configure a default model in Model Manager (/model-manager).");
            return Task.FromResult(DefaultModel);
        }
    }

    private sealed class StubCompletionClient : ICompletionClient
    {
        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult("{\"events\":[]}");

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult("{\"events\":[]}");

        public Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => Task.FromResult("{\"events\":[]}");

        public Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => Task.FromResult("{\"events\":[]}");

        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult((true, "OK"));

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult<(string, string?)>(("{\"events\":[]}", null));

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult<(string, string?)>(("{\"events\":[]}", null));

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => Task.FromResult<(string, string?)>(("{\"events\":[]}", null));

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => Task.FromResult<(string, string?)>(("{\"events\":[]}", null));

    }

    private static SemanticEventInferenceRequest MakeRequest() => new()
    {
        SessionId = "session-test",
        InteractionId = "interaction-test",
        ActorName = "Aria",
        InteractionText = "She smiled warmly.",
        ContextTurns = Array.Empty<string>(),
        AllowedEventIds = new[] { "flirt", "comfort" }
    };

    // ──────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InferAsync_UsesRolePlaySemanticAnalysisFunction()
    {
        var resolver = new CapturingModelResolver();
        var service = new SemanticEventInferenceService(
            new StubCompletionClient(),
            resolver,
            NullLogger<SemanticEventInferenceService>.Instance);

        await service.InferAsync(MakeRequest());

        Assert.Equal(AppFunction.RolePlaySemanticAnalysis, resolver.CapturedFunction);
    }

    [Fact]
    public async Task InferAsync_WhenModelResolutionExceptionThrown_ReturnsFailureResultWithoutRethrowing()
    {
        var resolver = new CapturingModelResolver(shouldThrow: true);
        var service = new SemanticEventInferenceService(
            new StubCompletionClient(),
            resolver,
            NullLogger<SemanticEventInferenceService>.Instance);

        var result = await service.InferAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.ErrorMessage);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task InferAsync_WhenModelResolutionExceptionThrown_DoesNotUseRolePlayGenerationFunction()
    {
        var resolver = new CapturingModelResolver(shouldThrow: true);
        var service = new SemanticEventInferenceService(
            new StubCompletionClient(),
            resolver,
            NullLogger<SemanticEventInferenceService>.Instance);

        await service.InferAsync(MakeRequest());

        // Must have attempted the semantic slot, not fallen back to generation
        Assert.Equal(AppFunction.RolePlaySemanticAnalysis, resolver.CapturedFunction);
        Assert.NotEqual(AppFunction.RolePlayGeneration, resolver.CapturedFunction);
    }
}
