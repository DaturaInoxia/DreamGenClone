using System.Net;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Together answers 400 for two reasons that are recoverable client-side: some models are stream-only
/// (Qwen3.7/3.8 family) and some reject the default top_p (Kimi-K3 requires [0.95, 1]). Both must be
/// handled inside the client so every caller — Prompt Tester included — can talk to every model.
/// </summary>
public sealed class CompletionClientRetryTests
{
    private static ResolvedModel Resolve(double topP = 0.9) => new(
        ProviderBaseUrl: "https://api.together.ai",
        ChatCompletionsPath: "/v1/chat/completions",
        ProviderTimeoutSeconds: 30,
        ApiKeyEncrypted: "sekret",
        ModelIdentifier: "Qwen/Qwen3.8-Flash",
        Temperature: 0.7,
        TopP: topP,
        MaxTokens: 500,
        ProviderName: "TogetherAI",
        IsSessionOverride: false);

    [Fact]
    public async Task GenerateAsync_StreamingRequired_RetriesWithStreamAndAssemblesContent()
    {
        var bodies = new List<JsonDocument>();
        var callCount = 0;

        var client = BuildClient(req =>
        {
            bodies.Add(JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));
            callCount++;

            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"error":{"code":"streaming_required","message":"This model only supports streaming. Set \"stream\": true."}}""")
                };
            }

            var sse = """
                data: {"choices":[{"delta":{"content":"Hello "}}]}

                data: {"choices":[{"delta":{"content":"world"}}]}

                data: [DONE]

                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
        });

        var content = await client.GenerateAsync("hello", Resolve(), CancellationToken.None);

        Assert.Equal("Hello world", content);
        Assert.Equal(2, bodies.Count);
        Assert.False(bodies[0].RootElement.TryGetProperty("stream", out _));
        Assert.True(bodies[1].RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task GenerateAsync_StreamOnlyModelThatStreamsNothing_DoesNotRecurse()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"error":{"code":"streaming_required","message":"This model only supports streaming."}}""")
        });

        // Both the plain call and the streaming fallback are rejected; the call must fail, not loop.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GenerateAsync("hello", Resolve(), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_RejectedTopP_ClampsToProviderRangeAndRetries()
    {
        var bodies = new List<JsonDocument>();
        var callCount = 0;

        var client = BuildClient(req =>
        {
            bodies.Add(JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));
            callCount++;

            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"error":{"type":"invalid_request_error","code":"invalid_top_p","message":"top_p must be within [0.95, 1], got 0.9","param":null}}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""")
            };
        });

        var content = await client.GenerateAsync("hello", Resolve(), CancellationToken.None);

        Assert.Equal("ok", content);
        Assert.Equal(2, bodies.Count);
        Assert.Equal(0.9, bodies[0].RootElement.GetProperty("top_p").GetDouble(), 3);
        Assert.Equal(0.95, bodies[1].RootElement.GetProperty("top_p").GetDouble(), 3);
    }

    [Fact]
    public async Task GenerateAsync_UnrelatedBadRequest_DoesNotRetry()
    {
        var callCount = 0;
        var client = BuildClient(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"model not available"}}""")
            };
        });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GenerateAsync("hello", Resolve(), CancellationToken.None));

        // The UI must show the provider's reason, not a bare "400".
        Assert.Contains("model not available", exception.Message);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GenerateWithReasoningAsync_SendsSeedAndThinkingKwargs_WhenTheModelDeclaresThem()
    {
        JsonDocument? captured = null;
        var client = BuildClient(req =>
        {
            captured = JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"ok","reasoning_content":"why"},"finish_reason":"stop"}]}""")
            };
        });

        var model = Resolve() with
        {
            Seed = 12345,
            SupportsThinkingControl = true,
            ThinkingMode = ThinkingMode.Disabled
        };

        var (content, reasoning) = await client.GenerateWithReasoningAsync("system", "hello", model, CancellationToken.None);

        Assert.Equal("ok", content);
        Assert.Equal("why", reasoning);
        Assert.NotNull(captured);
        Assert.Equal(12345, captured!.RootElement.GetProperty("seed").GetInt64());
        Assert.False(captured.RootElement.GetProperty("chat_template_kwargs").GetProperty("thinking").GetBoolean());
    }

    [Fact]
    public async Task GenerateWithReasoningAsync_OmitsSeedAndThinking_WhenNotDeclared()
    {
        JsonDocument? captured = null;
        var client = BuildClient(req =>
        {
            captured = JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""")
            };
        });

        await client.GenerateWithReasoningAsync("system", "hello", Resolve(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.False(captured!.RootElement.TryGetProperty("seed", out _));
        Assert.False(captured.RootElement.TryGetProperty("chat_template_kwargs", out _));
    }

    [Fact]
    public async Task GenerateWithReasoningAsync_StreamingRequired_KeepsReasoningAndContent()
    {
        var callCount = 0;
        var client = BuildClient(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"error":{"code":"streaming_required","message":"This model only supports streaming."}}""")
                };
            }

            var sse = """
                data: {"choices":[{"delta":{"reasoning_content":"thinking "}}]}

                data: {"choices":[{"delta":{"reasoning_content":"hard"}}]}

                data: {"choices":[{"delta":{"content":"Final prose."}}]}

                data: [DONE]

                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
        });

        var (content, reasoning) = await client.GenerateWithReasoningAsync("system", "hello", Resolve(), CancellationToken.None);

        Assert.Equal("Final prose.", content);
        Assert.Equal("thinking hard", reasoning);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GenerateWithReasoningAsync_ProviderAbortsStream_ThrowsWithPartialText()
    {
        var callCount = 0;
        var client = BuildClient(_ =>
        {
            callCount++;

            // The model is stream-only, so the request arrives at the streaming path via the normal fallback.
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"error":{"code":"streaming_required","message":"This model only supports streaming."}}""")
                };
            }

            var sse = """
                data: {"choices":[{"delta":{"content":"He pulled her close"}}]}

                data: {"choices":[{"delta":{"content":" and then"}}]}

                data: {"error":{"message":"invalid or incomplete chat provider response","type":"server_error","code":"managed_chat_error"}}

                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
        });

        var exception = await Assert.ThrowsAsync<IncompleteCompletionException>(
            () => client.GenerateWithReasoningAsync("system", "hello", Resolve(), CancellationToken.None));

        Assert.Equal("He pulled her close and then", exception.PartialContent);
        Assert.Contains("incomplete chat provider response", exception.Message);
    }

    [Fact]
    public async Task GenerateWithReasoningAsync_DeepSeekProvider_SendsThinkingTypeNotChatTemplateKwargs()
    {
        JsonDocument? captured = null;
        var client = BuildClient(req =>
        {
            captured = JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""")
            };
        });

        var model = Resolve() with
        {
            ProviderBaseUrl = "https://api.deepseek.com",
            ProviderName = "DeepSeek",
            SupportsThinkingControl = true,
            ThinkingMode = ThinkingMode.Disabled
        };

        await client.GenerateWithReasoningAsync("system", "hello", model, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("disabled", captured!.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(captured.RootElement.TryGetProperty("chat_template_kwargs", out _));
    }

    [Fact]
    public async Task GenerateWithReasoningAsync_DeepSeekWithoutThinkingMode_OmitsThinking()
    {
        JsonDocument? captured = null;
        var client = BuildClient(req =>
        {
            captured = JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""")
            };
        });

        var model = Resolve() with
        {
            ProviderBaseUrl = "https://api.deepseek.com",
            ProviderName = "DeepSeek",
            SupportsThinkingControl = true,
            ThinkingMode = ThinkingMode.Default
        };

        await client.GenerateWithReasoningAsync("system", "hello", model, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.False(captured!.RootElement.TryGetProperty("thinking", out _));
    }

    private static CompletionClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(
            new FakeHttpClientFactory(new StubHttpMessageHandler(responder)),
            new FakeEncryption(),
            NullLogger<CompletionClient>.Instance);

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        // A real IHttpClientFactory hands out a new HttpClient per call (sharing the handler), and the retry
        // path depends on that: BaseAddress cannot be reassigned on an instance that already sent a request.
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
