using System.Net;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CompletionClientReasoningTests
{
    [Fact]
    public async Task GenerateWithReasoningAsync_ReasoningOnlyContinuation_DoesNotAppendReasoningToContent()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Response(content: "{\"beats\":[", reasoning: "initial reasoning", finishReason: "length"),
            Response(content: null, reasoning: "reasoning that must never enter JSON", finishReason: "stop")
        ]);
        var handler = new StubHttpMessageHandler(_ => responses.Dequeue());
        var client = new CompletionClient(
            new FakeHttpClientFactory(handler),
            new FakeEncryption(),
            NullLogger<CompletionClient>.Instance);

        var (content, reasoning) = await client.GenerateWithReasoningAsync(
            "system",
            "user",
            Resolve(),
            CancellationToken.None);

        Assert.Equal("{\"beats\":[", content);
        Assert.Equal("initial reasoning", reasoning);
        Assert.Equal(2, handler.RequestCount);
        Assert.DoesNotContain("reasoning that must never enter JSON", content, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Response(string? content, string? reasoning, string finishReason)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new { role = "assistant", content, reasoning_content = reasoning },
                        finish_reason = finishReason
                    }
                }
            }))
        };

    private static ResolvedModel Resolve() => new(
        "https://api.together.ai",
        "/v1/chat/completions",
        30,
        null,
        "deepseek-test",
        0.2,
        0.9,
        8000,
        "TogetherAI",
        false);

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
