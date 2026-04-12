using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.Assistants;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayAssistantServiceAdaptiveContextTests
{
    [Fact]
    public async Task GenerateSuggestionAsync_IncludesSteeringProfileAndManualPinInAdaptiveContext()
    {
        var completion = new RecordingCompletionClient();
        var resolver = new StubModelResolutionService();
        var contextManager = new AssistantContextManager(NullLogger<AssistantContextManager>.Instance);
        var service = new RolePlayAssistantService(
            completion,
            resolver,
            contextManager,
            NullLogger<RolePlayAssistantService>.Instance);

        var context = new RolePlayAssistantContext
        {
            SessionId = "session-1",
            SelectedThemeProfileId = "ranking-a",
            SelectedIntensityProfileId = "tone-a",
            SelectedSteeringProfileId = "style-a",
            IntensityFloorOverride = "Emotional",
            IntensityCeilingOverride = "Erotic",
            IsIntensityManuallyPinned = true
        };

        var response = await service.GenerateSuggestionAsync(context, "How do I make this slower?");

        Assert.Equal("ok", response);
        Assert.Contains("steering=style-a", completion.LastUserMessage, StringComparison.Ordinal);
        Assert.Contains("baseIntensity=tone-a", completion.LastUserMessage, StringComparison.Ordinal);
        Assert.Contains("manualPin=on", completion.LastUserMessage, StringComparison.Ordinal);
    }

    private sealed class RecordingCompletionClient : ICompletionClient
    {
        public string LastUserMessage { get; private set; } = string.Empty;

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult("ok");

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            LastUserMessage = userMessage;
            return Task.FromResult("ok");
        }

        public async Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            await onChunk("ok");
            return "ok";
        }

        public async Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            LastUserMessage = userMessage;
            await onChunk("ok");
            return "ok";
        }

        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult((true, "ok"));
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
        {
            return Task.FromResult(new ResolvedModel(
                ProviderBaseUrl: "http://localhost",
                ChatCompletionsPath: "/v1/chat/completions",
                ProviderTimeoutSeconds: 30,
                ApiKeyEncrypted: null,
                ModelIdentifier: "test-model",
                Temperature: 0.7,
                TopP: 0.9,
                MaxTokens: 512,
                ProviderName: "test",
                IsSessionOverride: false));
        }
    }
}
